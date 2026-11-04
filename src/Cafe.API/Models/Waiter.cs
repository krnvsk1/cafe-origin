using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cafe.API.Models
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

        /// <summary>Смена заведения, в которую открыт счёт</summary>
        [Column("venue_shift_id")]
        public int? VenueShiftId { get; set; }

        /// <summary>Смена официанта, в которую открыт счёт</summary>
        [Column("waiter_shift_id")]
        public int? WaiterShiftId { get; set; }

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

        /// <summary>cash, card или staff (закрыт на сотрудника — без оплаты и без чека ККТ) — при закрытии</summary>
        [MaxLength(20)]
        [Column("payment_method")]
        public string? PaymentMethod { get; set; }

        /// <summary>Сотрудник, на которого закрыт счёт (питание персонала и т.п.) — сумма к удержанию с него</summary>
        [Column("staff_user_id")]
        public int? StaffUserId { get; set; }

        /// <summary>Причина закрытия на сотрудника: «Питание», «Домой», «Угощение», своя</summary>
        [MaxLength(60)]
        [Column("staff_reason")]
        public string? StaffReason { get; set; }

        /// <summary>Способ оплаты меняли после закрытия: какой был, кто и когда поменял</summary>
        [MaxLength(20)]
        [Column("payment_was")]
        public string? PaymentWas { get; set; }

        [Column("payment_changed_by")]
        public int? PaymentChangedBy { get; set; }

        [Column("payment_changed_at")]
        public DateTime? PaymentChangedAt { get; set; }

        /// <summary>Менеджер отменил весь счёт: часть блюд ждёт решения кухни — счёт отменится, когда кухня ответит</summary>
        [Column("cancel_requested_at")]
        public DateTime? CancelRequestedAt { get; set; }

        [Column("cancel_requested_by")]
        public int? CancelRequestedBy { get; set; }

        [Column("total", TypeName = "numeric(12,2)")]
        public decimal Total { get; set; }

        [MaxLength(500)]
        [Column("comment")]
        public string? Comment { get; set; }

        /// <summary>Когда последний раз печатали счёт для гостя (пречек)</summary>
        [Column("printed_at")]
        public DateTime? PrintedAt { get; set; }

        /// <summary>Сумма в напечатанном счёте: дозаказали после печати — счёт надо печатать заново</summary>
        [Column("printed_total", TypeName = "numeric(12,2)")]
        public decimal? PrintedTotal { get; set; }
    }

    public static class CheckPayments
    {
        public const string Cash = "cash";
        public const string Card = "card";
        /// <summary>На сотрудника: денег не принимают, чек ККТ не нужен — сумма удерживается с сотрудника</summary>
        public const string Staff = "staff";
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

        [Column("dish_id")]
        public int DishId { get; set; }

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
