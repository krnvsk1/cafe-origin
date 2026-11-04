namespace Cafe.API.Monitoring
{
    /// <summary>Настройки мониторинга (раздел Monitoring в appsettings или переменные окружения Monitoring__...)</summary>
    public class MonitoringOptions
    {
        /// <summary>
        /// Адрес пинга внешнего мониторинга по умолчанию (Healthchecks.io и т.п.). Обычно задаётся в «Состоянии системы»;
        /// это значение — запасное. Пусто — тревоги никуда не уходят
        /// </summary>
        public string? PingUrl { get; set; }

        /// <summary>Одинаковую ошибку отправляем не чаще, чем раз в столько минут</summary>
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
