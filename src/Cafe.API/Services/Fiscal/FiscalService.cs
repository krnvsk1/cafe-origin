using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services.Fiscal
{
    /// <summary>Пробивает чеки по заказам кассы на АТОЛ и следит, чтобы один заказ не пробился дважды</summary>
    public class FiscalService
    {
        private static readonly SemaphoreSlim DeviceLock = new(1, 1);

        private readonly ApplicationDbContext _context;
        private readonly AtolClient _atol;
        private readonly FiscalSettingsProvider _settings;
        private readonly ILogger<FiscalService> _logger;
        private readonly BrandSettingsProvider _brand;

        public FiscalService(ApplicationDbContext context, AtolClient atol, FiscalSettingsProvider settings, ILogger<FiscalService> logger,
            BrandSettingsProvider brand)
        {
            _brand = brand;
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
            var lines = await ReceiptLines.LoadAsync(_context, order, ct);
            var contact = o.SendToCustomer ? Controllers.PosController.NormalizePhone(order.Phone) : null;
            return await RunReceiptAsync(o, order.Id, null, kind, receipts, lines, order.PaymentMethod, contact, order.DeliveryType, userId, ct);
        }

        /// <summary>
        /// Чек прихода или возврата по счёту стола (официант закрыл счёт): все блюда счёта из всех подач,
        /// включая перенесённые из других счетов. Ошибка кассы записывается в чек, как и у кассы.
        /// </summary>
        public async Task<FiscalReceipt> PrintCheckAsync(int checkId, string kind, int? userId, CancellationToken ct = default)
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled) throw new FiscalException("Онлайн-касса выключена — включите её в «Настройке интеграций»");
            var check = await _context.TableChecks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == checkId, ct)
                        ?? throw new FiscalException("Счёт не найден");
            if (check.Status != CheckStatuses.Closed) throw new FiscalException("Чек пробивается после оплаты счёта");
            if (check.PaymentMethod is not ("cash" or "card")) throw new FiscalException("Не указан способ оплаты счёта");
            if (check.Total <= 0) throw new FiscalException("Сумма счёта — 0 ₽, чек не нужен");

            var orders = await _context.Orders.AsNoTracking().Include(x => x.OrderItems).ThenInclude(i => i.SelectedAddons)
                .Where(x => x.Status != OrderStatuses.Cancelled &&
                            ((x.CheckId == checkId) || x.OrderItems.Any(i => i.CheckId == checkId)))
                .OrderBy(x => x.CreatedAt).ToListAsync(ct);
            var items = orders.SelectMany(x => x.OrderItems.Where(i => (i.CheckId ?? x.CheckId) == checkId)).OrderBy(i => i.Id).ToList();
            if (items.Count == 0 || orders.Count == 0) throw new FiscalException("В счёте нет блюд");

            var receipts = await _context.FiscalReceipts.Where(r => r.CheckId == checkId).OrderBy(r => r.Id).ToListAsync(ct);
            var lines = ReceiptLines.Build(items, 0, check.Total);
            return await RunReceiptAsync(o, orders[0].Id, checkId, kind, receipts, lines, check.PaymentMethod, null, SalesChannels.DineIn, userId, ct);
        }

        private async Task<FiscalReceipt> RunReceiptAsync(FiscalOptions o, int orderId, int? checkId, string kind, List<FiscalReceipt> receipts,
            List<ReceiptLine> lines, string paymentMethod, string? contact, string deliveryType, int? userId, CancellationToken ct)
        {
            var sold = receipts.Any(r => r.Kind == FiscalKinds.Sell && r.Status == FiscalStatuses.Done);
            var returned = receipts.Any(r => r.Kind == FiscalKinds.Return && r.Status == FiscalStatuses.Done);
            if (kind == FiscalKinds.Sell && sold) throw new FiscalException(checkId != null ? "Чек по этому счёту уже пробит" : "Чек по этому заказу уже пробит");
            if (kind == FiscalKinds.Return && !sold) throw new FiscalException("Чек прихода не пробит — возвращать нечего");
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

                var cashier = await CashierNameAsync(userId, ct);
                var task = FiscalTasks.Receipt(kind, o, lines, paymentMethod, cashier, contact, Vars(orderId, deliveryType, cashier, DateTime.UtcNow, (await _brand.GetAsync()).DisplayName));

                var receipt = new FiscalReceipt
                {
                    OrderId = orderId,
                    CheckId = checkId,
                    Kind = kind,
                    Status = FiscalStatuses.Processing,
                    RequestUuid = Guid.NewGuid().ToString(),
                    Total = lines.Sum(l => l.Amount),
                    PaymentMethod = paymentMethod,
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
                    _logger.LogInformation("🧾 Чек {Kind} по заказу #{OrderId} (счёт {CheckId}): ФД {Fd}", kind, orderId, checkId, receipt.FiscalDocumentNumber);
                else
                    _logger.LogWarning("Чек {Kind} по заказу #{OrderId} (счёт {CheckId}) не пробит: {Error}", kind, orderId, checkId, receipt.Error);
                return receipt;
            }
            finally { DeviceLock.Release(); }
        }

        /// <summary>Подстановки для своего текста в чеке</summary>
        public static Dictionary<string, string> Vars(int orderId, string deliveryType, string cashier, DateTime utc, string brand = "")
        {
            var msk = utc.AddHours(3);
            return new()
            {
                ["order"] = orderId.ToString(),
                ["type"] = deliveryType switch { "В зале" => "в зале", "Самовывоз" => "с собой", _ => "доставка" },
                ["cashier"] = cashier,
                ["brand"] = brand,
                ["date"] = msk.ToString("dd.MM.yyyy"),
                ["time"] = msk.ToString("HH:mm"),
            };
        }

        /// <summary>Напечатать образец оформления нефискальным документом</summary>
        public async Task PrintSampleAsync(ReceiptLayout layout, int? userId, CancellationToken ct)
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled) throw new FiscalException("Онлайн-касса выключена");
            var cashier = await CashierNameAsync(userId, ct);
            var lines = new List<ReceiptLine>
            {
                new("Бургер классический + Сыр", 350, 2, 700, false),
                new("Морс клюквенный", 120, 1, 120, false),
            };
            await DeviceLock.WaitAsync(ct);
            try
            {
                var r = await _atol.RunAsync(o, Guid.NewGuid().ToString(), FiscalTasks.Sample(o, layout, lines, Vars(128, "В зале", cashier, DateTime.UtcNow, (await _brand.GetAsync()).DisplayName)), ct);
                if (r.Pending) throw new FiscalException("Касса не закончила печать — проверьте её");
                if (!r.Ready) throw new FiscalException(r.Error ?? "Касса отказала");
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
