using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Edo
{
    public class EdoException : Exception
    {
        public EdoException(string message) : base(message) { }
    }

    /// <summary>
    /// Входящие УПД из СБИС: синхронизация, сопоставление строк с номенклатурой, создание прихода.
    /// </summary>
    public class EdoService
    {
        private static readonly SemaphoreSlim SyncLock = new(1, 1);
        private const int MaxPagesPerSync = 40;

        private readonly ApplicationDbContext _context;
        private readonly SbisClient _sbis;
        private readonly SbisSettingsProvider _settings;
        private readonly IInventoryService _inventory;
        private readonly ILogger<EdoService> _logger;

        public EdoService(ApplicationDbContext context, SbisClient sbis, SbisSettingsProvider settings,
            IInventoryService inventory, ILogger<EdoService> logger)
        {
            _context = context;
            _sbis = sbis;
            _settings = settings;
            _inventory = inventory;
            _logger = logger;
        }

        public record SyncResult(int Received, int Errors, int Pages);

        /// <summary>Забрать новые входящие УПД из СБИС</summary>
        public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
        {
            if (!await SyncLock.WaitAsync(0, ct)) throw new EdoException("Синхронизация уже идёт — подождите");
            try
            {
                var o = await _settings.GetAsync();
                if (!o.Enabled) throw new EdoException("Загрузка из СБИС выключена — включите её в настройках");

                var fromDate = o.LastEventDate ?? SbisClient.FormatMoscow(DateTime.UtcNow.AddDays(-Math.Clamp(o.InitialDays, 1, 90)));
                var eventId = o.LastEventId;
                int received = 0, errors = 0, pages = 0;
                try
                {
                    for (; pages < MaxPagesPerSync; pages++)
                    {
                        var page = await _sbis.ChangesAsync(o, fromDate, eventId, ct);
                        foreach (var doc in page.Documents)
                        {
                            var (r, e) = await ProcessAsync(o, doc, ct);
                            received += r;
                            errors += e;
                        }

                        // Курсор — последнее событие страницы
                        var last = page.Documents.SelectMany(d => d.Events).LastOrDefault(ev => ev.Id != null && ev.DateTime != null);
                        if (last != null)
                        {
                            fromDate = last.DateTime!;
                            eventId = last.Id;
                        }
                        if (!page.HasMore || last == null) break;
                    }
                    o.LastError = null;
                }
                catch (Exception ex) when (ex is SbisException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    o.LastError = ex is SbisException ? ex.Message : $"Нет связи с СБИС: {ex.Message}";
                    throw new EdoException(o.LastError);
                }
                finally
                {
                    o.LastEventDate = fromDate;
                    o.LastEventId = eventId;
                    o.LastSyncAt = DateTime.UtcNow;
                    await _settings.SaveAsync(o, null);
                }
                if (received > 0) _logger.LogInformation("📄 СБИС: новых накладных {Count}", received);
                return new SyncResult(received, errors, pages + 1);
            }
            finally { SyncLock.Release(); }
        }

        private async Task<(int Received, int Errors)> ProcessAsync(SbisOptions o, SbisClient.SbisDocument doc, CancellationToken ct)
        {
            if (doc.Deleted || !string.Equals(doc.Direction, "Входящий", StringComparison.OrdinalIgnoreCase)) return (0, 0);
            if (await _context.EdoDocuments.AnyAsync(d => d.SbisId == doc.Id, ct)) return (0, 0);

            // УПД — файл ON_NSCHFDOPPR…xml; берём первое такое вложение
            var pair = doc.Events
                .SelectMany(ev => ev.Attachments.Select(a => (Event: ev, Attachment: a)))
                .FirstOrDefault(x => !x.Attachment.Service && UpdParser.IsUpdFileName(x.Attachment.FileName) && !string.IsNullOrEmpty(x.Attachment.FileUrl));
            if (pair.Attachment == null) return (0, 0);

            var row = new EdoDocument
            {
                SbisId = doc.Id,
                Number = Trunc(doc.Number, 100),
                Date = ParseDate(doc.Date),
                SupplierInn = Trunc(doc.CounterpartyInn, 12),
                SupplierName = Trunc(doc.CounterpartyName, 300),
                FileName = Trunc(pair.Attachment.FileName, 200),
                ReceivedAt = SbisClient.ParseDateTime(pair.Event.DateTime),
                CreatedAt = DateTime.UtcNow
            };
            try
            {
                var bytes = await _sbis.DownloadAsync(o, pair.Attachment.FileUrl!, ct);
                row.Xml = UpdParser.DecodeFile(bytes);
                Fill(row, UpdParser.Parse(row.Xml));
            }
            catch (Exception ex) when (ex is SbisException or UpdParseException or System.Xml.XmlException)
            {
                row.Status = EdoStatuses.Error;
                row.Error = Trunc(ex.Message, 2000);
            }
            _context.EdoDocuments.Add(row);
            await _context.SaveChangesAsync(ct);
            return row.Status == EdoStatuses.Error ? (0, 1) : (1, 0);
        }

        private static void Fill(EdoDocument row, UpdDocument upd)
        {
            row.Number = Trunc(upd.Number ?? row.Number, 100);
            row.Date = upd.Date ?? row.Date;
            row.SupplierInn = Trunc(upd.SellerInn ?? row.SupplierInn, 12);
            row.SupplierKpp = Trunc(upd.SellerKpp, 9);
            row.SupplierName = Trunc(upd.SellerName ?? row.SupplierName, 300);
            row.Total = upd.Total;
            row.VatTotal = upd.VatTotal;
            row.WithoutVat = upd.WithoutVat;
            row.LinesCount = upd.Lines.Count;
            row.CodesCount = upd.Lines.Sum(l => l.Codes.Count);
        }

        /// <summary>Повторно скачать/разобрать документ с ошибкой — пока не реализовано повторное скачивание, только разбор</summary>
        public UpdDocument Parse(EdoDocument doc) =>
            string.IsNullOrEmpty(doc.Xml) ? throw new EdoException(doc.Error ?? "Файл накладной не загружен") : UpdParser.Parse(doc.Xml);

        public record LineView(UpdLine Line, int? StockItemId, string? StockItemName, string? StockUnit, decimal Factor, bool Remembered);

        /// <summary>Строки накладной с подставленными сопоставлениями</summary>
        public async Task<(UpdDocument Upd, List<LineView> Lines)> ViewAsync(EdoDocument doc)
        {
            var upd = Parse(doc);
            var inn = doc.SupplierInn ?? "";
            var keys = upd.Lines.Select(l => l.Key).ToList();
            var maps = await _context.EdoItemMappings.AsNoTracking().Include(m => m.StockItem)
                .Where(m => m.SupplierInn == inn && keys.Contains(m.ItemKey))
                .ToListAsync();
            var lines = upd.Lines.Select(l =>
            {
                var m = maps.FirstOrDefault(x => x.ItemKey == l.Key && x.StockItem is { IsArchived: false });
                return m == null
                    ? new LineView(l, null, null, null, 1, false)
                    : new LineView(l, m.StockItemId, m.StockItem!.Name, m.StockItem.Unit, m.Factor, true);
            }).ToList();
            return (upd, lines);
        }

        public record ImportLine(int No, int? StockItemId, decimal Factor, bool Skip);

        /// <summary>Создать приход по накладной; сопоставления запоминаются для следующих накладных поставщика</summary>
        public async Task<StockDocument> ImportAsync(int id, IReadOnlyCollection<ImportLine> lines, bool post, int? userId)
        {
            var doc = await _context.EdoDocuments.FirstOrDefaultAsync(d => d.Id == id) ?? throw new EdoException("Накладная не найдена");
            if (doc.StockDocumentId != null) throw new EdoException($"По накладной уже создан приход #{doc.StockDocumentId}");
            var upd = Parse(doc);

            var byNo = lines.ToDictionary(l => l.No);
            var itemIds = lines.Where(l => !l.Skip && l.StockItemId != null).Select(l => l.StockItemId!.Value).Distinct().ToList();
            var items = await _context.StockItems.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

            var receipt = new StockDocument
            {
                Type = StockDocumentTypes.Receipt,
                Number = Trunc(upd.Number ?? doc.Number, 50),
                Date = upd.Date ?? doc.Date ?? DateTime.UtcNow,
                SupplierId = await EnsureSupplierAsync(doc),
                Comment = $"СБИС, УПД № {upd.Number} от {upd.Date:dd.MM.yyyy}",
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            var order = 0;
            var inn = doc.SupplierInn ?? "";
            foreach (var l in upd.Lines)
            {
                if (!byNo.TryGetValue(l.No, out var choice)) throw new EdoException($"Строка {l.No} «{l.Name}»: не выбрана позиция склада");
                if (choice.Skip) continue;
                if (choice.StockItemId == null || !items.TryGetValue(choice.StockItemId.Value, out var item))
                    throw new EdoException($"Строка {l.No} «{l.Name}»: выберите позицию склада или отметьте «не приходовать»");
                if (choice.Factor <= 0) throw new EdoException($"Строка {l.No} «{l.Name}»: коэффициент должен быть больше нуля");

                var qty = Math.Round(l.Quantity * choice.Factor, 3);
                receipt.Lines.Add(new StockDocumentLine
                {
                    StockItemId = item.Id,
                    Quantity = qty,
                    Price = qty > 0 ? Math.Round(l.Sum / qty, 4) : 0,
                    Sum = l.Sum,
                    SortOrder = order++
                });

                // Запоминаем выбор
                if (inn.Length > 0)
                {
                    var key = Trunc(l.Key, 300)!;
                    var map = await _context.EdoItemMappings.FirstOrDefaultAsync(m => m.SupplierInn == inn && m.ItemKey == key);
                    if (map == null)
                    {
                        map = new EdoItemMapping { SupplierInn = inn, ItemKey = key };
                        _context.EdoItemMappings.Add(map);
                    }
                    map.ItemName = Trunc(l.Name, 500);
                    map.StockItemId = item.Id;
                    map.Factor = choice.Factor;
                    map.UpdatedAt = DateTime.UtcNow;
                }
            }
            if (receipt.Lines.Count == 0) throw new EdoException("Все строки отмечены «не приходовать» — приходовать нечего");

            receipt.Total = receipt.Lines.Sum(x => x.Sum);
            _context.StockDocuments.Add(receipt);
            await _context.SaveChangesAsync();
            if (post) await _inventory.PostDocumentAsync(receipt);

            doc.StockDocumentId = receipt.Id;
            doc.Status = EdoStatuses.Imported;
            doc.ImportedAt = DateTime.UtcNow;
            doc.ImportedByUserId = userId;
            await _context.SaveChangesAsync();
            return receipt;
        }

        private async Task<int?> EnsureSupplierAsync(EdoDocument doc)
        {
            if (string.IsNullOrWhiteSpace(doc.SupplierName) && string.IsNullOrWhiteSpace(doc.SupplierInn)) return null;
            var supplier = !string.IsNullOrWhiteSpace(doc.SupplierInn)
                ? await _context.Suppliers.FirstOrDefaultAsync(s => s.Inn == doc.SupplierInn)
                : await _context.Suppliers.FirstOrDefaultAsync(s => s.Name == doc.SupplierName);
            if (supplier != null) return supplier.Id;

            supplier = new Supplier
            {
                Name = Trunc(doc.SupplierName ?? $"ИНН {doc.SupplierInn}", 200)!,
                Inn = doc.SupplierInn,
                Notes = "Добавлен из СБИС",
                CreatedAt = DateTime.UtcNow
            };
            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();
            return supplier.Id;
        }

        private static DateTime? ParseDate(string? s) =>
            DateTime.TryParseExact(s, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;

        private static string? Trunc(string? value, int max) => value == null ? null : value.Length <= max ? value : value[..max];
    }

    /// <summary>Фоновая загрузка накладных из СБИС</summary>
    public class SbisSyncWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly SbisSettingsProvider _settings;
        private readonly ILogger<SbisSyncWorker> _logger;

        public SbisSyncWorker(IServiceScopeFactory scopes, SbisSettingsProvider settings, ILogger<SbisSyncWorker> logger)
        {
            _scopes = scopes;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var lastRun = DateTime.MinValue;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                SbisOptions o;
                try { o = await _settings.GetAsync(); }
                catch (Exception ex) { _logger.LogWarning("СБИС: не удалось прочитать настройки — {Message}", ex.Message); continue; }
                if (!o.Enabled || o.AutoSyncMinutes <= 0 || DateTime.UtcNow - lastRun < TimeSpan.FromMinutes(o.AutoSyncMinutes))
                    continue;

                lastRun = DateTime.UtcNow;
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<EdoService>().SyncAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("СБИС: загрузка накладных не удалась — {Message}", ex.Message);
                }
            }
        }
    }
}
