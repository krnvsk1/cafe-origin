using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kumashaurma.API.Models
{
    public static class ChzStatuses
    {
        /// <summary>Документ собран, ждёт подписи в браузере</summary>
        public const string Draft = "draft";
        /// <summary>Отправлен, Честный знак проверяет</summary>
        public const string Sent = "sent";
        public const string Ok = "ok";
        public const string Error = "error";
    }

    /// <summary>Документ вывода из оборота в Честном знаке (LK_RECEIPT)</summary>
    [Table("chz_documents")]
    public class ChzDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>Товарная группа (milk, water, beer…)</summary>
        [Required]
        [MaxLength(30)]
        [Column("product_group")]
        public string ProductGroup { get; set; } = "milk";

        /// <summary>Причина вывода: PRODUCTION_USE, OWN_USE, EXPIRATION…</summary>
        [Required]
        [MaxLength(30)]
        [Column("action")]
        public string Action { get; set; } = "PRODUCTION_USE";

        [Column("action_date")]
        public DateTime ActionDate { get; set; } = DateTime.UtcNow;

        [MaxLength(50)]
        [Column("document_number")]
        public string DocumentNumber { get; set; } = string.Empty;

        /// <summary>Коды маркировки (без криптохвоста), по одному в строке</summary>
        [Column("codes", TypeName = "text")]
        public string Codes { get; set; } = string.Empty;

        [Column("codes_count")]
        public int CodesCount { get; set; }

        /// <summary>JSON документа в base64 — именно его подписывают в браузере</summary>
        [Column("product_document", TypeName = "text")]
        public string ProductDocument { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = ChzStatuses.Draft;

        /// <summary>Идентификатор документа в Честном знаке</summary>
        [MaxLength(64)]
        [Column("chz_document_id")]
        public string? ChzDocumentId { get; set; }

        [MaxLength(2000)]
        [Column("result")]
        public string? Result { get; set; }

        [MaxLength(300)]
        [Column("comment")]
        public string? Comment { get; set; }

        /// <summary>Связанное списание на складе (если вывод сделан из него)</summary>
        [Column("stock_document_id")]
        public int? StockDocumentId { get; set; }

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("sent_at")]
        public DateTime? SentAt { get; set; }

        [Column("checked_at")]
        public DateTime? CheckedAt { get; set; }
    }
}
