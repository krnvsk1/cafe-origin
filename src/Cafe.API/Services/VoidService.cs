using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services
{
    /// <summary>
    /// Отмена блюд из чека официантом:
    /// кухня ещё не приняла — убираем сразу; приняла или готовит — ждём решения кухни (со списанием или без);
    /// уже готово или отдано — убираем со списанием продуктов.
    /// </summary>
    public class VoidService
    {
        private readonly ApplicationDbContext _context;
        private readonly IInventoryService _inventory;
        private readonly IOrderStatusService _statusService;
        private readonly IOrderEventBus _events;

        public VoidService(ApplicationDbContext context, IInventoryService inventory, IOrderStatusService statusService, IOrderEventBus events)
        {
            _context = context;
            _inventory = inventory;
            _statusService = statusService;
            _events = events;
        }

        public class VoidException : Exception
        {
            public VoidException(string message) : base(message) { }
        }

        /// <summary>Нужно ли решение кухни: блюдо приняли или готовят</summary>
        public static bool NeedsKitchen(string orderStatus) => orderStatus is OrderStatuses.Accepted or OrderStatuses.Preparing;

        /// <summary>Готовое или отданное блюдо уже приготовлено — его продукты списываются</summary>
        public static bool AlreadyMade(string orderStatus) =>
            orderStatus is OrderStatuses.Ready or OrderStatuses.OnTheWay or OrderStatuses.Delivered or OrderStatuses.Completed;

        public static decimal UnitPrice(OrderItem i) => i.Price + i.SelectedAddons.Sum(a => a.Price * a.Quantity);

        public async Task<OrderItemVoid> RequestAsync(int itemId, int quantity, string reason, int? userId)
        {
            reason = (reason ?? "").Trim();
            if (reason.Length == 0) throw new VoidException("Укажите причину");
            if (reason.Length > 300) reason = reason[..300];
            var item = await _context.OrderItems.Include(i => i.SelectedAddons).FirstOrDefaultAsync(i => i.Id == itemId)
                       ?? throw new VoidException("Позиция не найдена");
            var order = await _context.Orders.FirstAsync(o => o.Id == item.OrderId);
            if (order.Status == OrderStatuses.Cancelled) throw new VoidException("Подача уже отменена");
            var checkId = item.CheckId ?? order.CheckId;
            if (checkId != null && await _context.TableChecks.AnyAsync(c => c.Id == checkId && c.Status != CheckStatuses.Open))
                throw new VoidException("Счёт уже закрыт");

            var pending = await _context.OrderItemVoids.Where(v => v.OrderItemId == itemId && v.Status == VoidStatuses.Pending).SumAsync(v => v.Quantity);
            if (quantity < 1 || quantity + pending > item.Quantity)
                throw new VoidException(pending > 0 ? $"Можно убрать не больше {item.Quantity - pending} шт. — остальное уже ждёт кухню" : $"В позиции {item.Quantity} шт.");

            var v = new OrderItemVoid
            {
                OrderId = order.Id,
                OrderItemId = item.Id,
                CheckId = checkId,
                DishId = item.DishId,
                Name = item.Name.Length > 150 ? item.Name[..150] : item.Name,
                Quantity = quantity,
                Amount = Math.Round(UnitPrice(item) * quantity, 2),
                Reason = reason,
                OrderStatus = order.Status,
                Status = VoidStatuses.Pending,
                RequestedByUserId = userId,
                RequestedAt = DateTime.UtcNow
            };
            _context.OrderItemVoids.Add(v);
            await _context.SaveChangesAsync();

            if (!NeedsKitchen(order.Status))
                await ApplyAsync(v, writeOff: AlreadyMade(order.Status), resolvedBy: userId);
            else
                _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
            return v;
        }

        /// <summary>Решение кухни: списать продукты или нет. Отказать в отмене кухня не может</summary>
        public async Task ResolveAsync(int voidId, string decision, int? userId)
        {
            var v = await _context.OrderItemVoids.FirstOrDefaultAsync(x => x.Id == voidId) ?? throw new VoidException("Запрос не найден");
            if (v.Status != VoidStatuses.Pending) throw new VoidException("Запрос уже обработан");
            if (decision is not ("writeoff" or "keep")) throw new VoidException("Решение: writeoff (списать) или keep (без списания)");
            await ApplyAsync(v, writeOff: decision == "writeoff", resolvedBy: userId);
        }

        private async Task ApplyAsync(OrderItemVoid v, bool writeOff, int? resolvedBy)
        {
            var order = await _context.Orders.Include(o => o.OrderItems).ThenInclude(i => i.SelectedAddons).FirstAsync(o => o.Id == v.OrderId);
            var item = order.OrderItems.FirstOrDefault(i => i.Id == v.OrderItemId);
            if (item == null) throw new VoidException("Позиции уже нет в заказе");
            var qty = Math.Min(v.Quantity, item.Quantity);

            await _inventory.VoidItemStockAsync(order, item, qty, writeOff, $"{qty} × {item.Name}, заказ #{order.Id}");

            item.Quantity -= qty;
            if (item.Quantity <= 0)
            {
                order.OrderItems.Remove(item);
                _context.OrderItems.Remove(item);
                v.OrderItemId = null;
            }
            else
            {
                var st = await _context.OrderItemKitchenStates.FirstOrDefaultAsync(x => x.OrderItemId == item.Id);
                if (st != null)
                {
                    st.ReadyQuantity = Math.Min(st.ReadyQuantity, item.Quantity);
                    st.ServedQuantity = Math.Min(st.ServedQuantity, st.ReadyQuantity);
                }
            }
            order.Total = order.OrderItems.Sum(i => UnitPrice(i) * i.Quantity);
            v.Status = VoidStatuses.Done;
            v.WriteOff = writeOff;
            v.Quantity = qty;
            v.ResolvedAt = DateTime.UtcNow;
            v.ResolvedByUserId = resolvedBy;
            await _context.SaveChangesAsync();

            // Убрали всё — подача отменена (склад уже поправлен выше, повторного возврата не будет)
            if (order.OrderItems.Count == 0 && order.Status != OrderStatuses.Cancelled)
                await _statusService.ChangeStatusAsync(order, OrderStatuses.Cancelled);
            else
                _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
        }
    }
}
