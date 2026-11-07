namespace Cafe.API.Options
{
    /// <summary>
    /// SMS-коды подтверждения телефона (секция "Sms").
    /// Provider: "smsaero" (SMS Aero: Login — email, ApiKey) или "smsru" (SMS.ru: ApiKey = api_id).
    /// Не задан — SMS не отправляются, и подтверждение телефона перед доставкой не требуется.
    /// </summary>
    public class SmsOptions
    {
        public const string SectionName = "Sms";

        public string? Provider { get; set; }
        /// <summary>SMS Aero: email аккаунта</summary>
        public string? Login { get; set; }
        public string? ApiKey { get; set; }
        /// <summary>Имя отправителя (подпись), если зарегистрировано у сервиса</summary>
        public string? Sender { get; set; }

        public string ProviderKey => (Provider ?? "").Trim().ToLowerInvariant();

        public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey) && ProviderKey switch
        {
            "smsaero" => !string.IsNullOrWhiteSpace(Login),
            "smsru" => true,
            _ => false,
        };
    }
}
