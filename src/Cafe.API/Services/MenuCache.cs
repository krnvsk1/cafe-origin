using System.Threading;

namespace Cafe.API.Services
{
    /// <summary>
    /// Меню (GET /api/dishes) — самый частый и самый тяжёлый запрос: держим готовый ответ до минуты.
    /// Любое сохранение блюд, фото, добавок или их групп сбрасывает кэш (см. ApplicationDbContext.SaveChanges),
    /// так что правки в админке и стоп-лист видны сразу.
    /// </summary>
    public static class MenuCache
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);
        private static object? _value;
        private static DateTime _until;
        private static int _version;

        public static int Version => Volatile.Read(ref _version);

        public static void Invalidate()
        {
            Interlocked.Increment(ref _version);
            Volatile.Write(ref _value, null);
        }

        public static T? Get<T>() where T : class =>
            DateTime.UtcNow < _until ? Volatile.Read(ref _value) as T : null;

        /// <summary>Положить, только если за время запроса меню никто не поменял</summary>
        public static void Set(object value, int versionAtStart)
        {
            if (Version != versionAtStart) return;
            _until = DateTime.UtcNow + Lifetime;
            Volatile.Write(ref _value, value);
        }
    }
}
