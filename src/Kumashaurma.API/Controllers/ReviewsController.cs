using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Отзывы и оценки заказов
    /// </summary>
    [ApiController]
    [Route("api/reviews")]
    public class ReviewsController : ControllerBase
    {
        private static readonly string[] CompletedStatuses = { "Доставлен", "Выполнен" };

        private readonly ApplicationDbContext _context;
        private readonly ILogger<ReviewsController> _logger;

        public ReviewsController(ApplicationDbContext context, ILogger<ReviewsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public class CreateReviewRequest
        {
            public int OrderId { get; set; }
            public int Rating { get; set; }
            public string? Comment { get; set; }
        }

        public class ReplyRequest
        {
            public string Reply { get; set; } = string.Empty;
        }

        // POST: api/reviews — оставить отзыв о своём завершённом заказе
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateReviewRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            if (request.Rating < 1 || request.Rating > 5)
                return BadRequest(new { Message = "Оценка должна быть от 1 до 5" });

            var comment = request.Comment?.Trim();
            if (comment?.Length > 1000)
                return BadRequest(new { Message = "Отзыв не должен превышать 1000 символов" });

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId && o.UserId == userId.Value);
            if (order == null)
                return NotFound(new { Message = "Заказ не найден" });

            if (!CompletedStatuses.Contains(order.Status))
                return BadRequest(new { Message = "Оценить можно только полученный заказ" });

            if (await _context.OrderReviews.AnyAsync(r => r.OrderId == order.Id))
                return BadRequest(new { Message = "Вы уже оценили этот заказ" });

            var review = new OrderReview
            {
                OrderId = order.Id,
                UserId = userId.Value,
                Rating = request.Rating,
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment,
                CreatedAt = DateTime.UtcNow
            };
            _context.OrderReviews.Add(review);
            await _context.SaveChangesAsync();

            _logger.LogInformation("⭐ Отзыв на заказ #{OrderId}: {Rating}/5", order.Id, review.Rating);

            return Ok(ToDto(review, null));
        }

        // GET: api/reviews/my — мои отзывы (для отметки оценённых заказов)
        [HttpGet("my")]
        [Authorize]
        public async Task<IActionResult> GetMy()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var reviews = await _context.OrderReviews
                .AsNoTracking()
                .Where(r => r.UserId == userId.Value)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return Ok(reviews.Select(r => ToDto(r, null)));
        }

        // GET: api/reviews?rating=1&withComment=true&page=1&pageSize=20 — для админки
        [HttpGet]
        [Authorize(Roles = "admin,manager")]
        public async Task<IActionResult> GetAll(
            [FromQuery] int? rating,
            [FromQuery] bool? withComment,
            [FromQuery] bool? unanswered,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _context.OrderReviews.AsNoTracking().AsQueryable();
            if (rating.HasValue) query = query.Where(r => r.Rating == rating.Value);
            if (withComment == true) query = query.Where(r => r.Comment != null && r.Comment != "");
            if (unanswered == true) query = query.Where(r => r.AdminReply == null);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    Review = r,
                    CustomerName = r.Order!.CustomerName,
                    OrderTotal = r.Order!.Total,
                    Items = r.Order!.OrderItems.Select(i => i.Name).ToList()
                })
                .ToListAsync();

            // Сводка по всем отзывам (без фильтров)
            var all = _context.OrderReviews.AsNoTracking();
            var count = await all.CountAsync();
            var average = count > 0 ? await all.AverageAsync(r => (double)r.Rating) : 0;
            var distribution = await all
                .GroupBy(r => r.Rating)
                .Select(g => new { Rating = g.Key, Count = g.Count() })
                .ToListAsync();

            return Ok(new
            {
                Total = total,
                Page = page,
                PageSize = pageSize,
                Summary = new
                {
                    Count = count,
                    Average = Math.Round(average, 2),
                    Distribution = Enumerable.Range(1, 5)
                        .Select(n => new { Rating = n, Count = distribution.FirstOrDefault(d => d.Rating == n)?.Count ?? 0 })
                },
                Items = items.Select(x => new
                {
                    Review = ToDto(x.Review, x.CustomerName),
                    x.OrderTotal,
                    x.Items
                })
            });
        }

        // POST: api/reviews/{id}/reply — ответ заведения
        [HttpPost("{id}/reply")]
        [Authorize(Roles = "admin,manager")]
        public async Task<IActionResult> Reply(int id, [FromBody] ReplyRequest request)
        {
            var review = await _context.OrderReviews.FindAsync(id);
            if (review == null) return NotFound(new { Message = "Отзыв не найден" });

            var reply = request.Reply?.Trim() ?? string.Empty;
            if (reply.Length > 1000)
                return BadRequest(new { Message = "Ответ не должен превышать 1000 символов" });

            review.AdminReply = reply.Length == 0 ? null : reply;
            review.RepliedAt = reply.Length == 0 ? null : DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(ToDto(review, null));
        }

        private static object ToDto(OrderReview r, string? customerName) => new
        {
            r.Id,
            r.OrderId,
            r.Rating,
            r.Comment,
            r.AdminReply,
            r.RepliedAt,
            r.CreatedAt,
            CustomerName = customerName
        };
    }
}
