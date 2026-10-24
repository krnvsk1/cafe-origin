namespace Kumashaurma.API.Options
{
    /// <summary>
    /// Настройки заведения (секция "Restaurant" в appsettings).
    /// </summary>
    public class RestaurantOptions
    {
        public const string SectionName = "Restaurant";

        /// <summary>Часовой пояс заведения (IANA), например Europe/Moscow</summary>
        public string TimeZone { get; set; } = "Europe/Moscow";

        /// <summary>Время открытия (местное), формат HH:mm</summary>
        public string OpenTime { get; set; } = "10:00";

        /// <summary>Время закрытия (местное), формат HH:mm</summary>
        public string CloseTime { get; set; } = "23:00";

        /// <summary>Минимум минут от текущего момента до заказа ко времени</summary>
        public int MinLeadMinutes { get; set; } = 45;

        /// <summary>На сколько дней вперёд можно оформить заказ</summary>
        public int MaxDaysAhead { get; set; } = 7;

        public TimeZoneInfo GetTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZone); }
            catch { return TimeZoneInfo.Utc; }
        }

        public TimeSpan GetOpen() => TimeSpan.TryParse(OpenTime, out var t) ? t : new TimeSpan(10, 0, 0);
        public TimeSpan GetClose() => TimeSpan.TryParse(CloseTime, out var t) ? t : new TimeSpan(23, 0, 0);

        /// <summary>Попадает ли момент (UTC) в часы работы заведения</summary>
        public bool IsWithinWorkingHours(DateTime utc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), GetTimeZone());
            var time = local.TimeOfDay;
            var open = GetOpen();
            var close = GetClose();
            // Поддержка работы через полночь (например 10:00–02:00)
            return open <= close
                ? time >= open && time <= close
                : time >= open || time <= close;
        }
    }
}
