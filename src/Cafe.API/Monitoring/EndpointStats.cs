using System.Collections.Concurrent;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cafe.API.Monitoring
{
    /// <summary>Статистика ручки за окно: запросы, ошибки, время ответа</summary>
    public record EndpointSummary(string Method, string Route, int Count, int Errors5xx, int Errors4xx,
        double ErrorRate, int P50Ms, int P95Ms, int MaxMs, DateTime? LastError, int? LastErrorStatus, string? LastErrorRequestId);

    /// <summary>
    /// Стабильность ручек API в памяти сервера: корзины по 5 минут за последние сутки
    /// (запросы, 4xx, 5xx, распределение времени ответа). Сбрасывается при перезапуске — для истории есть /metrics.
    /// </summary>
    public class EndpointStats
    {
        private static readonly int[] Bounds = { 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 30000 };
        private const int BucketMinutes = 5;
        private const int KeepBuckets = 24 * 60 / BucketMinutes + 1;

        private class Bucket
        {
            public int Count, E4, E5, Max;
            public readonly int[] Hist = new int[Bounds.Length + 1];
        }

        private class Endpoint
        {
            public readonly object Lock = new();
            public readonly Dictionary<long, Bucket> Buckets = new();
            public DateTime? LastError;
            public int? LastErrorStatus;
            public string? LastErrorRequestId;
        }

        private readonly ConcurrentDictionary<(string Method, string Route), Endpoint> _items = new();

        private static long BucketOf(DateTime utc) => utc.Ticks / TimeSpan.FromMinutes(BucketMinutes).Ticks;

        /// <summary>Ручки, которые не считаем: живые подключения, проверки, метрики, файлы</summary>
        public static bool Skip(PathString path) =>
            path.StartsWithSegments("/api/events") || path.StartsWithSegments("/health") || path.StartsWithSegments("/metrics")
            || path.StartsWithSegments("/uploads") || !path.StartsWithSegments("/api");

        public void Record(HttpContext ctx, double elapsedMs)
        {
            if (Skip(ctx.Request.Path)) return;
            // Шаблон маршрута, а не адрес: /api/orders/{id}, чтобы заказы 1 и 2 считались одной ручкой
            if (ctx.GetEndpoint() is not RouteEndpoint endpoint) return;
            var route = "/" + (endpoint.RoutePattern.RawText ?? ctx.Request.Path.Value ?? "").TrimStart('/');
            var key = (ctx.Request.Method, route);
            var item = _items.GetOrAdd(key, _ => new Endpoint());
            var status = ctx.Response.StatusCode;
            var ms = (int)Math.Min(int.MaxValue, Math.Max(0, elapsedMs));
            var now = DateTime.UtcNow;
            var b = BucketOf(now);
            lock (item.Lock)
            {
                if (!item.Buckets.TryGetValue(b, out var bucket))
                {
                    bucket = new Bucket();
                    item.Buckets[b] = bucket;
                    foreach (var old in item.Buckets.Keys.Where(k => k < b - KeepBuckets).ToList()) item.Buckets.Remove(old);
                }
                bucket.Count++;
                if (status >= 500) bucket.E5++;
                else if (status >= 400) bucket.E4++;
                bucket.Max = Math.Max(bucket.Max, ms);
                var i = Array.FindIndex(Bounds, x => ms <= x);
                bucket.Hist[i < 0 ? Bounds.Length : i]++;
                if (status >= 500)
                {
                    item.LastError = now;
                    item.LastErrorStatus = status;
                    item.LastErrorRequestId = ctx.TraceIdentifier;
                }
            }
        }

        public List<EndpointSummary> Summary(TimeSpan window)
        {
            var from = BucketOf(DateTime.UtcNow - window);
            var list = new List<EndpointSummary>();
            foreach (var ((method, route), item) in _items)
            {
                int count = 0, e4 = 0, e5 = 0, max = 0;
                var hist = new int[Bounds.Length + 1];
                lock (item.Lock)
                {
                    foreach (var (k, b) in item.Buckets)
                    {
                        if (k < from) continue;
                        count += b.Count; e4 += b.E4; e5 += b.E5; max = Math.Max(max, b.Max);
                        for (var i = 0; i < hist.Length; i++) hist[i] += b.Hist[i];
                    }
                    if (count == 0) continue;
                    list.Add(new EndpointSummary(method, route, count, e5, e4, Math.Round(100.0 * e5 / count, 1),
                        Percentile(hist, count, 0.5, max), Percentile(hist, count, 0.95, max), max,
                        item.LastError, item.LastErrorStatus, item.LastErrorRequestId));
                }
            }
            return list;
        }

        /// <summary>Перцентиль по корзинам времени: верхняя граница корзины, где он оказался (не больше максимума)</summary>
        private static int Percentile(int[] hist, int count, double p, int max)
        {
            var target = Math.Ceiling(count * p);
            var acc = 0;
            for (var i = 0; i < hist.Length; i++)
            {
                acc += hist[i];
                if (acc >= target) return Math.Min(i < Bounds.Length ? Bounds[i] : max, max);
            }
            return max;
        }
    }

    /// <summary>Ручки, где за 15 минут больше 5% ответов — ошибки сервера (при 20+ запросах)</summary>
    public class EndpointsHealthCheck : IHealthCheck
    {
        public const int MinRequests = 20;
        public const double MaxErrorRate = 5;
        private readonly EndpointStats _stats;
        public EndpointsHealthCheck(EndpointStats stats) => _stats = stats;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            var bad = _stats.Summary(TimeSpan.FromMinutes(15))
                .Where(e => e.Count >= MinRequests && e.ErrorRate > MaxErrorRate)
                .OrderByDescending(e => e.ErrorRate).ToList();
            return Task.FromResult(bad.Count == 0
                ? HealthCheckResult.Healthy("Ручки отвечают без сбоев")
                : HealthCheckResult.Degraded("Много ошибок за 15 мин: " +
                    string.Join("; ", bad.Take(5).Select(e => $"{e.Method} {e.Route} — {e.ErrorRate:0.#}% из {e.Count}"))));
        }
    }
}
