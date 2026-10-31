namespace Cafe.API.Options
{
    /// <summary>
    /// Настройки ЮKassa (секция "YooKassa"). Онлайн-оплата включена, если заданы ShopId и SecretKey.
    /// </summary>
    public class YooKassaOptions
    {
        public const string SectionName = "YooKassa";

        public string? ShopId { get; set; }
        public string? SecretKey { get; set; }

        /// <summary>Адрес сайта для возврата после оплаты, например https://kumashaurma-frontend.vercel.app</summary>
        public string? ReturnUrl { get; set; }

        /// <summary>Передавать чек (54-ФЗ) через ЮKassa</summary>
        public bool SendReceipt { get; set; } = false;

        /// <summary>Код ставки НДС для чека: 1 — без НДС (см. документацию ЮKassa)</summary>
        public int VatCode { get; set; } = 1;

        /// <summary>Через сколько минут неоплаченный онлайн-заказ отменяется</summary>
        public int PaymentTimeoutMinutes { get; set; } = 30;

        public bool Enabled => !string.IsNullOrWhiteSpace(ShopId) && !string.IsNullOrWhiteSpace(SecretKey);
    }
}
