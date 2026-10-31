using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services
{
    /// <summary>
    /// Смены: сначала открывается смена заведения на день, затем каждый официант открывает свою.
    /// Счёт открывается только в открытую смену официанта и помнит её.
    /// </summary>
    public class ShiftService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPermissionService _permissions;

        public ShiftService(ApplicationDbContext context, IPermissionService permissions)
        {
            _context = context;
            _permissions = permissions;
        }

        public class ShiftException : Exception
        {
            public ShiftException(string message) : base(message) { }
        }

        /// <summary>Рабочий день по Москве (полночь)</summary>
        public static DateTime BusinessDate(DateTime utc) => utc.AddHours(3).Date;

        public async Task<bool> HasAsync(ClaimsPrincipal user, string permission)
        {
            var perms = await _permissions.GetPermissionsAsync(user.FindAll(ClaimTypes.Role).Select(c => c.Value));
            return perms.Contains(permission);
        }

        public Task<VenueShift?> CurrentVenueAsync() =>
            _context.VenueShifts.Where(s => s.ClosedAt == null).OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync();

        public Task<WaiterShift?> CurrentWaiterAsync(int userId) =>
            _context.WaiterShifts.Where(s => s.UserId == userId && s.ClosedAt == null).OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync();

        public async Task<VenueShift> OpenVenueAsync(int? userId)
        {
            var current = await CurrentVenueAsync();
            if (current != null) throw new ShiftException("Смена заведения уже открыта");
            var now = DateTime.UtcNow;
            var s = new VenueShift { BusinessDate = BusinessDate(now), OpenedAt = now, OpenedByUserId = userId };
            _context.VenueShifts.Add(s);
            await _context.SaveChangesAsync();
            return s;
        }

        /// <summary>Закрыть смену заведения: открытых счетов быть не должно; смены официантов закрываются вместе с ней</summary>
        public async Task<VenueShift> CloseVenueAsync(int? userId)
        {
            var s = await CurrentVenueAsync() ?? throw new ShiftException("Смена заведения не открыта");
            var open = await _context.TableChecks.Include(c => c.Table).Where(c => c.Status == CheckStatuses.Open).ToListAsync();
            if (open.Count > 0)
                throw new ShiftException($"Есть открытые счета: {string.Join(", ", open.Take(5).Select(c => $"стол {c.Table?.Name}"))}{(open.Count > 5 ? "…" : "")}. Закройте их или отмените.");
            var now = DateTime.UtcNow;
            foreach (var w in await _context.WaiterShifts.Where(w => w.VenueShiftId == s.Id && w.ClosedAt == null).ToListAsync())
                w.ClosedAt = now;
            s.ClosedAt = now;
            s.ClosedByUserId = userId;
            await _context.SaveChangesAsync();
            return s;
        }

        public async Task<WaiterShift> OpenWaiterAsync(int userId)
        {
            var venue = await CurrentVenueAsync() ?? throw new ShiftException("Смена заведения ещё не открыта — попросите менеджера открыть смену");
            var mine = await CurrentWaiterAsync(userId);
            if (mine != null) return mine;
            var w = new WaiterShift { VenueShiftId = venue.Id, UserId = userId, OpenedAt = DateTime.UtcNow };
            _context.WaiterShifts.Add(w);
            await _context.SaveChangesAsync();
            return w;
        }

        public async Task<WaiterShift> CloseWaiterAsync(int userId)
        {
            var w = await CurrentWaiterAsync(userId) ?? throw new ShiftException("Ваша смена не открыта");
            var open = await _context.TableChecks.Include(c => c.Table).Where(c => c.Status == CheckStatuses.Open && c.WaiterId == userId).ToListAsync();
            if (open.Count > 0)
                throw new ShiftException($"У вас открыты счета: {string.Join(", ", open.Take(5).Select(c => $"стол {c.Table?.Name}"))}. Закройте их или попросите менеджера передать другому официанту.");
            w.ClosedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return w;
        }
    }
}
