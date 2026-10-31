using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Cafe.API.Options;

namespace Cafe.API.Services
{
    /// <summary>
    /// Подписи для Telegram: токен привязки аккаунта (deep link /start) и проверка initData мини-приложения
    /// </summary>
    public class TelegramAuth
    {
        private static readonly TimeSpan LinkTokenLifetime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan InitDataLifetime = TimeSpan.FromHours(24);

        private readonly TelegramOptions _options;
        private readonly byte[] _linkSecret;

        public TelegramAuth(IOptions<TelegramOptions> options, IConfiguration configuration)
        {
            _options = options.Value;
            var secret = !string.IsNullOrWhiteSpace(_options.WebhookSecret)
                ? _options.WebhookSecret
                : configuration["JwtSettings:SecretKey"] ?? "cafe-telegram-link";
            _linkSecret = Encoding.UTF8.GetBytes("tg-link:" + secret);
        }

        // ---------- Токен привязки: {userId}_{expires}_{signature} (символы, разрешённые в /start) ----------

        public string CreateLinkToken(int userId)
        {
            var expires = DateTimeOffset.UtcNow.Add(LinkTokenLifetime).ToUnixTimeSeconds();
            return $"{userId}_{expires}_{Sign($"{userId}.{expires}")}";
        }

        public int? ValidateLinkToken(string token)
        {
            var parts = token.Split('_', 3);
            if (parts.Length != 3) return null;
            if (!int.TryParse(parts[0], out var userId) || !long.TryParse(parts[1], out var expires)) return null;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires) return null;

            var expected = Sign($"{userId}.{expires}");
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2]))
                ? userId
                : null;
        }

        private string Sign(string payload)
        {
            var hash = HMACSHA256.HashData(_linkSecret, Encoding.UTF8.GetBytes(payload));
            // base64url без паддинга, 22 символа достаточно
            return Convert.ToBase64String(hash, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        // ---------- initData мини-приложения (https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app) ----------

        public record TelegramUser(long Id, string? FirstName, string? LastName, string? Username);

        public TelegramUser? ValidateInitData(string initData)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(initData)) return null;

            var pairs = initData.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

            if (!pairs.TryGetValue("hash", out var hash)) return null;

            var dataCheckString = string.Join('\n', pairs
                .Where(p => p.Key != "hash")
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"));

            var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(_options.BotToken!));
            var computed = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLowerInvariant();

            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(hash.ToLowerInvariant())))
                return null;

            if (pairs.TryGetValue("auth_date", out var authDateRaw) && long.TryParse(authDateRaw, out var authDate))
            {
                if (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(authDate) > InitDataLifetime)
                    return null;
            }
            else return null;

            if (!pairs.TryGetValue("user", out var userJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(userJson);
                var root = doc.RootElement;
                return new TelegramUser(
                    root.GetProperty("id").GetInt64(),
                    root.TryGetProperty("first_name", out var fn) ? fn.GetString() : null,
                    root.TryGetProperty("last_name", out var ln) ? ln.GetString() : null,
                    root.TryGetProperty("username", out var un) ? un.GetString() : null);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
