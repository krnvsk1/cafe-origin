using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;
using Kumashaurma.API.Options;
using Microsoft.Extensions.Options;

namespace Kumashaurma.API.Services
{
    public static class OrderStatuses
    {
        public const string New = "Новый";
        /// <summary>Повар принял заказ, но ещё не начал готовить — кухня подаёт звуковой сигнал</summary>
        public const string Accepted = "Принят";
        public const string Preparing = "Готовится";
        public const string Ready = "Готов";
        public const string OnTheWay = "В пути";
        public const string Delivered = "Доставлен";
        public const string Completed = "Выполнен";
        public const string Cancelled = "Отменён";

        public static readonly string[] All = { New, Accepted, Preparing, Ready, OnTheWay, Delivered, Completed, Cancelled };

        /// <summary>Готовить ещё не начали: продукты со склада не списаны</summary>
        public static bool IsNotStarted(string status) =>
            status == New || status == Accepted;

        public static bool IsFinal(string status) =>
            status == Delivered || status == Completed || status == Cancelled;

        public static bool IsCompleted(string status) =>
            status == Delivered || status == Completed;
    }

    public interface IOrderStatusService
    {
        /// <summary>
        /// Меняет статус заказа: CompletedAt, начисление баллов, сохранение и событие.
        /// Возвращает false, если статус неизвестен.
        /// </summary>
        Task<bool> ChangeStatusAsync(Order order, string newStatus);
    }

    public class OrderStatusService : IOrderStatusService
    {
        private readonly ApplicationDbContext _context;
        private readonly IOrderEventBus _events;
        private readonly YooKassaClient _yooKassa;
        private readonly ReferralOptions _referral;
        private readonly ILogger<OrderStatusService> _logger;
        private readonly IInventoryService _inventory;

        public OrderStatusService(
            ApplicationDbContext context,
            IOrderEventBus events,
            YooKassaClient yooKassa,
            IOptions<ReferralOptions> referral,
            ILogger<OrderStatusService> logger,
            IInventoryService inventory)
        {
            _inventory = inventory;
            _referral = referral.Value;
            _context = context;
            _events = events;
            _yooKassa = yooKassa;
            _logger = logger;
        }

