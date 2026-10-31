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
}
