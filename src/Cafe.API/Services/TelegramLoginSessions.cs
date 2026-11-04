using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Cafe.API.Services
{
    /// <summary>
    /// Вход на сайт через Telegram: сайт получает код, гость открывает t.me/бот?start=login_КОД и жмёт «Запустить»,
    /// бот подтверждает код от имени этого Telegram-пользователя, сайт забирает вход опросом. Код живёт 5 минут и срабатывает один раз.
    /// Хранится в памяти API (один экземпляр сервера); после перезапуска нужно просто начать вход заново.
    /// </summary>
    public class TelegramLoginSessions
    {
        public const string Prefix = "login_";
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        public record Session(DateTime ExpiresAt, TelegramAuth.TelegramUser? User);

        private readonly ConcurrentDictionary<string, Session> _sessions = new();

        public string Create()
        {
            Cleanup();
            // Символы, разрешённые в параметре /start: A-Z a-z 0-9 _ -
            var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            _sessions[code] = new Session(DateTime.UtcNow.Add(Lifetime), null);
            return code;
        }

        /// <summary>Бот получил /start login_КОД — запоминаем, кто подтвердил. false — код неизвестен или устарел</summary>
        public bool Approve(string code, TelegramAuth.TelegramUser user)
        {
            if (!_sessions.TryGetValue(code, out var s) || s.ExpiresAt < DateTime.UtcNow || s.User != null) return false;
            return _sessions.TryUpdate(code, s with { User = user }, s);
        }

        /// <summary>null — кода нет или он устарел; Session без User — ещё не подтвердили</summary>
        public Session? Get(string code)
        {
            if (!_sessions.TryGetValue(code, out var s)) return null;
            if (s.ExpiresAt < DateTime.UtcNow) { _sessions.TryRemove(code, out _); return null; }
            return s;
        }

        /// <summary>Забрать подтверждённый вход (один раз)</summary>
        public TelegramAuth.TelegramUser? Take(string code)
        {
            var s = Get(code);
            if (s?.User == null) return null;
            return _sessions.TryRemove(new KeyValuePair<string, Session>(code, s)) ? s.User : null;
        }

        private void Cleanup()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _sessions)
                if (kv.Value.ExpiresAt < now) _sessions.TryRemove(kv.Key, out _);
        }
    }
}
