namespace Cafe.API.Services.Finance
{
    /// <summary>Куда попадает платёж в «Финансах»</summary>
    public static class PaymentBuckets
    {
        public const string Overdue = "overdue";
        public const string ThisWeek = "week";
        public const string Later = "later";
        public const string NoDueDate = "nodue";
        public const string Paid = "paid";
    }

    /// <summary>Сроки оплаты накладных поставщиков</summary>
    public static class PaymentSchedule
    {
        // «На этой неделе» — срок в ближайшие столько дней, включая сегодня
        public const int SoonDays = 7;

        // Сроки — календарные даты по Москве
        private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

        public static DateTime TodayMoscow(DateTime utcNow) => (utcNow + MoscowOffset).Date;

        /// <summary>
        /// Срок оплаты: из самого УПД (явная дата или отсрочка в доп. сведениях),
        /// иначе дата накладной + отсрочка, заданная у поставщика; null — срок не известен.
        /// </summary>
        public static DateTime? DueDate(DateTime? updDueDate, DateTime? documentDate, int? supplierDelayDays) =>
            updDueDate?.Date
            ?? (documentDate.HasValue && supplierDelayDays.HasValue ? documentDate.Value.Date.AddDays(supplierDelayDays.Value) : null);

        public static string Bucket(DateTime? dueDate, bool paid, DateTime today)
        {
            if (paid) return PaymentBuckets.Paid;
            if (dueDate == null) return PaymentBuckets.NoDueDate;
            if (dueDate.Value.Date < today) return PaymentBuckets.Overdue;
            return dueDate.Value.Date <= today.AddDays(SoonDays) ? PaymentBuckets.ThisWeek : PaymentBuckets.Later;
        }

        /// <summary>Сколько дней до срока (отрицательное — просрочено на столько дней)</summary>
        public static int? DaysLeft(DateTime? dueDate, DateTime today) =>
            dueDate.HasValue ? (int)(dueDate.Value.Date - today).TotalDays : null;
    }
}
