using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Controllers;
using Cafe.API.Data;
using Cafe.API.Models;
using Cafe.API.Services.Fiscal;

namespace Cafe.API.Services
{
    /// <summary>Суммы банкета: блюда, скидка, обслуживание, итог, внесённая предоплата и остаток</summary>
    public record BanquetTotals(decimal Dishes, decimal Discount, decimal Service, decimal Total, decimal Prepaid, decimal Due);

    /// <summary>Добавка блюда банкета (как выбрали при добавлении)</summary>
    public record BanquetAddon(int AddonId, int Quantity, string Name, decimal Price);

    /// <summary>Банкет: расчёт сумм, отправка блюд на кухню, позиции чеков</summary>
    public class BanquetService
    {
        public const string Source = "banquet";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _context;
        private readonly IOrderEventBus _events;
        private readonly ILogger<BanquetService> _logger;

        public BanquetService(ApplicationDbContext context, IOrderEventBus events, ILogger<BanquetService> logger)
        {
            _context = context;
            _events = events;
            _logger = logger;
        }

        public class BanquetException : Exception
        {
            public BanquetException(string message) : base(message) { }
        }

        public static List<BanquetAddon> AddonsOf(BanquetItem i) =>
            string.IsNullOrEmpty(i.Addons) ? new() : JsonSerializer.Deserialize<List<BanquetAddon>>(i.Addons, Json) ?? new();

        public static string AddonsJson(IEnumerable<BanquetAddon> addons) => JsonSerializer.Serialize(addons, Json);

        /// <summary>Скидка — на блюда; обслуживание — процент от блюд после скидки или сумма</summary>
        public static BanquetTotals Totals(Banquet b, IEnumerable<BanquetItem> items, IEnumerable<BanquetPayment> payments)
        {
            var dishes = items.Sum(i => i.Price * i.Quantity);
            var discount = DiscountTypes.Of(b.DiscountType, b.DiscountValue, dishes);
            var net = dishes - discount;
            var service = b.ServiceValue is > 0
                ? b.ServiceType == DiscountTypes.Percent ? Math.Round(net * Math.Min(b.ServiceValue.Value, 100) / 100, 2, MidpointRounding.AwayFromZero)
                  : b.ServiceType == DiscountTypes.Amount ? b.ServiceValue.Value : 0
                : 0;
            var total = net + service;
            var prepaid = payments.Where(p => p.Kind == BanquetPaymentKinds.Prepay && p.RefundedAt == null).Sum(p => p.Amount);
            return new BanquetTotals(dishes, discount, service, total, prepaid, Math.Max(0, total - prepaid));
        }

        /// <summary>Отправить на кухню блюда, которые ещё не отправлены: новый заказ кухни «В зале»</summary>
        public async Task<Order?> SendToKitchenAsync(int banquetId, int? userId)
        {
            var b = await _context.Banquets.FirstOrDefaultAsync(x => x.Id == banquetId) ?? throw new BanquetException("Банкет не найден");
            if (b.Status != BanquetStatuses.Open) throw new BanquetException("Банкет уже закрыт");
            var items = await _context.BanquetItems.Where(i => i.BanquetId == banquetId && i.OrderId == null).OrderBy(i => i.Id).ToListAsync();
            if (items.Count == 0) return null;

            var requests = items.Select(i => new PosController.PosItemRequest
            {
                DishId = i.DishId,
                Quantity = i.Quantity,
                Weight = i.Weight,
                Comment = i.Comment,
                Addons = AddonsOf(i).Select(a => new PosController.PosAddonRequest { AddonId = a.AddonId, Quantity = a.Quantity }).ToList(),
            }).ToList();
            var (built, total, error) = await PosController.BuildItemsAsync(_context, requests, SalesChannels.DineIn);
            if (error != null) throw new BanquetException(error);

            var table = b.TableId == null ? null : await _context.DiningTables.AsNoTracking().Where(t => t.Id == b.TableId).Select(t => t.Name).FirstOrDefaultAsync();
            var msk = b.EventAt.AddHours(3);
            var order = new Order
            {
                CustomerName = $"Банкет: {b.CustomerName}",
                Phone = string.Empty,
                Address = "В зале",
                DeliveryType = SalesChannels.DineIn,
                Notes = $"Банкет №{b.Id} на {msk:dd.MM HH:mm}" + (b.Guests is > 0 ? $", гостей {b.Guests}" : "") + (table != null ? $", {WaiterController.TableLabel(table)}" : "")
                        + (string.IsNullOrWhiteSpace(b.Comment) ? "" : $". {b.Comment}"),
                Status = OrderStatuses.New,
                // Оплата — по банкету (предоплаты и расчёт при закрытии), не по заказу кухни
                PaymentMethod = Source,
                PaymentStatus = "unpaid",
                Source = Source,
                BanquetId = b.Id,
                CashierId = userId,
                Total = total,
                CreatedAt = DateTime.UtcNow,
                AccessToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
            };
            foreach (var item in built!) order.OrderItems.Add(item);
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
            foreach (var i in items) i.OrderId = order.Id;
            await _context.SaveChangesAsync();
            _events.Publish(new OrderEvent("created", order.Id, null, order.Status, null, DateTime.UtcNow));
            _logger.LogInformation("🎉 Банкет #{BanquetId}: блюда отправлены на кухню, заказ #{OrderId}", b.Id, order.Id);
            return order;
        }

