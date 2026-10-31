using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Models;
using Kumashaurma.API.Services;

namespace Kumashaurma.API.Controllers
{
    /// <summary>Подписка браузера сотрудника на уведомления (официанту — «блюдо готово»)</summary>
    [ApiController]
    [Route("api/push")]
    [Authorize]
    public class PushController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly WebPushService _push;

        public PushController(ApplicationDbContext context, WebPushService push)
        {
            _context = context;
            _push = push;
        }

        public class SubscriptionKeys
        {
            public string P256dh { get; set; } = string.Empty;
            public string Auth { get; set; } = string.Empty;
        }

        public class SubscribeRequest
        {
            public string Endpoint { get; set; } = string.Empty;
            public SubscriptionKeys Keys { get; set; } = new();
        }

        public class UnsubscribeRequest
        {
            public string Endpoint { get; set; } = string.Empty;
        }

        // GET: api/push/key — открытый ключ VAPID для подписки в браузере
        [HttpGet("key")]
        public async Task<IActionResult> Key() => Ok(new { publicKey = await _push.GetPublicKeyAsync() });

        // POST: api/push/subscribe — сохранить подписку этого браузера
        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest r)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();
            if (!User.IsStaff()) return Forbid();
            if (r.Endpoint.Length > 1000 || !WebPushService.IsAllowedEndpoint(r.Endpoint))
                return BadRequest(new { message = "Этот браузер не поддерживает уведомления" });
            if (string.IsNullOrWhiteSpace(r.Keys.P256dh) || r.Keys.P256dh.Length > 200 || string.IsNullOrWhiteSpace(r.Keys.Auth) || r.Keys.Auth.Length > 100)
                return BadRequest(new { message = "Неверные ключи подписки" });

            var sub = await _context.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == r.Endpoint);
            if (sub == null)
            {
                sub = new WebPushSubscription { Endpoint = r.Endpoint };
                _context.PushSubscriptions.Add(sub);
            }
            sub.UserId = userId.Value;
            sub.P256dh = r.Keys.P256dh;
            sub.Auth = r.Keys.Auth;
            var ua = Request.Headers.UserAgent.ToString();
            sub.UserAgent = ua.Length > 300 ? ua[..300] : ua;
            sub.CreatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/push/unsubscribe — этот браузер больше не получает уведомления
        [HttpPost("unsubscribe")]
        public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest r)
        {
            var userId = User.GetUserId();
            var sub = await _context.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == r.Endpoint && s.UserId == userId);
            if (sub != null)
            {
                _context.PushSubscriptions.Remove(sub);
                await _context.SaveChangesAsync();
            }
            return NoContent();
        }

        // POST: api/push/test — проверочное уведомление себе
        [HttpPost("test")]
        public async Task<IActionResult> Test()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();
            var sent = await _push.SendAsync(new[] { userId.Value },
                new PushMessage("Уведомления включены", "Сюда придёт, когда блюдо будет готово", "/waiter", $"test-{DateTime.UtcNow.Ticks}"));
            return Ok(new { sent });
        }
    }
}
