using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Controllers
{
    /// <summary>Журнал отменённых блюд: что официанты убрали из чеков, когда, почему, списано ли со склада</summary>
    [ApiController]
    [Route("api/voids")]
    [HasPermission(Permissions.Voids)]
    public class VoidsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public VoidsController(ApplicationDbContext context) => _context = context;

        // GET: api/voids?from=2026-10-01&to=2026-10-31 (даты по Москве, включительно)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var toDay = (to ?? DateTime.UtcNow.AddHours(3)).Date;
            var fromDay = (from ?? toDay.AddDays(-6)).Date;
            if (toDay < fromDay || (toDay - fromDay).TotalDays > 92) return BadRequest(new { message = "Период — до 3 месяцев" });
            var fromUtc = fromDay.AddHours(-3);
            var toUtc = toDay.AddDays(1).AddHours(-3);

            var rows = await _context.OrderItemVoids.AsNoTracking()
                .Where(v => v.RequestedAt >= fromUtc && v.RequestedAt < toUtc)
                .OrderByDescending(v => v.RequestedAt)
                .ToListAsync();
            var userIds = rows.SelectMany(v => new[] { v.RequestedByUserId, v.ResolvedByUserId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
            var users = (await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FirstName, u.LastName, u.PhoneNumber }).ToListAsync())
                .ToDictionary(u => u.Id, u => string.Join(' ', new[] { u.FirstName, u.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } n ? n : u.PhoneNumber ?? $"#{u.Id}");
            var checkIds = rows.Where(v => v.CheckId != null).Select(v => v.CheckId!.Value).Distinct().ToList();
            var tables = await _context.TableChecks.AsNoTracking().Include(c => c.Table).Where(c => checkIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Table!.Name);

            var done = rows.Where(v => v.Status == VoidStatuses.Done).ToList();
            return Ok(new
            {
                from = fromDay,
                to = toDay,
                summary = new
                {
                    count = done.Sum(v => v.Quantity),
                    amount = done.Sum(v => v.Amount),
                    writtenOff = done.Where(v => v.WriteOff == true).Sum(v => v.Amount),
                    pending = rows.Count(v => v.Status == VoidStatuses.Pending)
                },
                items = rows.Select(v => new
                {
                    v.Id, v.OrderId, v.Name, v.Quantity, v.Amount, v.Reason, v.Status, v.WriteOff, v.OrderStatus, v.RequestedAt, v.ResolvedAt,
                    table = v.CheckId != null ? tables.GetValueOrDefault(v.CheckId.Value) : null,
                    requestedBy = v.RequestedByUserId != null ? users.GetValueOrDefault(v.RequestedByUserId.Value) : null,
                    resolvedBy = v.ResolvedByUserId != null && v.ResolvedByUserId != v.RequestedByUserId ? users.GetValueOrDefault(v.ResolvedByUserId.Value) : null
                })
            });
        }
    }
}
