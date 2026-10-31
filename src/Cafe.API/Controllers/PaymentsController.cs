using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Services;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Онлайн-оплата через ЮKassa
    /// </summary>
    [ApiController]
    [Route("api/payments")]
    public class PaymentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IPaymentService _payments;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(ApplicationDbContext context, IPaymentService payments, ILogger<PaymentsController> logger)
        {
            _context = context;
            _payments = payments;
            _logger = logger;
        }

        // POST: api/payments/orders/{id}/pay?token= — (повторно) получить ссылку на оплату
        [HttpPost("orders/{id}/pay")]
        public async Task<IActionResult> Pay(int id, [FromQuery] string? token, CancellationToken ct)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order == null || !User.CanAccess(order, token))
                return NotFound(new { Message = "Заказ не найден" });

            if (order.PaymentMethod != "online")
                return BadRequest(new { Message = "Этот заказ оплачивается при получении" });
            if (order.PaymentStatus == "paid")
                return BadRequest(new { Message = "Заказ уже оплачен" });
            if (order.Status == OrderStatuses.Cancelled)
                return BadRequest(new { Message = "Заказ отменён" });

            var url = await _payments.StartAsync(order, ct);
            if (url == null)
                return StatusCode(502, new { Message = "Платёжный сервис недоступен, попробуйте позже" });

            return Ok(new { ConfirmationUrl = url });
        }

        // GET: api/payments/orders/{id}/status?token= — статус оплаты (с проверкой в ЮKassa)
        [HttpGet("orders/{id}/status")]
        public async Task<IActionResult> Status(int id, [FromQuery] string? token, CancellationToken ct)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order == null || !User.CanAccess(order, token))
                return NotFound(new { Message = "Заказ не найден" });

            if (order.PaymentMethod == "online" && order.PaymentStatus == "pending")
                await _payments.SyncAsync(order, ct);

            return Ok(new { order.Id, order.PaymentMethod, order.PaymentStatus, order.PaidAt, order.Status });
        }

        // POST: api/payments/yookassa/webhook — уведомления ЮKassa
        // Тело уведомления не подписано, поэтому статус всегда перепроверяется запросом к API ЮKassa.
        [HttpPost("yookassa/webhook")]
        public async Task<IActionResult> Webhook([FromBody] JsonElement notification, CancellationToken ct)
        {
            try
            {
                if (!notification.TryGetProperty("object", out var obj)) return Ok();

                // Для refund.* id платежа лежит в payment_id
                var paymentId = obj.TryGetProperty("payment_id", out var pid) ? pid.GetString()
                    : obj.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(paymentId)) return Ok();

                var order = await _context.Orders.FirstOrDefaultAsync(o => o.PaymentId == paymentId, ct);
                if (order == null)
                {
                    _logger.LogWarning("ЮKassa webhook: заказ для платежа {PaymentId} не найден", paymentId);
                    return Ok();
                }

                await _payments.SyncAsync(order, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "ЮKassa webhook: ошибка обработки");
                // 500 — ЮKassa повторит уведомление позже
                return StatusCode(500);
            }

            return Ok();
        }
    }
}
