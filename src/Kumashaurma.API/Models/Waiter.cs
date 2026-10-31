using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kumashaurma.API.Models
{
    /// <summary>Стол в зале</summary>
    [Table("dining_tables")]
    public class DiningTable
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>«1», «12», «Веранда 3»</summary>
        [Required]
        [MaxLength(40)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;
    }

    public static class CheckStatuses
    {
        public const string Open = "open";
        public const string Closed = "closed";
        public const string Cancelled = "cancelled";
    }

    /// <summary>
    /// Чек стола: официант открывает его, отправляет на кухню заказы (каждая отправка — отдельный заказ кухни)
    /// и закрывает при оплате.
    /// </summary>
    [Table("table_checks")]
    public class TableCheck
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("table_id")]
        public int TableId { get; set; }

        public DiningTable? Table { get; set; }

        [Column("waiter_id")]
        public int? WaiterId { get; set; }

        [Column("guests")]
        public int? Guests { get; set; }

        /// <summary>Подпись счёта, когда за столом их несколько: «Счёт 2», «Анна»</summary>
        [MaxLength(40)]
        [Column("label")]
        public string? Label { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = CheckStatuses.Open;

        [Column("opened_at")]
        public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

        [Column("closed_at")]
        public DateTime? ClosedAt { get; set; }

        /// <summary>cash или card — при закрытии</summary>
        [MaxLength(20)]
        [Column("payment_method")]
        public string? PaymentMethod { get; set; }

        [Column("total", TypeName = "numeric(12,2)")]
        public decimal Total { get; set; }

        [MaxLength(500)]
        [Column("comment")]
        public string? Comment { get; set; }
    }

    public static class VoidStatuses
    {
        /// <summary>Ждёт решения кухни (блюдо уже готовят)</summary>
        public const string Pending = "pending";
        public const string Done = "done";
        /// <summary>Кухня отказала (например, блюдо уже отдали)</summary>
        public const string Rejected = "rejected";
    }

    /// <summary>
    /// Блюдо, убранное официантом из чека. Пока кухня не начала готовить — убирается сразу;
    /// если готовят — кухня подтверждает со списанием продуктов или без. Журнал видят администратор и менеджер.
    /// </summary>
    [Table("order_item_voids")]
    public class OrderItemVoid
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("order_id")]
        public int OrderId { get; set; }

        /// <summary>Позиция заказа (после удаления строки — null, название и сумма остаются здесь)</summary>
        [Column("order_item_id")]
        public int? OrderItemId { get; set; }

        [Column("check_id")]
        public int? CheckId { get; set; }

        [Column("shawarma_id")]
        public int ShawarmaId { get; set; }

        [Required]
        [MaxLength(150)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("amount", TypeName = "numeric(12,2)")]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(300)]
        [Column("reason")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>Статус заказа на кухне в момент просьбы</summary>
        [MaxLength(50)]
        [Column("order_status")]
        public string? OrderStatus { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = VoidStatuses.Pending;

        /// <summary>Продукты списаны (блюдо уже было приготовлено или начато)</summary>
        [Column("write_off")]
        public bool? WriteOff { get; set; }

        [Column("requested_by_user_id")]
        public int? RequestedByUserId { get; set; }

        [Column("requested_at")]
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        [Column("resolved_by_user_id")]
        public int? ResolvedByUserId { get; set; }

        [Column("resolved_at")]
        public DateTime? ResolvedAt { get; set; }
    }
}
