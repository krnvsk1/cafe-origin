namespace Cafe.API.Options
{
    /// <summary>
    /// Настройки Telegram-бота (секция "Telegram").
    /// Если BotToken не задан — все функции Telegram выключены.
    /// </summary>
    public class TelegramOptions
    {
        public const string SectionName = "Telegram";

        /// <summary>Токен бота от @BotFather</summary>
        public string? BotToken { get; set; }

        /// <summary>Имя бота без @ (для ссылок t.me/...)</summary>
        public string? BotUsername { get; set; }

        /// <summary>Секрет вебхука (заголовок X-Telegram-Bot-Api-Secret-Token)</summary>
        public string? WebhookSecret { get; set; }

        /// <summary>Публичный адрес API, например https://api.cafe.example (для установки вебхука)</summary>
        public string? PublicApiUrl { get; set; }

        /// <summary>Адрес сайта для мини-приложения, например https://example.ru</summary>
        public string? WebAppUrl { get; set; }

        /// <summary>Чат персонала для уведомлений о новых заказах (ID группы или пользователя)</summary>
        public long? StaffChatId { get; set; }

        public bool Enabled => !string.IsNullOrWhiteSpace(BotToken);
    }
}
