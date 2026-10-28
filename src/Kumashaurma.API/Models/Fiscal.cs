using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kumashaurma.API.Models
{
    public static class FiscalKinds
    {
        /// <summary>Приход</summary>
        public const string Sell = "sell";
        /// <summary>Возврат прихода</summary>
        public const string Return = "sellReturn";
    }

    public static class FiscalStatuses
    {
        /// <summary>Задание отправлено на кассу, результата ещё нет</summary>
        public const string Processing = "processing";
        /// <summary>Чек пробит и записан в фискальный накопитель</summary>
        public const string Done = "done";
        /// <summary>Касса отказала или недоступна — чек не пробит</summary>
        public const string Error = "error";
    }

    /// <summary>Чек, пробитый (или попытка пробить) на кассе АТОЛ по заказу</summary>
    [Table("fiscal_receipts")]
    public class FiscalReceipt
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("order_id")]
        public int OrderId { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("kind")]
        public string Kind { get; set; } = FiscalKinds.Sell;

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = FiscalStatuses.Processing;

        /// <summary>UUID задания в АТОЛ Web Server — по нему узнаём результат, не пробивая чек повторно</summary>
        [Required]
        [MaxLength(64)]
        [Column("request_uuid")]
        public string RequestUuid { get; set; } = string.Empty;

        [Column("total", TypeName = "numeric(12,2)")]
        public decimal Total { get; set; }

        /// <summary>cash или card</summary>
        [MaxLength(20)]
        [Column("payment_method")]
        public string? PaymentMethod { get; set; }

        /// <summary>Телефон или email, куда ОФД отправит копию чека</summary>
        [MaxLength(100)]
        [Column("client_contact")]
        public string? ClientContact { get; set; }

        [Column("fiscal_document_number")]
        public long? FiscalDocumentNumber { get; set; }

        [MaxLength(20)]
        [Column("fiscal_document_sign")]
        public string? FiscalDocumentSign { get; set; }

        [MaxLength(20)]
        [Column("fn_number")]
        public string? FnNumber { get; set; }

        [Column("shift_number")]
        public int? ShiftNumber { get; set; }

        [Column("receipt_number")]
        public int? ReceiptNumber { get; set; }

        [MaxLength(40)]
        [Column("fiscal_date_time")]
        public string? FiscalDateTime { get; set; }

        [MaxLength(2000)]
        [Column("error")]
        public string? Error { get; set; }

        /// <summary>Что отправили на кассу (JSON задания)</summary>
        [Column("request_json", TypeName = "text")]
        public string RequestJson { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("completed_at")]
        public DateTime? CompletedAt { get; set; }

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }
    }
}
