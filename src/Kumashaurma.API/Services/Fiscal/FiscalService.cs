using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Fiscal
{
    /// <summary>Пробивает чеки по заказам кассы на АТОЛ и следит, чтобы один заказ не пробился дважды</summary>
    public class FiscalService
    {
        private static readonly SemaphoreSlim DeviceLock = new(1, 1);

        private readonly ApplicationDbContext _context;
        private readonly AtolClient _atol;
        private readonly FiscalSettingsProvider _settings;
        private readonly ILogger<FiscalService> _logger;

        public FiscalService(ApplicationDbContext context, AtolClient atol, FiscalSettingsProvider settings, ILogger<FiscalService> logger)
        {
            _context = context;
            _atol = atol;
            _settings = settings;
            _logger = logger;
        }

        public async Task<bool> EnabledAsync() => (await _settings.GetAsync()).Enabled;

        /// <summary>
        /// Чек прихода или возврата по заказу. Ошибка кассы не бросается — она записывается в чек,
        /// чтобы заказ не терялся, а кассир мог нажать «Повторить».
        /// </summary>
        public async Task<FiscalReceipt> PrintAsync(int orderId, string kind, int? userId, CancellationToken ct = default)
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled) throw new FiscalException("Онлайн-касса выключена — включите её в «Настройке интеграций»");

            var order = await _context.Orders.FirstOrDefaultAsync(x => x.Id == orderId, ct)
                        ?? throw new FiscalException("Заказ не найден");
            if (order.PaymentMethod is not ("cash" or "card"))
                throw new FiscalException("Чек на кассе пробивается только при оплате наличными или картой; онлайн-оплату фискализирует ЮKassa");
            if (order.Total <= 0) throw new FiscalException("Сумма заказа — 0 ₽, чек не нужен");

            var receipts = await _context.FiscalReceipts.Where(r => r.OrderId == orderId).OrderBy(r => r.Id).ToListAsync(ct);
            var sold = receipts.Any(r => r.Kind == FiscalKinds.Sell && r.Status == FiscalStatuses.Done);
            var returned = receipts.Any(r => r.Kind == FiscalKinds.Return && r.Status == FiscalStatuses.Done);
            if (kind == FiscalKinds.Sell && sold) throw new FiscalException("Чек по этому заказу уже пробит");
            if (kind == FiscalKinds.Return && !sold) throw new FiscalException("Чек прихода по заказу не пробит — возвращать нечего");
            if (kind == FiscalKinds.Return && returned) throw new FiscalException("Чек возврата уже пробит");

            await DeviceLock.WaitAsync(ct);
            try
            {
                // Незавершённое задание: сначала узнаём его судьбу — иначе можно пробить чек дважды
                var pending = receipts.LastOrDefault(r => r.Kind == kind && r.Status == FiscalStatuses.Processing);
                if (pending != null)
                {
                    var prev = await SafeResultAsync(o, pending.RequestUuid, ct);
                    Apply(pending, prev);
                    await _context.SaveChangesAsync(ct);
                    if (pending.Status != FiscalStatuses.Error) return pending;
                }

                var lines = await ReceiptLines.LoadAsync(_context, order, ct);
                var cashier = await CashierNameAsync(userId, ct);
                var contact = o.SendToCustomer ? Controllers.PosController.NormalizePhone(order.Phone) : null;
                var task = FiscalTasks.Receipt(kind, o, lines, order.PaymentMethod, cashier, contact);

                var receipt = new FiscalReceipt
                {
                    OrderId = order.Id,
                    Kind = kind,
                    Status = FiscalStatuses.Processing,
                    RequestUuid = Guid.NewGuid().ToString(),
                    Total = lines.Sum(l => l.Amount),
                    PaymentMethod = order.PaymentMethod,
                    ClientContact = contact,
                    RequestJson = task.ToJsonString(),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = userId
                };
                _context.FiscalReceipts.Add(receipt);
                await _context.SaveChangesAsync(ct);

                AtolResult result;
                try { result = await _atol.RunAsync(o, receipt.RequestUuid, task, ct); }
                catch (FiscalException ex) { result = new AtolResult(false, false, ex.Message, null); }
                Apply(receipt, result);
                await _context.SaveChangesAsync(ct);

                if (receipt.Status == FiscalStatuses.Done)
                    _logger.LogInformation("🧾 Чек {Kind} по заказу #{OrderId}: ФД {Fd}", kind, order.Id, receipt.FiscalDocumentNumber);
                else
                    _logger.LogWarning("Чек {Kind} по заказу #{OrderId} не пробит: {Error}", kind, order.Id, receipt.Error);
                return receipt;
            }
            finally { DeviceLock.Release(); }
        }

        /// <summary>Состояние смены: opened / closed / expired (больше 24 часов — надо закрыть)</summary>
        public async Task<(string State, int? Number)> ShiftStatusAsync(FiscalOptions o, CancellationToken ct)
        {
            var r = await _atol.RunAsync(o, Guid.NewGuid().ToString(), FiscalTasks.ShiftStatus(), ct, 10);
            if (!r.Ready) throw new FiscalException(r.Error ?? "Касса не ответила");
            var s = r.Result?["shiftStatus"];
            var state = s?["state"]?.GetValue<string>() ?? "unknown";
            var number = s?["number"] is JsonValue n && n.TryGetValue<int>(out var num) ? num : (int?)null;
            return (state, number);
        }

        /// <summary>Открыть смену, X-отчёт или закрыть смену (Z-отчёт)</summary>
        public async Task ShiftAsync(string type, int? userId, CancellationToken ct)
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled) throw new FiscalException("Онлайн-касса выключена");
            await DeviceLock.WaitAsync(ct);
            try
            {
                var r = await _atol.RunAsync(o, Guid.NewGuid().ToString(), FiscalTasks.Shift(type, o, await CashierNameAsync(userId, ct)), ct);
                if (r.Pending) throw new FiscalException("Касса не закончила операцию — проверьте её и повторите");
                if (!r.Ready) throw new FiscalException(r.Error ?? "Касса отказала");
            }
            finally { DeviceLock.Release(); }
        }

        private async Task<AtolResult> SafeResultAsync(FiscalOptions o, string uuid, CancellationToken ct)
        {
            try { return await _atol.ResultAsync(o, uuid, ct); }
            catch (FiscalException ex) { return new AtolResult(false, true, ex.Message, null); }
        }

        /// <summary>Перенести результат кассы в чек. Pending оставляет статус «отправлен» — пробивать заново нельзя, пока не ясно</summary>
        public static void Apply(FiscalReceipt receipt, AtolResult r)
        {
            if (r.Ready)
            {
                var p = r.Result?["fiscalParams"];
                receipt.Status = FiscalStatuses.Done;
                receipt.Error = null;
                receipt.FiscalDocumentNumber = Long(p?["fiscalDocumentNumber"]);
                receipt.FiscalDocumentSign = Str(p?["fiscalDocumentSign"]);
                receipt.FnNumber = Str(p?["fnNumber"]);
                receipt.ShiftNumber = (int?)Long(p?["shiftNumber"]);
                receipt.ReceiptNumber = (int?)Long(p?["fiscalReceiptNumber"]);
                receipt.FiscalDateTime = Str(p?["fiscalDocumentDateTime"]);
                receipt.CompletedAt = DateTime.UtcNow;
            }
            else if (r.Pending)
            {
                receipt.Status = FiscalStatuses.Processing;
                receipt.Error = r.Error ?? "Касса ещё печатает чек — нажмите «Проверить» через минуту";
            }
            else
            {
                receipt.Status = FiscalStatuses.Error;
                receipt.Error = Trunc(r.Error ?? "Касса отказала", 2000);
                receipt.CompletedAt = DateTime.UtcNow;
            }
        }

        private async Task<string> CashierNameAsync(int? userId, CancellationToken ct)
        {
            if (userId == null) return "Кассир";
            var u = await _context.Users.AsNoTracking().Where(x => x.Id == userId)
                .Select(x => new { x.FirstName, x.LastName }).FirstOrDefaultAsync(ct);
            var name = u == null ? "" : string.Join(' ', new[] { u.LastName, u.FirstName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return name.Length > 0 ? name : "Кассир";
        }

        private static long? Long(JsonNode? n)
        {
            if (n is not JsonValue v) return null;
            if (v.TryGetValue<long>(out var l)) return l;
            return v.TryGetValue<string>(out var s) && long.TryParse(s, out l) ? l : null;
        }

        private static string? Str(JsonNode? n)
        {
            if (n is not JsonValue v) return null;
            if (v.TryGetValue<string>(out var s)) return Trunc(s, 40);
            return Trunc(v.ToJsonString(), 40);
        }

        private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
    }
}
