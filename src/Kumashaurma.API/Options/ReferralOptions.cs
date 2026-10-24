namespace Kumashaurma.API.Options
{
    /// <summary>
    /// Реферальная программа (секция "Referral"): бонусы начисляются после первого полученного заказа друга
    /// </summary>
    public class ReferralOptions
    {
        public const string SectionName = "Referral";

        public bool Enabled { get; set; } = true;

        /// <summary>Баллы пригласившему</summary>
        public int InviterBonus { get; set; } = 200;

        /// <summary>Баллы приглашённому другу</summary>
        public int InviteeBonus { get; set; } = 150;

        /// <summary>Минимальная сумма первого заказа друга для начисления</summary>
        public decimal MinOrderAmount { get; set; } = 500;
    }
}
