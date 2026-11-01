namespace Cafe.API.Monitoring
{
    /// <summary>Настройки мониторинга (раздел Monitoring в appsettings или переменные окружения Monitoring__...)</summary>
    public class MonitoringOptions
    {
        /// <summary>Telegram-чат для тревог (ошибки сервера, база недоступна, сбои синхронизаций). Пусто — тревоги не шлём</summary>
        public long? TelegramChatId { get; set; }

        /// <summary>Одинаковую ошибку присылаем не чаще, чем раз в столько минут</summary>
        public int AlertThrottleMinutes { get; set; } = 15;

        /// <summary>Токен для /metrics (заголовок Authorization: Bearer ...). Пусто — /metrics доступен только с этого же компьютера</summary>
        public string? MetricsToken { get; set; }

        /// <summary>Папка логов; файлы по дням, хранятся RetainDays дней</summary>
        public string LogDirectory { get; set; } = "logs";
        public int RetainDays { get; set; } = 14;

        /// <summary>Свободного места на диске меньше — состояние «есть проблемы»</summary>
        public int MinFreeDiskMb { get; set; } = 1024;
    }
}
