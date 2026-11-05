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
        /// <param name="onlineViaAtol">Онлайн-оплата, а ЮKassa чеки не отправляет — пробиваем на кассе как безналичную</param>
        public async Task<FiscalReceipt> PrintAsync(int orderId, string kind, int? userId, CancellationToken ct = default, bool onlineViaAtol = false)
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled) throw new FiscalException("Онлайн-касса выключена — включите её в «Настройке интеграций»");

            var order = await _context.Orders.FirstOrDefaultAsync(x => x.Id == orderId, ct)
                        ?? throw new FiscalException("Заказ не найден");
            if (order.PaymentMethod == CheckPayments.Staff) throw new FiscalException("Заказ закрыт на сотрудника без оплаты — кассовый чек не нужен");
            var payment = order.PaymentMethod == "online" && onlineViaAtol && order.PaymentStatus is "paid" or "refunded" ? "card" : order.PaymentMethod;
            if (payment is not ("cash" or "card"))
                throw new FiscalException("Чек на кассе пробивается только при оплате наличными или картой; онлайн-оплату фискализирует ЮKassa");
            if (order.Total <= 0) throw new FiscalException("Сумма заказа — 0 ₽, чек не нужен");

            // Заказ из счёта стола: чек (и возврат) — по всему счёту, иначе суммы не сойдутся с чеком продажи
            if (order.CheckId != null || await _context.OrderItems.AnyAsync(i => i.OrderId == orderId && i.CheckId != null, ct)
                || await _context.FiscalReceipts.AnyAsync(r => r.OrderId == orderId && r.CheckId != null, ct))
                throw new FiscalException("Заказ из счёта стола — чек и возврат пробиваются по счёту в «Сменах и счетах»");

            var receipts = await _context.FiscalReceipts.Where(r => r.OrderId == orderId && r.CheckId == null).OrderBy(r => r.Id).ToListAsync(ct);
            var lines = await ReceiptLines.LoadAsync(_context, order, ct);
            var contact = o.SendToCustomer ? Controllers.PosController.NormalizePhone(order.Phone) : null;
            return await RunReceiptAsync(o, order.Id, null, kind, receipts, lines, payment, contact, order.DeliveryType, userId, ct);
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
            if (check.PaymentMethod == CheckPayments.Staff) throw new FiscalException("Счёт закрыт на сотрудника без оплаты — кассовый чек не нужен");
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

        /// <summary>
        /// Чек банкета: предоплата (аванс) или расчёт при закрытии (с зачётом аванса). Каждый платёж — свой чек,
        /// продажа и возврат по платежу — как у счёта (paymentId объединяет попытки одного платежа).
        /// </summary>
        public async Task<FiscalReceipt> PrintBanquetAsync(int banquetId, int paymentId, string kind, List<ReceiptLine> lines, string paymentMethod,
            IReadOnlyList<(string Type, decimal Sum)>? payments, string? contact, int? userId, CancellationToken ct = default)
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled) throw new FiscalException("Онлайн-касса выключена — включите её в «Настройке интеграций»");
            var receipts = await _context.FiscalReceipts.Where(r => r.BanquetPaymentId == paymentId).OrderBy(r => r.Id).ToListAsync(ct);
            if (kind == FiscalKinds.Sell && lines.Sum(l => l.Amount) <= 0) throw new FiscalException("Сумма чека — 0 ₽");
            var phone = o.SendToCustomer ? Controllers.PosController.NormalizePhone(contact) : null;
            return await RunReceiptAsync(o, null, null, kind, receipts, lines, paymentMethod, phone, SalesChannels.DineIn, userId, ct, banquetId, paymentId, payments);
        }

        private async Task<FiscalReceipt> RunReceiptAsync(FiscalOptions o, int? orderId, int? checkId, string kind, List<FiscalReceipt> receipts,
            List<ReceiptLine> lines, string paymentMethod, string? contact, string deliveryType, int? userId, CancellationToken ct,
            int? banquetId = null, int? banquetPaymentId = null, IReadOnlyList<(string Type, decimal Sum)>? payments = null)
        {
            // Продажа и возврат могут чередоваться (сменили способ оплаты: возврат → новый чек) — смотрим, что сейчас «в силе»
            var sold = IsSold(receipts);
            var everSold = receipts.Any(r => r.Kind == FiscalKinds.Sell && r.Status == FiscalStatuses.Done);
            if (kind == FiscalKinds.Sell && sold) throw new FiscalException(checkId != null ? "Чек по этому счёту уже пробит" : "Чек по этому заказу уже пробит");
            if (kind == FiscalKinds.Return && !sold) throw new FiscalException(everSold ? "Чек возврата уже пробит" : "Чек прихода не пробит — возвращать нечего");

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
                var task = FiscalTasks.Receipt(kind, o, lines, paymentMethod, cashier, contact,
                    Vars(orderId ?? banquetId ?? 0, deliveryType, cashier, DateTime.UtcNow, (await _brand.GetAsync()).DisplayName), payments);
                // Возврат — зеркало чека продажи: те же позиции, суммы и способ оплаты, даже если счёт с тех пор меняли
                var soldReceipt = receipts.LastOrDefault(r => r.Kind == FiscalKinds.Sell && r.Status == FiscalStatuses.Done);
                if (kind == FiscalKinds.Return && soldReceipt != null && FiscalTasks.ReturnOf(soldReceipt.RequestJson, o, cashier) is { } mirror)
                {
                    task = mirror;
                    paymentMethod = soldReceipt.PaymentMethod ?? paymentMethod;
                    contact = soldReceipt.ClientContact;
                }

                var receipt = new FiscalReceipt
                {
                    OrderId = orderId,
                    CheckId = checkId,
                    BanquetId = banquetId,
                    BanquetPaymentId = banquetPaymentId,
                    Kind = kind,
                    Status = FiscalStatuses.Processing,
                    RequestUuid = Guid.NewGuid().ToString(),
                    Total = kind == FiscalKinds.Return && soldReceipt != null ? soldReceipt.Total : lines.Sum(l => l.Amount),
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
                // Касса отказала — чек точно не пробит
                catch (FiscalRejectedException ex) { result = new AtolResult(false, false, ex.Message, null); }
                // Связь оборвалась (касса далеко, через программу на кассовом ПК): задание могло дойти и чек мог пробиться.
                // Оставляем «отправлен» — при повторе сначала спросим кассу об этом задании, второй чек не пробьём
                catch (FiscalException ex) { result = new AtolResult(false, true, $"Нет ответа от кассы: {ex.Message}. Пробит ли чек — неизвестно: нажмите «Проверить», когда касса будет на связи", null); }
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

        /// <summary>Узнать у кассы судьбу отправленного чека (фоновая проверка). Ничего не пробивает</summary>
        public async Task<bool> ReconcileAsync(FiscalReceipt receipt, CancellationToken ct)
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled || receipt.Status != FiscalStatuses.Processing) return false;
            await DeviceLock.WaitAsync(ct);
            try
            {
                var r = await _atol.ResultAsync(o, receipt.RequestUuid, ct);
                if (r.Pending) return false;
                Apply(receipt, r);
                await _context.SaveChangesAsync(ct);
                return true;
            }
            catch (FiscalException) { return false; }
            finally { DeviceLock.Release(); }
        }

        /// <summary>Непробитые чеки: последняя попытка по заказу/счёту не удалась или ждёт ответа кассы</summary>
        public async Task<List<FiscalReceipt>> UnfinishedAsync(DateTime sinceUtc, CancellationToken ct = default)
        {
            var list = await _context.FiscalReceipts.AsNoTracking().Where(r => r.CreatedAt >= sinceUtc).OrderBy(r => r.Id).ToListAsync(ct);
            return Unfinished(list);
        }

        /// <summary>Чек продажи сейчас в силе: пробитых продаж больше, чем пробитых возвратов</summary>
        public static bool IsSold(IEnumerable<FiscalReceipt> receipts)
        {
            var done = receipts.Where(r => r.Status == FiscalStatuses.Done).ToList();
            return done.Count(r => r.Kind == FiscalKinds.Sell) > done.Count(r => r.Kind == FiscalKinds.Return);
        }

        /// <summary>Незавершённые: последний чек этого вида по счёту/заказу не пробит (продажа и возврат могут чередоваться)</summary>
        public static List<FiscalReceipt> Unfinished(IEnumerable<FiscalReceipt> receipts) => receipts
            .GroupBy(r => (r.BanquetPaymentId != null ? "p" + r.BanquetPaymentId : r.CheckId != null ? "c" + r.CheckId : "o" + r.OrderId, r.Kind))
            .Where(g => g.OrderBy(r => r.Id).Last().Status != FiscalStatuses.Done)
            .Select(g => g.OrderBy(r => r.Id).Last())
            .OrderBy(r => r.CreatedAt)
            .ToList();

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
