using Microsoft.Extensions.Options;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;
using Kumashaurma.API.Options;

namespace Kumashaurma.API.Services
{
    public interface IPaymentService
    {
        bool OnlineEnabled { get; }

        /// <summary>Создать платёж ЮKassa для заказа. Возвращает ссылку на оплату или null.</summary>
        Task<string?> StartAsync(Order order, CancellationToken ct = default);

        /// <summary>Сверить статус платежа с ЮKassa и применить его к заказу</summary>
        Task SyncAsync(Order order, CancellationToken ct = default);

        /// <summary>Отменить онлайн-заказ, который так и не оплатили</summary>
        Task CancelIfUnpaidAsync(Order order, CancellationToken ct = default);
    }

    public class PaymentService : IPaymentService
    {
        private readonly ApplicationDbContext _context;
        private readonly YooKassaClient _yooKassa;
        private readonly IOrderStatusService _statusService;
        private readonly IOrderEventBus _events;
        private readonly YooKassaOptions _options;
        private readonly ILogger<PaymentService> _logger;
        private readonly BrandSettingsProvider _brand;

        public PaymentService(
            ApplicationDbContext context,
            YooKassaClient yooKassa,
            IOrderStatusService statusService,
            IOrderEventBus events,
            IOptions<YooKassaOptions> options,
            ILogger<PaymentService> logger,
            BrandSettingsProvider brand)
        {
            _context = context;
            _yooKassa = yooKassa;
            _statusService = statusService;
            _events = events;
            _options = options.Value;
            _brand = brand;
            _logger = logger;
        }

        public bool OnlineEnabled => _yooKassa.Enabled && !string.IsNullOrWhiteSpace(_options.ReturnUrl);

        public async Task<string?> StartAsync(Order order, CancellationToken ct = default)
        {
            if (!OnlineEnabled || order.PaymentStatus == "paid" || order.Status == OrderStatuses.Cancelled)
                return null;

            var returnUrl = $"{_options.ReturnUrl!.TrimEnd('/')}/order/{order.Id}/success?token={order.AccessToken}&paid=1";
            // Новый ключ на каждую попытку: повторная оплата после отмены создаёт новый платёж
            var attempt = DateTime.UtcNow.Ticks;
            var payment = await _yooKassa.CreatePaymentAsync(
                order.Id, order.Total, $"Заказ #{order.Id} — {(await _brand.GetAsync()).DisplayName}", returnUrl, order.Phone,
                await Fiscal.ReceiptLines.LoadAsync(_context, order, ct), $"order-{order.Id}-{attempt}", ct);

            if (payment == null) return null;

            order.PaymentId = payment.Id;
            order.PaymentStatus = "pending";
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("💳 Платёж {PaymentId} создан для заказа #{OrderId} на {Total} ₽", payment.Id, order.Id, order.Total);
            return payment.ConfirmationUrl;
        }

        public async Task CancelIfUnpaidAsync(Order order, CancellationToken ct = default)
        {
            await SyncAsync(order, ct);
            if (order.PaymentStatus == "paid" || order.Status == OrderStatuses.Cancelled) return;

            _logger.LogInformation("⌛ Заказ #{OrderId} не оплачен вовремя — отменяем", order.Id);
            await _statusService.ChangeStatusAsync(order, OrderStatuses.Cancelled);
        }

        public async Task SyncAsync(Order order, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(order.PaymentId) || order.PaymentStatus is "paid" or "refunded")
                return;

            var payment = await _yooKassa.GetPaymentAsync(order.PaymentId, ct);
            if (payment == null) return;

            // Платёж должен относиться именно к этому заказу и сумма должна совпадать
            if (payment.OrderId != order.Id.ToString() || payment.Amount != Math.Round(order.Total, 2))
            {
                _logger.LogWarning("ЮKassa: платёж {PaymentId} не соответствует заказу #{OrderId}", payment.Id, order.Id);
                return;
            }

            if (payment.Status == "succeeded" && payment.Paid)
            {
                order.PaymentStatus = "paid";
                order.PaidAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
                _logger.LogInformation("✅ Заказ #{OrderId} оплачен ({PaymentId})", order.Id, payment.Id);

                // Оплаченный онлайн-заказ впервые становится виден кухне и персоналу
                _events.Publish(new OrderEvent("created", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
            }
            else if (payment.Status == "canceled")
            {
                order.PaymentStatus = "canceled";
                await _context.SaveChangesAsync(ct);
                _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
            }
        }
    }
}