        public async Task<bool> ChangeStatusAsync(Order order, string newStatus)
        {
            if (!OrderStatuses.All.Contains(newStatus))
                return false;

            var oldStatus = order.Status;
            if (oldStatus == newStatus)
                return true;

            order.Status = newStatus;

            if (newStatus == OrderStatuses.OnTheWay && order.PickedUpAt == null)
                order.PickedUpAt = DateTime.UtcNow;

            if (OrderStatuses.IsCompleted(newStatus) && order.CompletedAt == null)
            {
                order.CompletedAt = DateTime.UtcNow;
                await AwardPointsAsync(order);
                await AwardReferralAsync(order);
            }
            else if (OrderStatuses.IsCompleted(oldStatus) && !OrderStatuses.IsCompleted(newStatus))
            {
                order.CompletedAt = null;
            }

            if (newStatus == OrderStatuses.Cancelled)
            {
                await RefundPointsAsync(order);
                await RefundPaymentAsync(order);
            }

            // Склад: продукты списываются по техкартам, когда заказ взят в работу,
            // и возвращаются при отмене. Ошибка склада не должна мешать работе с заказом.
            try
            {
                await _inventory.OnOrderStatusChangedAsync(order, oldStatus, newStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось провести складское движение по заказу #{OrderId}", order.Id);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("🔄 Заказ #{OrderId}: {Old} → {New}", order.Id, oldStatus, newStatus);
            _events.Publish(new OrderEvent("status", order.Id, order.UserId, newStatus, oldStatus, DateTime.UtcNow));
            return true;
        }

        // Первый полученный заказ приглашённого друга: бонусы обоим (один раз)
        private async Task AwardReferralAsync(Order order)
        {
            if (!_referral.Enabled || !order.UserId.HasValue || order.Total < _referral.MinOrderAmount) return;

            var invitee = await _context.Users.FindAsync(order.UserId.Value);
            if (invitee?.ReferredByUserId == null || invitee.ReferralRewardedAt != null) return;
            if (invitee.ReferredByUserId == invitee.Id) return;

            var inviter = await _context.Users.FindAsync(invitee.ReferredByUserId.Value);
            invitee.ReferralRewardedAt = DateTime.UtcNow;

            if (_referral.InviteeBonus > 0)
            {
                invitee.PointsBalance += _referral.InviteeBonus;
                _context.UserPointsTransactions.Add(new UserPointsTransaction
                {
                    UserId = invitee.Id,
                    Type = "referral",
                    Amount = _referral.InviteeBonus,
                    Description = "Бонус за первый заказ по приглашению",
                    OrderId = order.Id,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (inviter != null && _referral.InviterBonus > 0)
            {
                inviter.PointsBalance += _referral.InviterBonus;
                _context.UserPointsTransactions.Add(new UserPointsTransaction
                {
                    UserId = inviter.Id,
                    Type = "referral",
                    Amount = _referral.InviterBonus,
                    Description = $"Бонус за приглашённого друга ({invitee.FirstName})",
                    CreatedAt = DateTime.UtcNow
                });
            }

            _logger.LogInformation("🤝 Реферальный бонус: пригласивший {InviterId}, друг {InviteeId}, заказ #{OrderId}",
                invitee.ReferredByUserId, invitee.Id, order.Id);
        }

        // Отмена заказа: возвращаем баллы, списанные на оплату этого заказа
        private async Task RefundPointsAsync(Order order)
        {
            if (!order.UserId.HasValue) return;

            var spent = await _context.UserPointsTransactions
                .Where(t => t.OrderId == order.Id && t.Type == "spent")
                .SumAsync(t => (int?)t.Amount) ?? 0;
            if (spent >= 0) return;

            var alreadyRefunded = await _context.UserPointsTransactions
                .AnyAsync(t => t.OrderId == order.Id && t.Type == "refund");
            if (alreadyRefunded) return;

            var user = await _context.Users.FindAsync(order.UserId.Value);
            if (user == null) return;

            user.PointsBalance += -spent;
            _context.UserPointsTransactions.Add(new UserPointsTransaction
            {
                UserId = user.Id,
                Type = "refund",
                Amount = -spent,
                Description = $"Возврат баллов за отменённый заказ #{order.Id}",
                OrderId = order.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Отмена оплаченного онлайн-заказа: возврат денег через ЮKassa
        private async Task RefundPaymentAsync(Order order)
        {
            if (order.PaymentMethod != "online" || order.PaymentStatus != "paid" || string.IsNullOrEmpty(order.PaymentId))
                return;

            var refunded = await _yooKassa.RefundAsync(order.PaymentId, order.Total, $"refund-order-{order.Id}",
                await Fiscal.ReceiptLines.LoadAsync(_context, order), order.Phone);
            if (refunded)
            {
                order.PaymentStatus = "refunded";
                _logger.LogInformation("💸 Возврат оплаты по заказу #{OrderId}", order.Id);
            }
            else
            {
                _logger.LogWarning("Не удалось вернуть оплату по заказу #{OrderId} — сделайте возврат в личном кабинете ЮKassa", order.Id);
            }
        }

        // 1 балл за каждые 100 ₽, один раз за заказ
        private async Task AwardPointsAsync(Order order)
        {
            if (!order.UserId.HasValue) return;

            var alreadyAwarded = await _context.UserPointsTransactions
                .AnyAsync(t => t.OrderId == order.Id && t.Type == "earned");
            if (alreadyAwarded) return;

            var pointsEarned = (int)Math.Floor(order.Total / 100);
            if (pointsEarned <= 0) return;

            var user = await _context.Users.FindAsync(order.UserId.Value);
            if (user == null) return;

            user.PointsBalance += pointsEarned;
            _context.UserPointsTransactions.Add(new UserPointsTransaction
            {
                UserId = user.Id,
                Type = "earned",
                Amount = pointsEarned,
                Description = $"Начисление за заказ #{order.Id}",
                OrderId = order.Id,
                CreatedAt = DateTime.UtcNow
            });

            _logger.LogInformation("💰 Начислено {Points} баллов пользователю {UserId} за заказ {OrderId}",
                pointsEarned, user.Id, order.Id);
        }
    }
}
