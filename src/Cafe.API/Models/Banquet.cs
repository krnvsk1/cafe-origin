using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cafe.API.Models
{
    public static class BanquetStatuses
    {
        public const string Open = "open";
        public const string Closed = "closed";
        public const string Cancelled = "cancelled";
    }

    /// <summary>Скидка: процентом или суммой</summary>
    public static class DiscountTypes
    {
        public const string Percent = "percent";
        public const string Amount = "amount";

        /// <summary>Сумма скидки от суммы (не больше самой суммы), до копейки</summary>
        public static decimal Of(string? type, decimal? value, decimal sum)
        {
            if (value is not > 0 || sum <= 0) return 0;
            var d = type == Percent ? sum * Math.Min(value.Value, 100) / 100 : type == Amount ? value.Value : 0;
            return Math.Round(Math.Min(d, sum), 2, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>
    /// Банкет: заказ на дату для компании. Блюда на кухню не уходят, пока не наступит «отправить на кухню»
    /// (или не нажмут «Отправить сейчас»). Обслуживание — суммой или процентом, скидка, предоплаты.
    /// </summary>
    [Table("banquets")]
    public class Banquet
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(120)]
        [Column("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [MaxLength(30)]
        [Column("phone")]
        public string? Phone { get; set; }

        /// <summary>На когда заказан банкет (UTC)</summary>
        [Column("event_at")]
        public DateTime EventAt { get; set; }

        [Column("guests")]
        public int? Guests { get; set; }

        [Column("table_id")]
        public int? TableId { get; set; }

        [MaxLength(1000)]
        [Column("comment")]
        public string? Comment { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = BanquetStatuses.Open;

        /// <summary>Когда отправить блюда на кухню (UTC); пусто — не отправлять</summary>
        [Column("kitchen_at")]
        public DateTime? KitchenAt { get; set; }

        /// <summary>Обслуживание: percent — процент от суммы блюд (после скидки), amount — сумма; пусто — без обслуживания</summary>
        [MaxLength(20)]
        [Column("service_type")]
        public string? ServiceType { get; set; }

        [Column("service_value", TypeName = "numeric(12,2)")]
        public decimal? ServiceValue { get; set; }

        [MaxLength(20)]
        [Column("discount_type")]
        public string? DiscountType { get; set; }

        [Column("discount_value", TypeName = "numeric(12,2)")]
        public decimal? DiscountValue { get; set; }

        [Column("discount_by")]
        public int? DiscountBy { get; set; }

        /// <summary>Итог при закрытии</summary>
        [Column("total", TypeName = "numeric(12,2)")]
        public decimal Total { get; set; }

        [Column("created_by")]
        public int? CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("closed_at")]
        public DateTime? ClosedAt { get; set; }

        [Column("closed_by")]
        public int? ClosedBy { get; set; }
    }

    /// <summary>Блюдо банкета; OrderId — заказ кухни, с которым блюдо ушло на кухню (пусто — ещё не отправлено)</summary>
    [Table("banquet_items")]
    public class BanquetItem
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("banquet_id")]
        public int BanquetId { get; set; }

        [Column("dish_id")]
        public int DishId { get; set; }

        [Required]
        [MaxLength(200)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Цена за единицу с добавками</summary>
        [Column("price", TypeName = "numeric(12,2)")]
        public decimal Price { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("weight")]
        public int? Weight { get; set; }

        [MaxLength(300)]
        [Column("comment")]
        public string? Comment { get; set; }

        /// <summary>Добавки JSON: [{addonId, name, price, quantity}]</summary>
        [Column("addons", TypeName = "text")]
        public string? Addons { get; set; }

        [Column("order_id")]
        public int? OrderId { get; set; }
    }

    public static class BanquetPaymentKinds
    {
        /// <summary>Предоплата (аванс) до банкета — свой чек «аванс»</summary>
        public const string Prepay = "prepay";
        /// <summary>Расчёт при закрытии</summary>
        public const string Final = "final";
    }

    [Table("banquet_payments")]
    public class BanquetPayment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("banquet_id")]
        public int BanquetId { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("kind")]
        public string Kind { get; set; } = BanquetPaymentKinds.Prepay;

        /// <summary>cash или card</summary>
        [Required]
        [MaxLength(20)]
        [Column("method")]
        public string Method { get; set; } = "cash";

        [Column("amount", TypeName = "numeric(12,2)")]
        public decimal Amount { get; set; }

        /// <summary>Исторические данные: оплата «на сотрудника» убрана, новые записи не создаются</summary>
        [Column("staff_user_id")]
        public int? StaffUserId { get; set; }

        [MaxLength(60)]
        [Column("staff_reason")]
        public string? StaffReason { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("created_by")]
        public int? CreatedBy { get; set; }

        /// <summary>Предоплату вернули (банкет отменён): чек возврата пробит</summary>
        [Column("refunded_at")]
        public DateTime? RefundedAt { get; set; }
    }
}
