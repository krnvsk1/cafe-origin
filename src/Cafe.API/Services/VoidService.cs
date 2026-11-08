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

        private readonly KitchenPushNotifier? _kitchenPush;

        public VoidService(ApplicationDbContext context, IInventoryService inventory, IOrderStatusService statusService, IOrderEventBus events,
            KitchenPushNotifier? kitchenPush = null)
        {
            _kitchenPush = kitchenPush;
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
            {
                _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
                // Повару на телефон: отказ ждёт решения кухни
                if (_kitchenPush != null)
                    try { await _kitchenPush.VoidRequestAsync(v); } catch { /* уведомление не обязательно */ }
            }
            return v;
        }

        /// <summary>
        /// Отменить всё: каждое блюдо по тем же правилам, что и отмену одного блюда
        /// (не приняли — сразу; приняли или готовят — ждём кухню; готово или отдано — со списанием).
        /// Возвращает, сколько позиций убрано сразу и сколько ждёт решения кухни.
        /// </summary>
        public async Task<(int Done, int Pending)> CancelAllAsync(IEnumerable<int> itemIds, string reason, int? userId)
        {
            int done = 0, pending = 0;
            foreach (var itemId in itemIds.Distinct().ToList())
            {
                var item = await _context.OrderItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
                if (item == null || item.Quantity <= 0) continue;
                var waiting = await _context.OrderItemVoids.Where(v => v.OrderItemId == itemId && v.Status == VoidStatuses.Pending).SumAsync(v => v.Quantity);
                var left = item.Quantity - waiting - await DoneForSiteAsync(item);
                if (left <= 0) { pending += waiting > 0 ? 1 : 0; continue; }
                var orderStatus = await _context.Orders.Where(o => o.Id == item.OrderId).Select(o => o.Status).FirstAsync();
                if (orderStatus == OrderStatuses.Cancelled) continue;
                var v = await RequestAsync(itemId, left, reason, userId);
                if (v.Status == VoidStatuses.Pending) pending++; else done++;
            }
            return (done, pending);
        }

        /// <summary>Для заказа не из счёта стола позиции не удаляются при отмене — сколько уже убрано</summary>
        private async Task<int> DoneForSiteAsync(OrderItem item)
        {
            if (item.CheckId != null || await _context.Orders.AnyAsync(o => o.Id == item.OrderId && o.CheckId != null)) return 0;
            return await _context.OrderItemVoids.Where(v => v.OrderId == item.OrderId && v.Status == VoidStatuses.Done && v.OrderItemId == item.Id).SumAsync(v => v.Quantity);
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

            // Заказ с сайта (не из счёта стола) отменяется только целиком: позиции и сумма остаются для истории
            // и возврата онлайн-оплаты; когда убраны все блюда — заказ отменён (склад уже поправлен по каждому блюду)
            if (order.CheckId == null && order.OrderItems.All(i => i.CheckId == null))
            {
                v.Status = VoidStatuses.Done;
                v.WriteOff = writeOff;
                v.Quantity = qty;
                v.ResolvedAt = DateTime.UtcNow;
                v.ResolvedByUserId = resolvedBy;
                await _context.SaveChangesAsync();
                var voided = await _context.OrderItemVoids.Where(x => x.OrderId == order.Id && x.Status == VoidStatuses.Done && x.OrderItemId != null)
                    .GroupBy(x => x.OrderItemId!.Value).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty);
                if (order.OrderItems.All(i => voided.GetValueOrDefault(i.Id) >= i.Quantity) && order.Status != OrderStatuses.Cancelled)
                {
                    order.StockDeductedAt = null;
                    await _statusService.ChangeStatusAsync(order, OrderStatuses.Cancelled);
                }
                else
                    _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
                return;
            }

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

            if (v.CheckId != null) await CancelCheckIfEmptyAsync(v.CheckId.Value);
        }

        /// <summary>Менеджер отменил весь счёт и кухня ответила по последнему блюду — счёт отменён, стол свободен</summary>
        public async Task<bool> CancelCheckIfEmptyAsync(int checkId)
        {
            var check = await _context.TableChecks.FirstOrDefaultAsync(c => c.Id == checkId);
            if (check == null || check.Status != CheckStatuses.Open || check.CancelRequestedAt == null) return false;
            var hasItems = await _context.OrderItems.AnyAsync(i => i.Quantity > 0 && (i.CheckId ?? i.Order!.CheckId) == checkId && i.Order!.Status != OrderStatuses.Cancelled);
            if (hasItems) return false;
            check.Status = CheckStatuses.Cancelled;
            check.ClosedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
