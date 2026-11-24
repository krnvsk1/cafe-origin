using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Cafe.API.Models
{
    [Table("orders")]
    public class Order
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("user_id")]
        public int? UserId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [MaxLength(20)]
        [Column("phone")]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(500)]
        [Column("address")]
        public string Address { get; set; } = string.Empty;

        [Required]
        [Column("total", TypeName = "decimal(10,2)")]
        public decimal Total { get; set; }

        [MaxLength(50)]
        [Column("status")]
        public string Status { get; set; } = "Новый";

        [Column("notes")]
        public string? Notes { get; set; }

        [Column("delivery_type")]
        [MaxLength(30)]
        public string DeliveryType { get; set; } = "Доставка";

        // Промокод
        [Column("promo_code_id")]
        public int? PromoCodeId { get; set; }

        [Column("discount_amount", TypeName = "decimal(10,2)")]
        public decimal DiscountAmount { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("completed_at")]
        public DateTime? CompletedAt { get; set; }

        // Секретный токен для отслеживания заказа без входа (гостевые заказы).
        // Не отдаётся в обычных ответах API — только при создании заказа.
        [MaxLength(64)]
        [Column("access_token")]
        [JsonIgnore]
        public string? AccessToken { get; set; }

        // Заказ ко времени (UTC). null — «как можно скорее»
        [Column("scheduled_for")]
        public DateTime? ScheduledFor { get; set; }

        // Оплата: cash — наличными, card — картой при получении, online — онлайн через ЮKassa
        [MaxLength(20)]
        [Column("payment_method")]
        public string PaymentMethod { get; set; } = "cash";

        // unpaid | pending | paid | canceled | refunded
        [MaxLength(20)]
        [Column("payment_status")]
        public string PaymentStatus { get; set; } = "unpaid";

        // ID платежа в ЮKassa
        [MaxLength(64)]
        [Column("payment_id")]
        [JsonIgnore]
        public string? PaymentId { get; set; }

        [Column("paid_at")]
        public DateTime? PaidAt { get; set; }

        // Онлайн-заказ виден кухне/курьерам только после оплаты
        [NotMapped]
        public bool IsVisibleToStaff => PaymentMethod != "online" || PaymentStatus == "paid";

        // Доставка: зона, координаты адреса и стоимость доставки (входит в Total)
        [Column("delivery_zone_id")]
        public int? DeliveryZoneId { get; set; }

        [Column("latitude")]
        public double? Latitude { get; set; }

        [Column("longitude")]
        public double? Longitude { get; set; }

        [Column("delivery_price", TypeName = "decimal(10,2)")]
        public decimal DeliveryPrice { get; set; }

        // Курьер, взявший заказ
        [Column("courier_id")]
        public int? CourierId { get; set; }

        // Когда курьер забрал заказ (статус «В пути»)
        [Column("picked_up_at")]
        public DateTime? PickedUpAt { get; set; }

        // Откуда заказ: online — сайт/приложение, pos — касса в заведении
        [MaxLength(20)]
        [Column("source")]
        public string Source { get; set; } = "online";

        // Сотрудник, принявший заказ на кассе
        [Column("cashier_id")]
        public int? CashierId { get; set; }

        /// <summary>Чек стола, к которому относится заказ официанта (оплата — при закрытии чека)</summary>
        [Column("check_id")]
        public int? CheckId { get; set; }

        /// <summary>Заказ кухни банкета (блюда банкета, отправленные на кухню)</summary>
        [Column("banquet_id")]
        public int? BanquetId { get; set; }

        /// <summary>Скидка, которую дал сотрудник (входит в DiscountAmount): кто дал</summary>
        [Column("manual_discount", TypeName = "numeric(12,2)")]
        public decimal ManualDiscount { get; set; }

        [Column("manual_discount_by")]
        public int? ManualDiscountBy { get; set; }

        /// <summary>Заказ с сайта закрыт менеджером: выбран способ оплаты и пробит чек</summary>
        [Column("closed_at")]
        public DateTime? ClosedAt { get; set; }

        [Column("closed_by")]
        public int? ClosedBy { get; set; }

        /// <summary>Исторические данные: закрытие заказа на сотрудника убрано, новые записи не создаются</summary>
        [Column("staff_user_id")]
        public int? StaffUserId { get; set; }

        [MaxLength(60)]
        [Column("staff_reason")]
        public string? StaffReason { get; set; }

        // Когда продукты списаны со склада по техкартам (null — не списаны или возвращены)
        [Column("stock_deducted_at")]
        [JsonIgnore]
        public DateTime? StockDeductedAt { get; set; }

        // Navigation properties
        [JsonIgnore]
        public AppUser? Courier { get; set; }

        [JsonIgnore]
        public AppUser? User { get; set; }
        [JsonIgnore]
        public PromoCode? PromoCode { get; set; }
        // Состав заказа отдаётся клиенту (список заказов, дашборд, повтор заказа); обратная ссылка OrderItem.Order скрыта
        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }
}
