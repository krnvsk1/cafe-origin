using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Egais
{
    public record SyncResult(int WayBills, int RegInfos, int Tickets, int Skipped, List<string> Errors, int ResendRequested = 0);

    public record ActLine(int LineId, decimal? RealQuantity);

    public class EgaisService
    {
        private static readonly SemaphoreSlim SyncLock = new(1, 1);
        // Документы, уже прочитанные из УТМ (когда не удаляем их — чтобы не скачивать заново)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Seen = new();

        private readonly ApplicationDbContext _context;
        private readonly UtmClient _utm;
        private readonly IInventoryService _inventory;
        private readonly EgaisSettingsProvider _settings;
        private readonly ILogger<EgaisService> _logger;

        public EgaisService(ApplicationDbContext context, UtmClient utm, IInventoryService inventory,
            EgaisSettingsProvider settings, ILogger<EgaisService> logger)
        {
            _context = context;
            _utm = utm;
            _inventory = inventory;
            _settings = settings;
            _logger = logger;
        }

        private EgaisOptions _options => _settings.Current;

        public static DateTime? LastSyncAt { get; private set; }

        /// <summary>Последний ответ ЕГАИС на «какие накладные без акта» и что с ними сделали — для диагностики</summary>
        public record NoAnswerState(string WbRegId, string? Number, DateTime? Date, string? ShipperFsrarId, string Action);
        public static DateTime? LastNoAnswerAt { get; private set; }
        public static IReadOnlyList<NoAnswerState> LastNoAnswer { get; private set; } = Array.Empty<NoAnswerState>();

        public static bool IsSeen(string url) => Seen.ContainsKey(url);
        public static string? LastSyncError { get; private set; }

        // ================= Синхронизация =================

        /// <summary>Забирает из УТМ накладные, справки о регистрации и квитанции; обработанные удаляет из УТМ</summary>
        public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
        {
            if (!await SyncLock.WaitAsync(0, ct))
                return new SyncResult(0, 0, 0, 0, new List<string> { "Синхронизация уже идёт" });

            try
            {
                var refs = await _utm.ListIncomingAsync(ct);
                int wayBills = 0, regInfos = 0, tickets = 0, skipped = 0, resend = 0;
                var errors = new List<string>();

                // Сначала накладные, потом справки к ним, потом квитанции
                int Order(string type) => type.StartsWith("WayBill", StringComparison.OrdinalIgnoreCase) && !type.Contains("Act", StringComparison.OrdinalIgnoreCase) ? 0
                    : type.Equals("FORM2REGINFO", StringComparison.OrdinalIgnoreCase) || type.Equals("FORMBREGINFO", StringComparison.OrdinalIgnoreCase) ? 1
                    : 2;

                foreach (var r in refs.OrderBy(r => Order(r.Type)))
                {
                    if (Seen.ContainsKey(r.Url)) continue;
                    try
                    {
                        if (r.Type.Equals("ReplyNATTN", StringComparison.OrdinalIgnoreCase))
                        {
                            resend += await HandleNoAnswerAsync(r, ct);
                            Seen[r.Url] = 1;
                            if (_options.DeleteProcessed) await _utm.DeleteAsync(r.Url, ct);
                            continue;
                        }

                        var handled = await HandleAsync(r, ct);
                        switch (handled)
                        {
                            case "waybill": wayBills++; break;
                            case "reginfo": regInfos++; break;
                            case "ticket": tickets++; break;
                            default: skipped++; break;
                        }
                        if (handled != null)
                        {
                            Seen[r.Url] = 1;
                            // Удаляем из УТМ только то, что забрали к себе, и только если это разрешено
                            if (_options.DeleteProcessed) await _utm.DeleteAsync(r.Url, ct);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "ЕГАИС: не удалось обработать {Url}", r.Url);
                        errors.Add($"{r.Type}: {ex.Message}");
                    }
                }

                LastSyncAt = DateTime.UtcNow;
                LastSyncError = errors.Count > 0 ? string.Join("; ", errors.Take(3)) : null;
                return new SyncResult(wayBills, regInfos, tickets, skipped, errors, resend);
            }
            catch (HttpRequestException ex)
            {
                LastSyncError = $"УТМ недоступен ({_options.UtmUrl}): {ex.Message}";
                throw new EgaisException(LastSyncError);
            }
            finally
            {
                SyncLock.Release();
            }
        }

        /// <summary>
        /// Попросить ЕГАИС прислать список накладных, по которым ещё нет акта (ответ ReplyNATTN придёт во входящие).
        /// Нужно, если накладные из очереди УТМ уже забрала другая программа (например, iiko).
        /// </summary>
        public async Task RequestUnacceptedAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_options.FsrarId)) throw new EgaisException("Не указан ФСРАР ID заведения");
            var xml = EgaisXml.BuildQuery(_options.FsrarId, "QueryNATTN", "КОД", _options.FsrarId);
            var (ok, response) = await _utm.SendAsync("QueryNATTN", xml, ct);
            if (!ok) throw new EgaisException($"УТМ не принял запрос: {UtmClient.ExtractError(response)}");
        }

        /// <summary>По списку неподтверждённых накладных запрашиваем повторную отправку тех, которых у нас нет</summary>
        private async Task<int> HandleNoAnswerAsync(UtmDocumentRef r, CancellationToken ct)
        {
            var list = EgaisXml.ParseNoAnswer(EgaisXml.Load(await _utm.GetAsync(r.Url, ct)));
            var known = await _context.EgaisDocuments.AsNoTracking()
                .Where(d => d.WbRegId != null).Select(d => d.WbRegId!).ToListAsync(ct);
            var requested = 0;
            var states = new List<NoAnswerState>();
            foreach (var ttn in list)
            {
                if (known.Contains(ttn.WbRegId))
                {
                    states.Add(new NoAnswerState(ttn.WbRegId, ttn.Number, ttn.Date, ttn.ShipperFsrarId, "уже есть в сервисе"));
                    continue;
                }
                var xml = EgaisXml.BuildQuery(_options.FsrarId, "QueryResendDoc", "WBREGID", ttn.WbRegId);
                var (ok, response) = await _utm.SendAsync("QueryResendDoc", xml, ct);
                if (ok) requested++;
                states.Add(new NoAnswerState(ttn.WbRegId, ttn.Number, ttn.Date, ttn.ShipperFsrarId,
                    ok ? "запрошена повторная отправка" : $"УТМ не принял запрос: {UtmClient.ExtractError(response)}"));
            }
            LastNoAnswer = states;
            LastNoAnswerAt = DateTime.UtcNow;
            _logger.LogInformation("ЕГАИС: без акта {Count} накладных, запрошено повторно {Requested}", list.Count, requested);
            return requested;
        }

        private async Task<string?> HandleAsync(UtmDocumentRef r, CancellationToken ct)
        {
            var type = r.Type.ToLowerInvariant();
            if (!(type.StartsWith("waybill") || type is "form2reginfo" or "formbreginfo" or "ticket"))
                return null; // чужие документы (остатки, справки и т.п.) не трогаем

            var xml = await _utm.GetAsync(r.Url, ct);
            var doc = EgaisXml.Load(xml);
            var kind = EgaisXml.DetectType(doc) ?? r.Type;

            if (kind.StartsWith("WayBill", StringComparison.OrdinalIgnoreCase) && !kind.Contains("Act", StringComparison.OrdinalIgnoreCase)
                && !kind.Contains("Ticket", StringComparison.OrdinalIgnoreCase))
            {
                await SaveWayBillAsync(EgaisXml.ParseWayBill(doc), kind, xml, ct);
                return "waybill";
            }
            if (kind.StartsWith("TTNInform", StringComparison.OrdinalIgnoreCase))
            {
                await ApplyRegInfoAsync(EgaisXml.ParseRegInfo(doc), ct);
                return "reginfo";
            }
            if (kind.Equals("Ticket", StringComparison.OrdinalIgnoreCase))
            {
                return await ApplyTicketAsync(EgaisXml.ParseTicket(doc), ct) ? "ticket" : null;
            }
            return null;
        }

        private async Task SaveWayBillAsync(WayBillData wb, string kind, string xml, CancellationToken ct)
        {
            var exists = await _context.EgaisDocuments.AnyAsync(d =>
                d.ShipperFsrarId == wb.ShipperFsrarId && d.Number == wb.Number && d.Date == wb.Date, ct);
            if (exists) return;

            // Запомненные сопоставления «код ЕГАИС → позиция склада»
            var codes = wb.Positions.Select(p => p.AlcCode).Where(c => c != null).Distinct().ToList();
            var mapped = await _context.StockItems.AsNoTracking()
                .Where(i => i.EgaisAlcCode != null && codes.Contains(i.EgaisAlcCode) && !i.IsArchived)
                .ToDictionaryAsync(i => i.EgaisAlcCode!, i => i.Id, ct);

            var document = new EgaisDocument
            {
                DocType = kind,
                Identity = wb.Identity,
                Number = Trunc(wb.Number, 50),
                Date = wb.Date,
                ShipperFsrarId = wb.ShipperFsrarId,
                ShipperInn = wb.ShipperInn,
                ShipperName = wb.ShipperName is null ? null : Trunc(wb.ShipperName, 255),
                Status = EgaisStatuses.AwaitingRegId,
                RawXml = xml,
                CreatedAt = DateTime.UtcNow
            };
            foreach (var p in wb.Positions)
            {
                document.Lines.Add(new EgaisDocumentLine
                {
                    Identity = Trunc(p.Identity, 30),
                    ProductName = Trunc(p.ProductName, 255),
                    AlcCode = p.AlcCode,
                    Capacity = p.Capacity,
                    AlcVolume = p.AlcVolume,
                    UnitType = p.UnitType,
                    Quantity = p.Quantity,
                    Price = p.Price,
                    InformF2RegId = p.F2RegId,
                    RealQuantity = p.Quantity,
                    StockItemId = p.AlcCode != null && mapped.TryGetValue(p.AlcCode, out var id) ? id : null
                });
            }

            _context.EgaisDocuments.Add(document);
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("🍺 ЕГАИС: накладная № {Number} от {Shipper}, позиций: {Count}", wb.Number, wb.ShipperName, wb.Positions.Count);
        }

        private async Task ApplyRegInfoAsync(RegInfoData info, CancellationToken ct)
        {
            var doc = await _context.EgaisDocuments.Include(d => d.Lines)
                .Where(d => d.Status == EgaisStatuses.AwaitingRegId || d.WbRegId == null)
                .Where(d => (info.Identity != null && d.Identity == info.Identity)
                            || (d.Number == info.WbNumber && (info.ShipperFsrarId == null || d.ShipperFsrarId == info.ShipperFsrarId)))
                .OrderByDescending(d => d.Id)
                .FirstOrDefaultAsync(ct);
            if (doc == null)
                throw new EgaisException($"Не найдена накладная № {info.WbNumber} для справки о регистрации");

            doc.WbRegId = info.WbRegId;
            foreach (var line in doc.Lines)
                if (info.F2RegIdByPosition.TryGetValue(line.Identity, out var reg))
                    line.InformF2RegId = reg;
            if (doc.Status == EgaisStatuses.AwaitingRegId) doc.Status = EgaisStatuses.Ready;
            await _context.SaveChangesAsync(ct);
        }

        private async Task<bool> ApplyTicketAsync(TicketData t, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(t.RegId)) return false;
            var doc = await _context.EgaisDocuments.FirstOrDefaultAsync(d => d.WbRegId == t.RegId, ct);
            if (doc == null) return false;

            doc.ResultComment = Trunc(t.Comment, 1000);
            doc.ResultAt = DateTime.UtcNow;
            if (doc.Status == EgaisStatuses.ActSent && t.Accepted == false)
                doc.Status = EgaisStatuses.Error;
            else if (doc.Status == EgaisStatuses.ActSent && t.Accepted == true)
                doc.Status = doc.ActDecision switch
                {
                    nameof(ActDecision.Rejected) => EgaisStatuses.Rejected,
                    nameof(ActDecision.Differences) => EgaisStatuses.Differences,
                    _ => EgaisStatuses.Accepted
                };
            await _context.SaveChangesAsync(ct);
            return true;
        }

        // ================= Сопоставление и акт =================

        /// <summary>Привязать позиции накладной к позициям склада (и запомнить код ЕГАИС у позиции)</summary>
        public async Task SaveMappingAsync(int documentId, IEnumerable<(int LineId, int? StockItemId)> mapping)
        {
            var doc = await _context.EgaisDocuments.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == documentId)
                      ?? throw new EgaisException("Накладная не найдена");

            var ids = mapping.Where(m => m.StockItemId.HasValue).Select(m => m.StockItemId!.Value).Distinct().ToList();
            var items = await _context.StockItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

            foreach (var (lineId, stockItemId) in mapping)
            {
                var line = doc.Lines.FirstOrDefault(l => l.Id == lineId);
                if (line == null) continue;
                if (stockItemId.HasValue && !items.ContainsKey(stockItemId.Value)) throw new EgaisException("Позиция склада не найдена");
                line.StockItemId = stockItemId;
                if (stockItemId.HasValue && line.AlcCode != null && items[stockItemId.Value].EgaisAlcCode == null)
                    items[stockItemId.Value].EgaisAlcCode = line.AlcCode;
            }
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Отправить акт по накладной. При приёмке (полной или с расхождениями) создаётся проведённый приход на склад.
        /// </summary>
        public async Task<EgaisDocument> SendActAsync(int documentId, ActDecision decision, string? note, IReadOnlyCollection<ActLine> lines, int? userId)
        {
            if (_options.SendActs && string.IsNullOrWhiteSpace(_options.FsrarId))
                throw new EgaisException("Не указан ФСРАР ID заведения — укажите его в настройках ЕГАИС");

            var doc = await _context.EgaisDocuments.Include(d => d.Lines).ThenInclude(l => l.StockItem)
                .FirstOrDefaultAsync(d => d.Id == documentId) ?? throw new EgaisException("Накладная не найдена");

            if (doc.Status is not (EgaisStatuses.Ready or EgaisStatuses.Error) && !(doc.Status == EgaisStatuses.AwaitingRegId && !_options.SendActs))
                throw new EgaisException(doc.Status == EgaisStatuses.AwaitingRegId
                    ? "ЕГАИС ещё не прислал регистрационный номер накладной — нажмите «Проверить входящие» чуть позже"
                    : "По этой накладной акт уже отправлен");
            if (string.IsNullOrEmpty(doc.WbRegId) && _options.SendActs) throw new EgaisException("Нет регистрационного номера накладной");

            foreach (var l in lines)
            {
                var line = doc.Lines.FirstOrDefault(x => x.Id == l.LineId) ?? throw new EgaisException("Строка накладной не найдена");
                if (l.RealQuantity is < 0) throw new EgaisException("Количество не может быть отрицательным");
                if (l.RealQuantity.HasValue && l.RealQuantity > line.Quantity) throw new EgaisException($"«{line.ProductName}»: принято больше, чем в накладной");
                line.RealQuantity = l.RealQuantity ?? line.RealQuantity;
            }

            if (decision == ActDecision.Rejected) foreach (var l in doc.Lines) l.RealQuantity = 0;
            if (decision == ActDecision.Accepted) foreach (var l in doc.Lines) l.RealQuantity = l.Quantity;
            if (decision == ActDecision.Differences && doc.Lines.All(l => l.RealQuantity == l.Quantity))
                decision = ActDecision.Accepted;

            // Что принимаем — должно быть привязано к складу и пересчитываться в его единицы
            var receiptLines = new List<(StockItem Item, decimal Quantity, decimal Sum)>();
            if (decision != ActDecision.Rejected)
            {
                foreach (var line in doc.Lines.Where(l => (l.RealQuantity ?? 0) > 0))
                {
                    if (line.StockItem == null)
                        throw new EgaisException($"Укажите позицию склада для «{line.ProductName}»");
                    if (line.StockItem.Kind != StockItemKinds.Raw)
                        throw new EgaisException($"«{line.ProductName}»: выберите позицию сырья, а не полуфабрикат");
                    var qty = ConvertToStockUnits(line, line.StockItem, line.RealQuantity!.Value);
                    receiptLines.Add((line.StockItem, qty, Math.Round(line.RealQuantity!.Value * line.Price, 2)));
                }
            }

            var actNumber = $"A{doc.Id}-{DateTime.UtcNow:HHmmss}";
            if (!_options.SendActs)
            {
                // Акты отправляет другая программа (iiko) — здесь только приход на склад
                doc.ActNumber = null;
                doc.ActDecision = decision.ToString();
                doc.ActNote = string.IsNullOrWhiteSpace(note) ? null : Trunc(note.Trim(), 500);
                doc.Status = decision switch
                {
                    ActDecision.Rejected => EgaisStatuses.Rejected,
                    ActDecision.Differences => EgaisStatuses.Differences,
                    _ => EgaisStatuses.Accepted
                };
                doc.ResultComment = "Акт в ЕГАИС не отправлялся — его отправляет iiko (режим «только приход на склад»)";
                doc.ResultAt = DateTime.UtcNow;
                await CreateReceiptAsync(doc, receiptLines, userId);
                await _context.SaveChangesAsync();
                return doc;
            }

            var xml = EgaisXml.BuildWayBillAct(_options.FsrarId, decision, actNumber, DateTime.UtcNow.AddHours(3), doc.WbRegId!, note,
                decision == ActDecision.Differences
                    ? doc.Lines.Select(l => new ActPosition(l.Identity, l.InformF2RegId, l.RealQuantity ?? 0))
                    : null);

            var (ok, response) = await _utm.SendAsync("WayBillAct_v4", xml);
            if (!ok)
                throw new EgaisException($"УТМ не принял акт: {UtmClient.ExtractError(response)}");

            doc.ActNumber = actNumber;
            doc.ActDecision = decision.ToString();
            doc.ActNote = string.IsNullOrWhiteSpace(note) ? null : Trunc(note.Trim(), 500);
            doc.ActSentAt = DateTime.UtcNow;
            doc.Status = EgaisStatuses.ActSent;
            doc.ResultComment = null;

            // Приход на склад сразу — если ЕГАИС потом вернёт ошибку, приход можно отменить
            await CreateReceiptAsync(doc, receiptLines, userId);

            await _context.SaveChangesAsync();
            _logger.LogInformation("🍺 ЕГАИС: акт {Act} ({Decision}) по ТТН {RegId}", actNumber, decision, doc.WbRegId);
            return doc;
        }

        private async Task CreateReceiptAsync(EgaisDocument doc, List<(StockItem Item, decimal Quantity, decimal Sum)> receiptLines, int? userId)
        {
            if (receiptLines.Count > 0 && doc.StockDocumentId == null)
            {
                var supplierId = await EnsureSupplierAsync(doc);
                var receipt = new StockDocument
                {
                    Type = StockDocumentTypes.Receipt,
                    Number = doc.Number,
                    Date = doc.Date ?? DateTime.UtcNow,
                    SupplierId = supplierId,
                    Comment = $"ЕГАИС, ТТН {doc.WbRegId}",
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };
                var order = 0;
                foreach (var (item, qty, sum) in receiptLines)
                {
                    receipt.Lines.Add(new StockDocumentLine
                    {
                        StockItemId = item.Id,
                        Quantity = Math.Round(qty, 3),
                        Price = qty > 0 ? Math.Round(sum / qty, 4) : 0,
                        Sum = sum,
                        SortOrder = order++
                    });
                }
                _context.StockDocuments.Add(receipt);
                await _context.SaveChangesAsync();
                await _inventory.PostDocumentAsync(receipt);
                doc.StockDocumentId = receipt.Id;
            }

        }

        /// <summary>
        /// Количество из ЕГАИС → единицы склада. Фасованная продукция — штуки (объём тары в литрах),
        /// нефасованная (кеги) — декалитры.
        /// </summary>
        public static decimal ConvertToStockUnits(EgaisDocumentLine line, StockItem item, decimal quantity)
        {
            var unpacked = string.Equals(line.UnitType, "Unpacked", StringComparison.OrdinalIgnoreCase);
            return item.Unit switch
            {
                StockUnits.Liter => unpacked ? quantity * 10m
                    : line.Capacity is > 0 ? quantity * line.Capacity.Value
                    : throw new EgaisException($"«{line.ProductName}»: в накладной нет объёма тары — выберите позицию склада в штуках"),
                StockUnits.Piece => unpacked
                    ? throw new EgaisException($"«{line.ProductName}» — разливная продукция (в декалитрах), выберите позицию склада в литрах")
                    : quantity,
                _ => throw new EgaisException($"«{line.ProductName}»: для алкоголя позиция склада должна быть в литрах или штуках")
            };
        }

        private async Task<int?> EnsureSupplierAsync(EgaisDocument doc)
        {
            if (string.IsNullOrWhiteSpace(doc.ShipperName) && string.IsNullOrWhiteSpace(doc.ShipperInn)) return null;
            var supplier = !string.IsNullOrWhiteSpace(doc.ShipperInn)
                ? await _context.Suppliers.FirstOrDefaultAsync(s => s.Inn == doc.ShipperInn)
                : await _context.Suppliers.FirstOrDefaultAsync(s => s.Name == doc.ShipperName);
            if (supplier != null) return supplier.Id;

            supplier = new Supplier
            {
                Name = Trunc(doc.ShipperName ?? $"ИНН {doc.ShipperInn}", 200),
                Inn = doc.ShipperInn,
                Notes = doc.ShipperFsrarId != null ? $"ФСРАР ID {doc.ShipperFsrarId}" : null,
                CreatedAt = DateTime.UtcNow
            };
            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();
            return supplier.Id;
        }

        private static string Trunc(string value, int max) => value.Length <= max ? value : value[..max];
    }

    public class EgaisException : Exception
    {
        public EgaisException(string message) : base(message) { }
    }

    /// <summary>Фоновая проверка входящих в УТМ (настройки читаются каждый раз — их можно менять в админке)</summary>
    public class EgaisSyncWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly EgaisSettingsProvider _settings;
        private readonly ILogger<EgaisSyncWorker> _logger;

        public EgaisSyncWorker(IServiceScopeFactory scopes, EgaisSettingsProvider settings, ILogger<EgaisSyncWorker> logger)
        {
            _scopes = scopes;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await _settings.LoadAsync(); }
            catch (Exception ex) { _logger.LogWarning("ЕГАИС: не удалось прочитать настройки — {Message}", ex.Message); }

            var lastRun = DateTime.MinValue;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                var o = _settings.Current;
                if (!o.Enabled || o.AutoSyncMinutes <= 0 || DateTime.UtcNow - lastRun < TimeSpan.FromMinutes(o.AutoSyncMinutes))
                    continue;

                lastRun = DateTime.UtcNow;
                try
                {
                    using var scope = _scopes.CreateScope();
                    var result = await scope.ServiceProvider.GetRequiredService<EgaisService>().SyncAsync(stoppingToken);
                    if (result.WayBills + result.RegInfos + result.Tickets > 0)
                        _logger.LogInformation("ЕГАИС: накладных {W}, справок {R}, квитанций {T}", result.WayBills, result.RegInfos, result.Tickets);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("ЕГАИС: синхронизация не удалась — {Message}", ex.Message);
                }
            }
        }
    }
}
