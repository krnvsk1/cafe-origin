using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Models;
using Kumashaurma.API.Services;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Интерфейс курьера: свободные заказы на доставку, свои заказы, смена статусов
    /// </summary>
    [ApiController]
    [Route("api/courier")]
    [Authorize(Roles = "courier,admin,manager")]
    public class CourierController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IOrderStatusService _statusService;
        private readonly IOrderEventBus _events;
        private readonly UserManager<AppUser> _userManager;

        public CourierController(
            ApplicationDbContext context,
            IOrderStatusService statusService,
            IOrderEventBus events,
            UserManager<AppUser> userManager)
        {
            _context = context;
            _statusService = statusService;
            _events = events;
            _userManager = userManager;
        }

        public class AssignRequest
        {
            public int? CourierId { get; set; }
        }

        // GET: api/courier/orders — свободные (готовятся/готовы, без курьера) и мои активные
        [HttpGet("orders")]
        public async Task<IActionResult> GetOrders()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var since = DateTime.UtcNow.AddDays(-2);
            var orders = await _context.Orders
                .AsNoTracking()
                .Include(o => o.OrderItems)
                .Where(o => o.DeliveryType == "Доставка" && o.CreatedAt >= since)
                .Where(o => o.PaymentMethod != "online" || o.PaymentStatus == "paid")
                .Where(o =>
                    (o.CourierId == null && (o.Status == OrderStatuses.Preparing || o.Status == OrderStatuses.Ready))
                    || (o.CourierId == userId.Value && (o.Status == OrderStatuses.Preparing || o.Status == OrderStatuses.Ready || o.Status == OrderStatuses.OnTheWay)))
                .OrderBy(o => o.ScheduledFor ?? o.CreatedAt)
                .ToListAsync();

            object Map(Order o) => new
            {
                o.Id,
                o.Status,
                o.CustomerName,
                o.Phone,
                o.Address,
                o.Notes,
                o.Total,
                o.CreatedAt,
                o.ScheduledFor,
                o.PickedUpAt,
                o.PaymentMethod,
                // Сколько получить с клиента при доставке
                AmountToCollect = o.PaymentMethod == "online" ? 0 : o.Total,
                ItemsCount = o.OrderItems.Sum(i => i.Quantity),
                Items = o.OrderItems.Select(i => $"{i.Name} × {i.Quantity}")
            };

            return Ok(new
            {
                Available = orders.Where(o => o.CourierId == null).Select(Map),
                Mine = orders.Where(o => o.CourierId == userId.Value).Select(Map)
            });
        }

        // POST: api/courier/orders/{id}/take — взять заказ себе
        [HttpPost("orders/{id}/take")]
        public async Task<IActionResult> Take(int id)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            // Атомарно: заказ достаётся только одному курьеру
            var updated = await _context.Orders
                .Where(o => o.Id == id && o.CourierId == null && o.DeliveryType == "Доставка"
                    && (o.Status == OrderStatuses.Preparing || o.Status == OrderStatuses.Ready))
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.CourierId, (int?)userId.Value));

            if (updated == 0)
                return BadRequest(new { Message = "Заказ уже взят другим курьером или недоступен" });

            var order = await _context.Orders.AsNoTracking().FirstAsync(o => o.Id == id);
            _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
            return Ok(new { order.Id, order.CourierId });
        }

        // POST: api/courier/orders/{id}/release — отказаться от заказа (пока не забрал)
        [HttpPost("orders/{id}/release")]
        public async Task<IActionResult> Release(int id)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id && o.CourierId == userId.Value);
            if (order == null) return NotFound(new { Message = "Заказ не найден" });
            if (order.Status == OrderStatuses.OnTheWay)
                return BadRequest(new { Message = "Заказ уже в пути — обратитесь к менеджеру" });

            order.CourierId = null;
            await _context.SaveChangesAsync();
            _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
            return Ok(new { order.Id });
        }

        // POST: api/courier/orders/{id}/pickup — забрал заказ, выехал
        [HttpPost("orders/{id}/pickup")]
        public async Task<IActionResult> PickUp(int id)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id && o.CourierId == userId.Value);
            if (order == null) return NotFound(new { Message = "Заказ не найден" });
            if (order.Status != OrderStatuses.Ready && order.Status != OrderStatuses.Preparing)
                return BadRequest(new { Message = $"Нельзя забрать заказ в статусе «{order.Status}»" });

            await _statusService.ChangeStatusAsync(order, OrderStatuses.OnTheWay);
            return Ok(new { order.Id, order.Status });
        }

        // POST: api/courier/orders/{id}/delivered — доставил
        [HttpPost("orders/{id}/delivered")]
        public async Task<IActionResult> Delivered(int id)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id && o.CourierId == userId.Value);
            if (order == null) return NotFound(new { Message = "Заказ не найден" });
            if (order.Status != OrderStatuses.OnTheWay)
                return BadRequest(new { Message = "Сначала отметьте, что забрали заказ" });

            await _statusService.ChangeStatusAsync(order, OrderStatuses.Delivered);
            return Ok(new { order.Id, order.Status });
        }

        // GET: api/courier/couriers — список курьеров (для назначения менеджером)
        [HttpGet("couriers")]
        [Authorize(Roles = "admin,manager")]
        public async Task<IActionResult> GetCouriers()
        {
            var couriers = await _userManager.GetUsersInRoleAsync(AppRoles.Courier);
            return Ok(couriers.Select(c => new
            {
                c.Id,
                Name = string.Join(' ', new[] { c.FirstName, c.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
                Phone = c.PhoneNumber
            }));
        }

        // POST: api/courier/orders/{id}/assign — менеджер назначает/снимает курьера
        [HttpPost("orders/{id}/assign")]
        [Authorize(Roles = "admin,manager")]
        public async Task<IActionResult> Assign(int id, [FromBody] AssignRequest request)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound(new { Message = "Заказ не найден" });

            if (request.CourierId.HasValue)
            {
                var courier = await _userManager.FindByIdAsync(request.CourierId.Value.ToString());
                if (courier == null || !await _userManager.IsInRoleAsync(courier, AppRoles.Courier))
                    return BadRequest(new { Message = "Курьер не найден" });
            }

            order.CourierId = request.CourierId;
            await _context.SaveChangesAsync();
            _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
            return Ok(new { order.Id, order.CourierId });
        }
    }
}
