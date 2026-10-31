using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Services;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Живые обновления заказов через Server-Sent Events (text/event-stream).
    /// Браузерный EventSource не умеет слать заголовки, поэтому JWT передаётся в ?access_token=
    /// (см. настройку JwtBearer в Program.cs), а гость — ?token= своего заказа.
    /// </summary>
    [ApiController]
    [Route("api/events")]
    public class EventsController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

        private readonly IOrderEventBus _bus;
        private readonly ApplicationDbContext _context;

        public EventsController(IOrderEventBus bus, ApplicationDbContext context)
        {
            _bus = bus;
            _context = context;
        }

        // GET: api/events/orders — персонал получает все события, клиент — по своим заказам
        [HttpGet("orders")]
        public async Task Orders(CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            if (userId == null)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var isStaff = User.IsStaff();
            await StreamAsync(evt => isStaff || evt.UserId == userId, cancellationToken);
        }

        // GET: api/events/order/{id}?token=... — события одного заказа (в том числе для гостя)
        [HttpGet("order/{id}")]
        public async Task Order(int id, [FromQuery] string? token, CancellationToken cancellationToken)
        {
            var order = await _context.Orders.AsNoTracking()
                .Select(o => new { o.Id, o.UserId, o.AccessToken })
                .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

            var userId = User.GetUserId();
            var allowed = order != null && (
                User.IsStaff()
                || (userId != null && order.UserId == userId)
                || (!string.IsNullOrEmpty(token) && token == order.AccessToken));

            if (!allowed)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await StreamAsync(evt => evt.OrderId == id, cancellationToken);
        }

        private async Task StreamAsync(Func<OrderEvent, bool> filter, CancellationToken cancellationToken)
        {
            Response.Headers.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no"; // отключить буферизацию в nginx

            using var subscription = _bus.Subscribe();

            // Клиент переподключится через 3 секунды, если соединение оборвётся
            await Response.WriteAsync("retry: 3000\n: connected\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    heartbeat.CancelAfter(HeartbeatInterval);

                    OrderEvent? evt = null;
                    try
                    {
                        evt = await subscription.Reader.ReadAsync(heartbeat.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Пауза без событий — шлём комментарий, чтобы прокси не закрыли соединение
                        await Response.WriteAsync(": ping\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                        continue;
                    }

                    if (!filter(evt)) continue;

                    var payload = JsonSerializer.Serialize(evt, JsonOptions);
                    await Response.WriteAsync($"event: order\ndata: {payload}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Клиент отключился
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                // Подписка закрыта
            }
        }
    }
}
