using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Authorization;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services
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

        /// <summary>Открытая и подтверждённая смена официанта — только с ней он работает со счетами</summary>
        public Task<WaiterShift?> CurrentWaiterAsync(int userId) =>
            _context.WaiterShifts.Where(s => s.UserId == userId && s.ClosedAt == null && s.ApprovedAt != null).OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync();

        /// <summary>Смена официанта, которая ждёт подтверждения менеджера</summary>
        public Task<WaiterShift?> PendingWaiterAsync(int userId) =>
            _context.WaiterShifts.Where(s => s.UserId == userId && s.ClosedAt == null && s.ApprovedAt == null).OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync();

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

        /// <param name="approved">Можно без подтверждения (право «Смена без подтверждения» или «Подтверждение смен»)</param>
        public async Task<WaiterShift> OpenWaiterAsync(int userId, bool approved = true)
        {
            var venue = await CurrentVenueAsync() ?? throw new ShiftException("Смена заведения ещё не открыта — попросите менеджера открыть смену");
            var mine = await CurrentWaiterAsync(userId) ?? await PendingWaiterAsync(userId);
            if (mine != null)
            {
                if (approved && mine.ApprovedAt == null) { mine.ApprovedAt = DateTime.UtcNow; mine.ApprovedBy = userId; await _context.SaveChangesAsync(); }
                return mine;
            }
            var now = DateTime.UtcNow;
            var w = new WaiterShift { VenueShiftId = venue.Id, UserId = userId, OpenedAt = now, ApprovedAt = approved ? now : null, ApprovedBy = approved ? userId : null };
            _context.WaiterShifts.Add(w);
            await _context.SaveChangesAsync();
            return w;
        }

        /// <summary>Менеджер подтверждает смену официанта (смена начинается с момента подтверждения)</summary>
        public async Task<WaiterShift> ApproveWaiterAsync(int shiftId, int? byUserId)
        {
            var w = await _context.WaiterShifts.FirstOrDefaultAsync(x => x.Id == shiftId) ?? throw new ShiftException("Смена не найдена");
            if (w.ClosedAt != null) throw new ShiftException("Запрос уже отменён или отклонён");
            if (w.ApprovedAt != null) return w;
            var now = DateTime.UtcNow;
            w.ApprovedAt = now;
            w.ApprovedBy = byUserId;
            w.OpenedAt = now;
            await _context.SaveChangesAsync();
            return w;
        }

        /// <summary>Отклонить запрос на смену (или официант сам отменил свой запрос)</summary>
        public async Task<WaiterShift> RejectWaiterAsync(int shiftId)
        {
            var w = await _context.WaiterShifts.FirstOrDefaultAsync(x => x.Id == shiftId) ?? throw new ShiftException("Смена не найдена");
            if (w.ApprovedAt != null) throw new ShiftException("Смена уже подтверждена — её закрывает сам официант");
            w.ClosedAt ??= DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return w;
        }

        public async Task<WaiterShift> CloseWaiterAsync(int userId)
        {
            // Запрос ещё не подтвердили — просто отменяем его
            if (await PendingWaiterAsync(userId) is { } pending) return await RejectWaiterAsync(pending.Id);
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
