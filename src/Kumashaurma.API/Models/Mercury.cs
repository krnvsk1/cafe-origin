using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kumashaurma.API.Models
{
    public static class MercuryStatuses
    {
        /// <summary>Входящий ВСД получен, ещё не погашен</summary>
        public const string New = "new";
        /// <summary>Заявка на гашение отправлена, ждём результат</summary>
        public const string Processing = "processing";
        /// <summary>Погашен (партия принята на площадку)</summary>
        public const string Accepted = "accepted";
        /// <summary>Меркурий отклонил гашение</summary>
        public const string Error = "error";
        /// <summary>Скрыт пользователем</summary>
        public const string Ignored = "ignored";
        /// <summary>Погашен в другой программе (в Меркурии статус уже не «оформлен»)</summary>
        public const string External = "external";
    }

    /// <summary>Входящий ветеринарный сопроводительный документ (ВСД) из ФГИС «Меркурий»</summary>
    [Table("mercury_documents")]
    public class MercuryDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>UUID ВСД в Меркурии</summary>
        [Required]
        [MaxLength(40)]
        [Column("uuid")]
        public string Uuid { get; set; } = string.Empty;

        [Column("issue_date")]
        public DateTime? IssueDate { get; set; }

        /// <summary>Статус ВСД в Меркурии: CONFIRMED, UTILIZED, WITHDRAWN…</summary>
        [MaxLength(20)]
        [Column("vet_status")]
        public string? VetStatus { get; set; }

        [MaxLength(500)]
        [Column("product_name")]
        public string? ProductName { get; set; }

        [Column("volume", TypeName = "decimal(14,3)")]
        public decimal Volume { get; set; }

        [MaxLength(50)]
        [Column("unit_name")]
        public string? UnitName { get; set; }

        [MaxLength(30)]
        [Column("production_date")]
        public string? ProductionDate { get; set; }

        [MaxLength(30)]
        [Column("expiry_date")]
        public string? ExpiryDate { get; set; }

        [MaxLength(300)]
        [Column("consignor_name")]
        public string? ConsignorName { get; set; }

        /// <summary>ТТН / УПД из ВСД: «№ 123 от 01.10.2026»</summary>
        [MaxLength(200)]
        [Column("waybill")]
        public string? Waybill { get; set; }

        /// <summary>vetDocument как пришёл из Меркурия — из него собирается заявка на гашение</summary>
        [Required]
        [Column("xml", TypeName = "text")]
        public string Xml { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = MercuryStatuses.New;

        /// <summary>Заявка на гашение (applicationId)</summary>
        [MaxLength(64)]
        [Column("application_id")]
        public string? ApplicationId { get; set; }

        [MaxLength(2000)]
        [Column("error")]
        public string? Error { get; set; }

        /// <summary>Запись складского журнала Меркурия, созданная при гашении</summary>
        [MaxLength(64)]
        [Column("stock_entry_guid")]
        public string? StockEntryGuid { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("processed_at")]
        public DateTime? ProcessedAt { get; set; }

        [Column("processed_by_user_id")]
        public int? ProcessedByUserId { get; set; }
    }
}
