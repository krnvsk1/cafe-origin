using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Cafe.API.Authorization;
using Cafe.API.Extensions;
using Cafe.API.Models;
using Cafe.API.Services;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Подтверждение телефона по SMS для вошедшего пользователя (вход — через Telegram).
    /// Подтверждённый телефон обязателен для заказа доставки, когда SMS-сервис подключён.
    /// </summary>
    [ApiController]
    [Route("api/auth/phone")]
    public class PhoneController : ControllerBase
    {
        private const int CodesPerHour = 5;

        private readonly IVerificationService _verification;
        private readonly ISmsService _sms;
        private readonly UserManager<AppUser> _users;
        private readonly IPermissionService _permissions;
        private readonly IMemoryCache _cache;

        public PhoneController(IVerificationService verification, ISmsService sms, UserManager<AppUser> users, IPermissionService permissions, IMemoryCache cache)
        {
            _verification = verification;
            _sms = sms;
            _users = users;
            _permissions = permissions;
            _cache = cache;
        }

        public class SendRequest { public string Phone { get; set; } = string.Empty; }
        public class VerifyRequest { public string Phone { get; set; } = string.Empty; public string Code { get; set; } = string.Empty; }

        /// <summary>Нужно ли подтверждать телефон перед доставкой (SMS-сервис подключён)</summary>
        public static bool Required(ISmsService sms) => sms.Enabled;

        // GET: api/auth/phone — нужно ли подтверждение телефона для доставки
        [HttpGet]
        public IActionResult Info() => Ok(new { Required = Required(_sms) });

        // POST: api/auth/phone/send-code — SMS с кодом на номер
        [HttpPost("send-code")]
        [Authorize]
        public async Task<IActionResult> SendCode([FromBody] SendRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();
            var phone = VerificationService.NormalizePhone(request.Phone ?? "");
            if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+7\d{10}$"))
                return BadRequest(new { Message = "Укажите номер в формате +7 999 123-45-67" });

            // Не больше 5 SMS в час на пользователя — SMS платные
            var key = $"sms-codes:{userId}";
            var sent = _cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); return new int[1]; })!;
            if (sent[0] >= CodesPerHour)
                return BadRequest(new { Message = "Слишком много SMS за час — попробуйте позже" });

            var (ok, message, retryAfter) = await _verification.CreateAndSendCodeAsync(phone);
            if (!ok) return BadRequest(new { Message = message, RetryAfter = retryAfter });
            Interlocked.Increment(ref sent[0]);
            return Ok(new { Message = "Код отправлен", Phone = phone });
        }

        // POST: api/auth/phone/verify — код верный: телефон сохраняется в профиле как подтверждённый
        [HttpPost("verify")]
        [Authorize]
        public async Task<IActionResult> Verify([FromBody] VerifyRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();
            var user = await _users.FindByIdAsync(userId.Value.ToString());
            if (user == null) return Unauthorized();

            var phone = VerificationService.NormalizePhone(request.Phone ?? "");
            var (ok, message) = await _verification.VerifyCodeAsync(phone, (request.Code ?? "").Trim());
            if (!ok) return BadRequest(new { Message = message });

            user.PhoneNumber = phone;
            user.PhoneVerified = true;
            user.UpdatedAt = DateTime.UtcNow;
            await _users.UpdateAsync(user);
            var roles = await _users.GetRolesAsync(user);
            return Ok(new { User = await UserDtoFactory.CreateAsync(user, roles, _permissions) });
        }
    }
}
