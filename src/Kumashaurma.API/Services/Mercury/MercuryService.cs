using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Mercury
{
    /// <summary>Меркурий: загрузка входящих ВСД на площадку заведения и их гашение (полное принятие партии)</summary>
    public class MercuryService
    {
        private static readonly SemaphoreSlim SyncLock = new(1, 1);
        private const int MaxPages = 20;

        private readonly ApplicationDbContext _context;
        private readonly VetisClient _client;
        private readonly MercurySettingsProvider _settings;
        private readonly ILogger<MercuryService> _logger;

        public MercuryService(ApplicationDbContext context, VetisClient client, MercurySettingsProvider settings, ILogger<MercuryService> logger)
        {
            _context = context;
            _client = client;
            _settings = settings;
            _logger = logger;
        }

        public record SyncResult(int Received, int Total, int External);

        /// <summary>Входящие оформленные ВСД за последние дни (не больше 31 — ограничение Меркурия)</summary>
        public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
        {
            if (!await SyncLock.WaitAsync(0, ct)) throw new VetisException("Загрузка уже идёт — подождите");
            var o = await _settings.GetAsync();
            try
            {
                if (!o.Enabled) throw new VetisException("Меркурий выключен — включите его в «Настройке интеграций»");
                var now = DateTime.UtcNow;
                var from = now.AddDays(-Math.Clamp(o.InitialDays, 1, 31));
                var seen = new List<VetDocumentInfo>();
                for (int page = 0, offset = 0; page < MaxPages; page++)
                {
                    var resp = await _client.CallAsync(o, VetDocuments.ListRequest(o, from, now, offset), ct);
                    var (docs, total) = VetDocuments.ParseList(resp);
                    seen.AddRange(docs);
                    offset += docs.Count;
                    if (docs.Count == 0 || offset >= total) break;
                }

                var uuids = seen.Select(d => d.Uuid).Where(u => u.Length > 0).ToList();
                var existing = await _context.MercuryDocuments.Where(d => uuids.Contains(d.Uuid)).ToDictionaryAsync(d => d.Uuid, ct);
                var received = 0;
                foreach (var d in seen.Where(d => d.Uuid.Length > 0).GroupBy(d => d.Uuid).Select(g => g.First()))
                {
                    if (existing.TryGetValue(d.Uuid, out var row))
                    {
                        row.VetStatus = d.Status;
                        // Повторно гасить нельзя, но если гашение у нас упало, а ВСД всё ещё «оформлен» — даём попробовать снова
                        continue;
                    }
                    _context.MercuryDocuments.Add(new MercuryDocument
                    {
                        Uuid = d.Uuid,
                        IssueDate = d.IssueDate,
                        VetStatus = d.Status,
                        ProductName = Trunc(d.ProductName, 500),
                        Volume = d.Volume,
                        UnitName = Trunc(d.UnitName, 50),
                        ProductionDate = Trunc(d.ProductionDate, 30),
                        ExpiryDate = Trunc(d.ExpiryDate, 30),
                        ConsignorName = Trunc(d.ConsignorName, 300),
                        Waybill = Trunc(d.Waybill, 200),
                        Xml = d.Element.ToString(SaveOptions.DisableFormatting),
                        CreatedAt = now
                    });
                    received++;
                }

                // Наши «новые», которых больше нет среди оформленных за этот период, — погашены в другой программе (например, в самом Меркурии)
                var stale = await _context.MercuryDocuments
                    .Where(d => d.Status == MercuryStatuses.New && d.IssueDate >= from.Date && !uuids.Contains(d.Uuid))
                    .ToListAsync(ct);
                foreach (var d in stale)
                {
                    d.Status = MercuryStatuses.External;
                    d.VetStatus = "UTILIZED";
                }

                await _context.SaveChangesAsync(ct);
                o.LastSyncAt = now;
                o.LastError = null;
                await _settings.SaveAsync(o, null);
                if (received > 0) _logger.LogInformation("🐄 Меркурий: новых ВСД {Count}", received);
                return new SyncResult(received, uuids.Count, stale.Count);
            }
            catch (VetisException ex)
            {
                o.LastError = ex.Message;
                o.LastSyncAt = DateTime.UtcNow;
                await _settings.SaveAsync(o, null);
                throw;
            }
            finally { SyncLock.Release(); }
        }

        public record AcceptResult(int Accepted, List<string> Errors);

        /// <summary>Погасить ВСД целиком — партия принята как в документе</summary>
        public async Task<AcceptResult> AcceptAsync(IReadOnlyCollection<int> ids, int? userId, CancellationToken ct = default)
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled) throw new VetisException("Меркурий выключен — включите его в «Настройке интеграций»");
            VetisClient.Validate(o);
            var docs = await _context.MercuryDocuments.Where(d => ids.Contains(d.Id)).ToListAsync(ct);
            var errors = new List<string>();
            var accepted = 0;
            foreach (var d in docs)
            {
                if (d.Status is not (MercuryStatuses.New or MercuryStatuses.Error))
                {
                    errors.Add($"«{d.ProductName}»: уже {(d.Status == MercuryStatuses.Accepted ? "погашен" : "обработан")}");
                    continue;
                }
                d.Status = MercuryStatuses.Processing;
                d.Error = null;
                await _context.SaveChangesAsync(ct);
                try
                {
                    var request = VetDocuments.AcceptRequest(o, XElement.Parse(d.Xml), DateTime.UtcNow);
                    var resp = await _client.CallAsync(o, request, ct);
                    d.Status = MercuryStatuses.Accepted;
                    d.VetStatus = "UTILIZED";
                    d.StockEntryGuid = VetDocuments.StockEntryGuid(resp);
                    d.ProcessedAt = DateTime.UtcNow;
                    d.ProcessedByUserId = userId;
                    accepted++;
                }
                catch (VetisException ex)
                {
                    d.Status = MercuryStatuses.Error;
                    d.Error = Trunc(ex.Message, 2000);
                    errors.Add($"«{d.ProductName}»: {ex.Message}");
                }
                await _context.SaveChangesAsync(ct);
            }
            if (accepted > 0) _logger.LogInformation("🐄 Меркурий: погашено ВСД {Count}", accepted);
            return new AcceptResult(accepted, errors);
        }

        /// <summary>Проверка подключения: запрос одного ВСД за сутки</summary>
        public async Task TestAsync(MercuryOptions o, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            await _client.CallAsync(o, VetDocuments.ListRequest(o, now.AddDays(-1), now, 0, 1), ct);
        }

        private static string? Trunc(string? s, int max) => s == null ? null : s.Length <= max ? s : s[..max];
    }

    /// <summary>Фоновая загрузка входящих ВСД</summary>
    public class MercurySyncWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly MercurySettingsProvider _settings;
        private readonly ILogger<MercurySyncWorker> _logger;

        public MercurySyncWorker(IServiceScopeFactory scopes, MercurySettingsProvider settings, ILogger<MercurySyncWorker> logger)
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
                MercuryOptions o;
                try { o = await _settings.GetAsync(); }
                catch (Exception ex) { _logger.LogWarning("Меркурий: не удалось прочитать настройки — {Message}", ex.Message); continue; }
                if (!o.Enabled || o.AutoSyncMinutes <= 0 || DateTime.UtcNow - lastRun < TimeSpan.FromMinutes(o.AutoSyncMinutes)) continue;
                lastRun = DateTime.UtcNow;
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<MercuryService>().SyncAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Меркурий: загрузка ВСД не удалась — {Message}", ex.Message);
                }
            }
        }
    }
}
