using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Cafe.API.Models
{
    public static class EgaisStatuses
    {
        /// <summary>Накладная пришла, ждём справку с регистрационным номером (FORM2REGINFO)</summary>
        public const string AwaitingRegId = "awaiting_regid";
        /// <summary>Можно принимать</summary>
        public const string Ready = "ready";
        /// <summary>Акт отправлен в УТМ, ждём квитанцию</summary>
        public const string ActSent = "act_sent";
        public const string Accepted = "accepted";
        public const string Rejected = "rejected";
        public const string Differences = "differences";
        /// <summary>ЕГАИС вернул ошибку на акт</summary>
        public const string Error = "error";
    }

    /// <summary>Входящая накладная (ТТН) из ЕГАИС</summary>
    [Table("egais_documents")]
    public class EgaisDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [MaxLength(30)]
        [Column("doc_type")]
        public string DocType { get; set; } = "WayBill_v4";

        [MaxLength(64)]
        [Column("identity")]
        public string? Identity { get; set; }

        [MaxLength(50)]
        [Column("number")]
        public string Number { get; set; } = string.Empty;

        [Column("date")]
        public DateTime? Date { get; set; }

        [MaxLength(20)]
        [Column("shipper_fsrar_id")]
        public string? ShipperFsrarId { get; set; }

        [MaxLength(12)]
        [Column("shipper_inn")]
        public string? ShipperInn { get; set; }

        [MaxLength(255)]
        [Column("shipper_name")]
        public string? ShipperName { get; set; }

        /// <summary>Регистрационный номер ТТН в ЕГАИС — нужен для акта</summary>
        [MaxLength(50)]
        [Column("wb_reg_id")]
        public string? WbRegId { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = EgaisStatuses.AwaitingRegId;

        [MaxLength(30)]
        [Column("act_number")]
        public string? ActNumber { get; set; }

        /// <summary>Решение по акту: Accepted, Rejected, Differences</summary>
        [MaxLength(20)]
        [Column("act_decision")]
        public string? ActDecision { get; set; }

        [MaxLength(500)]
        [Column("act_note")]
        public string? ActNote { get; set; }

        [Column("act_sent_at")]
        public DateTime? ActSentAt { get; set; }

        /// <summary>Ответ ЕГАИС на акт (текст квитанции)</summary>
        [MaxLength(1000)]
        [Column("result_comment")]
        public string? ResultComment { get; set; }

        [Column("result_at")]
        public DateTime? ResultAt { get; set; }

        /// <summary>Приход на склад, созданный при приёмке</summary>
        [Column("stock_document_id")]
        public int? StockDocumentId { get; set; }

        [JsonIgnore]
        [Column("raw_xml", TypeName = "text")]
        public string? RawXml { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<EgaisDocumentLine> Lines { get; set; } = new List<EgaisDocumentLine>();
    }

    [Table("egais_document_lines")]
    public class EgaisDocumentLine
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("document_id")]
        [JsonIgnore]
        public int DocumentId { get; set; }

        [ForeignKey(nameof(DocumentId))]
        [JsonIgnore]
        public EgaisDocument? Document { get; set; }

        /// <summary>Номер позиции в накладной (Identity)</summary>
        [MaxLength(30)]
        [Column("identity")]
        public string Identity { get; set; } = string.Empty;

        [MaxLength(255)]
        [Column("product_name")]
        public string ProductName { get; set; } = string.Empty;

        /// <summary>Код продукции в ЕГАИС</summary>
        [MaxLength(64)]
        [Column("alc_code")]
        public string? AlcCode { get; set; }

        /// <summary>Объём тары, л (у разливного — пусто)</summary>
        [Column("capacity", TypeName = "decimal(10,4)")]
        public decimal? Capacity { get; set; }

        [Column("alc_volume", TypeName = "decimal(6,3)")]
        public decimal? AlcVolume { get; set; }

        /// <summary>Packed — фасованная (штуки), Unpacked — нефасованная (декалитры)</summary>
        [MaxLength(20)]
        [Column("unit_type")]
        public string? UnitType { get; set; }

        [Column("quantity", TypeName = "decimal(14,4)")]
        public decimal Quantity { get; set; }

        [Column("price", TypeName = "decimal(14,4)")]
        public decimal Price { get; set; }

        /// <summary>Справка Б (форма 2) — регистрационный номер для акта</summary>
        [MaxLength(50)]
        [Column("inform_f2_reg_id")]
        public string? InformF2RegId { get; set; }

        /// <summary>Фактически принятое количество (в единицах ЕГАИС)</summary>
        [Column("real_quantity", TypeName = "decimal(14,4)")]
        public decimal? RealQuantity { get; set; }

        /// <summary>Позиция склада, в которую идёт приход</summary>
        [Column("stock_item_id")]
        public int? StockItemId { get; set; }

        [ForeignKey(nameof(StockItemId))]
        public StockItem? StockItem { get; set; }
    }
}
