using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services
{
    /// <summary>
    /// Заказ кухни из зала, у которого все счета уже закрыты: гость рассчитался, значит блюда отданы,
    /// даже если официант не нажал «Отдать». Такой готовый заказ убираем из «Готовы» на кухне — он выполнен.
    /// </summary>
    public static class ClosedCheckOrders
    {
        /// <summary>Готов и оплачен по закрытым счетам зала → все блюда отданы, заказ «Выполнен». true — изменили</summary>
        public static async Task<bool> CompleteIfPaidAsync(ApplicationDbContext db, IOrderStatusService status, Order order)
        {
            if (order.Status != OrderStatuses.Ready || order.PaymentStatus != "paid") return false;
            var fromHall = order.CheckId != null || order.OrderItems.Any(i => i.CheckId != null);
            if (!fromHall) return false;

            var ids = order.OrderItems.Select(i => i.Id).ToList();
            var states = await db.OrderItemKitchenStates.Where(s => ids.Contains(s.OrderItemId)).ToDictionaryAsync(s => s.OrderItemId);
            foreach (var i in order.OrderItems)
            {
                if (!states.TryGetValue(i.Id, out var st))
                {
                    st = new OrderItemKitchenState { OrderItemId = i.Id };
                    db.OrderItemKitchenStates.Add(st);
                }
                st.ReadyQuantity = i.Quantity;
                st.ServedQuantity = i.Quantity;
                st.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            return await status.ChangeStatusAsync(order, OrderStatuses.Completed);
        }
    }
}