        /// <summary>Позиции чека аванса: «Предоплата за банкет» — признак «аванс», предмет «платёж»</summary>
        public static List<ReceiptLine> PrepayLines(Banquet b, decimal amount) => new()
        {
            new ReceiptLine($"Предоплата за банкет №{b.Id} на {b.EventAt.AddHours(3):dd.MM.yyyy}", amount, 1, amount, false, "advance", "payment"),
        };

        /// <summary>
        /// Позиции чека расчёта: блюда и обслуживание, суммы приведены к fiscalSum
        /// (итог минус часть «на сотрудника» — она без оплаты и в чек не идёт)
        /// </summary>
        public static List<ReceiptLine> FinalLines(IEnumerable<BanquetItem> items, BanquetTotals t, decimal fiscalSum)
        {
            var raw = items.Where(i => i.Quantity > 0).Select(i =>
            {
                var addons = AddonsOf(i);
                var name = addons.Count > 0 ? $"{i.Name} + {string.Join(", ", addons.Select(a => a.Quantity > 1 ? $"{a.Name} ×{a.Quantity}" : a.Name))}" : i.Name;
                return (name, i.Price, i.Quantity, false);
            }).ToList();
            // Скидка — по блюдам; обслуживание — отдельной услугой
            var dishesNet = t.Dishes - t.Discount;
            var scale = t.Total > 0 ? fiscalSum / t.Total : 0;
            var dishTarget = Math.Round(dishesNet * scale, 2, MidpointRounding.AwayFromZero);
            var lines = ReceiptLines.Scale(raw, dishTarget);
            var service = fiscalSum - lines.Sum(l => l.Amount);
            if (service > 0) lines.Add(new ReceiptLine("Обслуживание", service, 1, service, true));
            return lines;
        }
    }

    /// <summary>Отправляет блюда банкетов на кухню, когда наступило «отправить на кухню»</summary>
    public class BanquetKitchenWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<BanquetKitchenWorker> _logger;

        public BanquetKitchenWorker(IServiceScopeFactory scopes, ILogger<BanquetKitchenWorker> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var now = DateTime.UtcNow;
                    var due = await db.Banquets.AsNoTracking()
                        .Where(b => b.Status == BanquetStatuses.Open && b.KitchenAt != null && b.KitchenAt <= now
                                    && db.BanquetItems.Any(i => i.BanquetId == b.Id && i.OrderId == null))
                        .Select(b => b.Id).ToListAsync(stoppingToken);
                    foreach (var id in due)
                    {
                        try { await scope.ServiceProvider.GetRequiredService<BanquetService>().SendToKitchenAsync(id, null); }
                        catch (BanquetService.BanquetException ex) { _logger.LogWarning("Банкет #{Id}: не отправлен на кухню — {Error}", id, ex.Message); }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogError(ex, "Ошибка отправки банкетов на кухню"); }
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
