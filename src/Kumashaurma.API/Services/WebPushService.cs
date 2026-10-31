using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebPush;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services
{
    /// <summary>Что показать в уведомлении: заголовок, текст, куда открыть по нажатию, метка для замены прошлого</summary>
    public record PushMessage(string Title, string Body, string Url, string Tag);

    /// <summary>
    /// Web Push: ключи VAPID создаются один раз и хранятся в app_settings (webpush_vapid).
    /// Отправка идёт в фоне, чтобы кухня не ждала; мёртвые подписки (404/410) удаляются.
    /// </summary>
    public class WebPushService
    {
        public const string Key = "webpush_vapid";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>Куда браузер разрешает слать: службы push Google, Mozilla, Apple, Microsoft</summary>
        private static readonly string[] AllowedHosts =
            { "fcm.googleapis.com", "android.googleapis.com", "updates.push.services.mozilla.com", "push.apple.com", "notify.windows.com" };

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<WebPushService> _logger;
        private readonly string _subject;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private VapidKeys? _keys;

        private class VapidKeys
        {
            public string PublicKey { get; set; } = string.Empty;
            public string PrivateKey { get; set; } = string.Empty;
        }

        public WebPushService(IServiceScopeFactory scopes, ILogger<WebPushService> logger, IConfiguration config)
        {
            _scopes = scopes;
            _logger = logger;
            _subject = config["WebPush:Subject"] is { Length: > 0 } s ? s : "mailto:admin@example.com";
        }

        public static bool IsAllowedEndpoint(string endpoint) =>
            Uri.TryCreate(endpoint, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
            AllowedHosts.Any(h => u.Host == h || u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

        public async Task<string> GetPublicKeyAsync() => (await KeysAsync()).PublicKey;

        private async Task<VapidKeys> KeysAsync()
        {
            if (_keys != null) return _keys;
            await _lock.WaitAsync();
            try
            {
                if (_keys != null) return _keys;
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == Key);
                var keys = row == null ? null : JsonSerializer.Deserialize<VapidKeys>(row.Value, Json);
                if (keys == null || string.IsNullOrEmpty(keys.PublicKey) || string.IsNullOrEmpty(keys.PrivateKey))
                {
                    var v = VapidHelper.GenerateVapidKeys();
                    keys = new VapidKeys { PublicKey = v.PublicKey, PrivateKey = v.PrivateKey };
                    if (row == null) { row = new AppSetting { Key = Key }; db.AppSettings.Add(row); }
                    row.Value = JsonSerializer.Serialize(keys, Json);
                    row.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
                _keys = keys;
                return keys;
            }
            finally { _lock.Release(); }
        }

        /// <summary>Отправить в фоне на все устройства этих сотрудников</summary>
        public void Enqueue(IEnumerable<int> userIds, PushMessage message)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return;
            _ = Task.Run(async () =>
            {
                try { await SendAsync(ids, message); }
                catch (Exception ex) { _logger.LogWarning(ex, "Не удалось отправить push-уведомление"); }
            });
        }

        /// <summary>Отправить и дождаться: сколько устройств приняли</summary>
        public async Task<int> SendAsync(IReadOnlyCollection<int> userIds, PushMessage message)
        {
            var keys = await KeysAsync();
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var subs = await db.PushSubscriptions.Where(s => userIds.Contains(s.UserId)).ToListAsync();
            if (subs.Count == 0) return 0;

            var client = new WebPushClient();
            var vapid = new VapidDetails(_subject, keys.PublicKey, keys.PrivateKey);
            var payload = JsonSerializer.Serialize(message, Json);
            var options = new Dictionary<string, object>
            {
                ["vapidDetails"] = vapid,
                ["TTL"] = 900,
                ["headers"] = new Dictionary<string, object> { ["Urgency"] = "high" },
            };
            var sent = 0;
            foreach (var s in subs)
            {
                try
                {
                    await client.SendNotificationAsync(new PushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, options);
                    s.LastUsedAt = DateTime.UtcNow;
                    sent++;
                }
                catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                {
                    db.PushSubscriptions.Remove(s);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Push не доставлен на {Host}", new Uri(s.Endpoint).Host);
                }
            }
            await db.SaveChangesAsync();
            return sent;
        }
    }

    /// <summary>
    /// Официанту — «блюдо готово»: сравниваем готовность блюд до и после отметки кухни
    /// и шлём уведомление тому, чей это счёт.
    /// </summary>
    public class WaiterReadyNotifier
    {
        private readonly ApplicationDbContext _context;
        private readonly WebPushService _push;

        public WaiterReadyNotifier(ApplicationDbContext context, WebPushService push)
        {
            _context = context;
            _push = push;
        }

        /// <summary>Сколько штук каждого блюда готово сейчас</summary>
        public async Task<Dictionary<int, int>> SnapshotAsync(Order order)
        {
            if (order.CheckId == null && order.OrderItems.All(i => i.CheckId == null)) return new();
            var ids = order.OrderItems.Select(i => i.Id).ToList();
            var states = await _context.OrderItemKitchenStates.AsNoTracking()
                .Where(x => ids.Contains(x.OrderItemId)).ToDictionaryAsync(x => x.OrderItemId, x => x.ReadyQuantity);
            var whole = KitchenFlow.Rank(order.Status) >= 3;
            return order.OrderItems.ToDictionary(i => i.Id, i => whole ? i.Quantity : Math.Min(i.Quantity, states.GetValueOrDefault(i.Id)));
        }

        public async Task NotifyAsync(Order order, Dictionary<int, int> before)
        {
            if (order.CheckId == null && order.OrderItems.All(i => i.CheckId == null)) return;
            var now = await SnapshotAsync(order);
            var fresh = order.OrderItems
                .Select(i => (Item: i, Qty: now.GetValueOrDefault(i.Id) - before.GetValueOrDefault(i.Id)))
                .Where(x => x.Qty > 0)
                .ToList();
            if (fresh.Count == 0) return;

            var checkIds = fresh.Select(x => x.Item.CheckId ?? order.CheckId).Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
            var checks = await _context.TableChecks.AsNoTracking().Include(c => c.Table)
                .Where(c => checkIds.Contains(c.Id) && c.WaiterId != null).ToListAsync();

            foreach (var c in checks)
            {
                var lines = fresh.Where(x => (x.Item.CheckId ?? order.CheckId) == c.Id).Select(x => $"{x.Qty} × {x.Item.Name}").ToList();
                if (lines.Count == 0) continue;
                var allReady = order.OrderItems.Where(i => (i.CheckId ?? order.CheckId) == c.Id).All(i => now.GetValueOrDefault(i.Id) >= i.Quantity);
                var table = c.Table?.Name is { Length: > 0 } n ? $"Стол {n}" : "Стол";
                var label = string.IsNullOrEmpty(c.Label) ? "" : $" · {c.Label}";
                var body = string.Join(", ", lines);
                if (body.Length > 180) body = body[..177] + "…";
                _push.Enqueue(new[] { c.WaiterId!.Value }, new PushMessage(
                    $"{table}{label}: {(allReady ? "подача готова" : "готово")}",
                    body,
                    $"/waiter/{c.Id}",
                    $"ready-{order.Id}-{c.Id}-{DateTime.UtcNow.Ticks}"));
            }
        }
    }
}
