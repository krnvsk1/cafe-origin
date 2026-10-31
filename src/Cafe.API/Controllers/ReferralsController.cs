using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Options;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Реферальная программа «Пригласи друга»
    /// </summary>
    [ApiController]
    [Route("api/referrals")]
    public class ReferralsController : ControllerBase
    {
        // Без похожих символов (0/O, 1/I/L)
        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        private readonly ApplicationDbContext _context;
        private readonly ReferralOptions _options;

        public ReferralsController(ApplicationDbContext context, IOptions<ReferralOptions> options)
        {
            _context = context;
            _options = options.Value;
        }

        // GET: api/referrals/me — мой код и статистика приглашений
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return Unauthorized();

            if (string.IsNullOrEmpty(user.ReferralCode))
            {
                // Генерируем уникальный код при первом обращении
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var code = new string(Enumerable.Range(0, 6).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
                    if (await _context.Users.AnyAsync(u => u.ReferralCode == code)) continue;
                    user.ReferralCode = code;
                    try
                    {
                        await _context.SaveChangesAsync();
                        break;
                    }
                    catch (DbUpdateException)
                    {
                        user.ReferralCode = null; // коллизия — пробуем ещё раз
                    }
                }
            }

            var invited = await _context.Users.AsNoTracking()
                .Where(u => u.ReferredByUserId == user.Id)
                .Select(u => new { u.ReferralRewardedAt })
                .ToListAsync();

            var earned = await _context.UserPointsTransactions.AsNoTracking()
                .Where(t => t.UserId == user.Id && t.Type == "referral")
                .SumAsync(t => (int?)t.Amount) ?? 0;

            return Ok(new
            {
                _options.Enabled,
                Code = user.ReferralCode,
                InvitedCount = invited.Count,
                RewardedCount = invited.Count(i => i.ReferralRewardedAt != null),
                EarnedPoints = earned,
                _options.InviterBonus,
                _options.InviteeBonus,
                _options.MinOrderAmount
            });
        }

        // GET: api/referrals/check/{code} — существует ли код (для подсказки при регистрации)
        [HttpGet("check/{code}")]
        public async Task<IActionResult> Check(string code)
        {
            var normalized = code.Trim().ToUpperInvariant();
            var inviter = await _context.Users.AsNoTracking()
                .Where(u => u.ReferralCode == normalized)
                .Select(u => new { u.FirstName })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                Valid = _options.Enabled && inviter != null,
                InviterName = inviter?.FirstName,
                _options.InviteeBonus,
                _options.MinOrderAmount
            });
        }
    }
}
