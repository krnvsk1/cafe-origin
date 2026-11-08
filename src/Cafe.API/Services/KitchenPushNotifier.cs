using Microsoft.EntityFrameworkCore;
using Cafe.API.Authorization;
using Cafe.API.Controllers;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services
{
    /// <summary>
    /// Повару — уведомления на телефон: новый заказ на кухне и отказ от блюда, который ждёт решения кухни.
    /// Получают сотрудники с правом «Кухня»; менеджеры и администраторы (право «Все счета») — нет, чтобы их не засыпать заказами.
    /// </summary>
    public class KitchenPushNotifier
    {
        private readonly ApplicationDbContext _context;
        private readonly WebPushService _push;

        public KitchenPushNotifier(ApplicationDbContext context, WebPushService push)
        {
            _context = context;
            _push = push;
        }

        public async Task<List<int>> RecipientsAsync()
        {
            var kitchenRoles = await RolesWithAsync(Permissions.Kitchen);
            var managerRoles = await RolesWithAsync(Permissions.ChecksAll);
            var managers = await _context.UserRoles.Where(ur => managerRoles.Contains(ur.RoleId) || _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Support))
                .Select(ur => ur.UserId).Distinct().ToListAsync();
            return await _context.UserRoles.Where(ur => kitchenRoles.Contains(ur.RoleId) && !managers.Contains(ur.UserId))
                .Select(ur => ur.UserId).Distinct().ToListAsync();
        }

        private Task<List<int>> RolesWithAsync(string permission) =>
            _context.Roles.Where(r => _context.RoleClaims.Any(c => c.RoleId == r.Id && c.ClaimType == Permissions.ClaimType && c.ClaimValue == permission))
                .Select(r => r.Id).ToListAsync();

        /// <summary>Новый заказ на кухне: откуда и что готовить</summary>
        public async Task NewOrderAsync(int orderId)
        {
            var order = await _context.Orders.AsNoTracking().Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null || order.Status != OrderStatuses.New || order.OrderItems.Count == 0) return;
            // Заказ ко времени придёт на кухню позже — сейчас не тревожим
            if (order.ScheduledFor != null && order.ScheduledFor > DateTime.UtcNow.AddMinutes(30)) return;
            var to = await RecipientsAsync();
            if (to.Count == 0) return;

            string where = order.DeliveryType;
            var checkId = order.CheckId ?? order.OrderItems.Select(i => i.CheckId).FirstOrDefault(id => id != null);
            if (checkId != null)
            {
                var table = await _context.TableChecks.AsNoTracking().Where(c => c.Id == checkId).Select(c => c.Table != null ? c.Table.Name : null).FirstOrDefaultAsync();
                if (!string.IsNullOrEmpty(table)) where = WaiterController.TableLabel(table);
            }
            if (order.BanquetId != null) where = "Банкет";
            var body = string.Join(", ", order.OrderItems.Select(i => i.Quantity > 1 ? $"{i.Quantity} × {i.Name}" : i.Name));
            if (body.Length > 180) body = body[..177] + "…";
            _push.Enqueue(to, new PushMessage($"Новый заказ #{order.Id} · {where}", body, "/kitchen", $"kitchen-new-{order.Id}"));
        }

        /// <summary>Официант убирает блюдо, которое уже готовят — кухня должна подтвердить</summary>
        public async Task VoidRequestAsync(OrderItemVoid v)
        {
            var to = await RecipientsAsync();
            if (to.Count == 0) return;
            _push.Enqueue(to, new PushMessage("Отказ от блюда — подтвердите",
                $"{v.Quantity} × {v.Name} (заказ #{v.OrderId}): {v.Reason}", "/kitchen", $"kitchen-void-{v.Id}"));
        }
    }

    /// <summary>Слушает новые заказы и шлёт повару уведомление</summary>
    public class KitchenPushWorker : BackgroundService
    {
        private readonly IOrderEventBus _bus;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<KitchenPushWorker> _logger;

        public KitchenPushWorker(IOrderEventBus bus, IServiceScopeFactory scopes, ILogger<KitchenPushWorker> logger)
        {
            _bus = bus;
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var sub = _bus.Subscribe();
            try
            {
                await foreach (var evt in sub.Reader.ReadAllAsync(stoppingToken))
                {
                    if (evt.Type != "created") continue;
                    try
                    {
                        using var scope = _scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<KitchenPushNotifier>().NewOrderAsync(evt.OrderId);
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Не удалось отправить повару уведомление о заказе #{OrderId}", evt.OrderId); }
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}
