using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kumashaurma.API.Models
{
    public static class EdoStatuses
    {
        /// <summary>Получен из СБИС, ещё не оприходован</summary>
        public const string New = "new";
        /// <summary>По накладной создан приход</summary>
        public const string Imported = "imported";
        /// <summary>Скрыт пользователем (не товар для склада, дубль и т.п.)</summary>
        public const string Ignored = "ignored";
        /// <summary>Не удалось скачать или разобрать файл</summary>
        public const string Error = "error";
    }

    /// <summary>Входящая электронная накладная (УПД) из СБИС</summary>
    [Table("edo_documents")]
    public class EdoDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>Идентификатор документа в СБИС</summary>
        [Required]
        [MaxLength(64)]
        [Column("sbis_id")]
        public string SbisId { get; set; } = string.Empty;

        [MaxLength(100)]
        [Column("number")]
        public string? Number { get; set; }

        [Column("date")]
        public DateTime? Date { get; set; }

        [MaxLength(12)]
        [Column("supplier_inn")]
        public string? SupplierInn { get; set; }

        [MaxLength(9)]
        [Column("supplier_kpp")]
        public string? SupplierKpp { get; set; }

        [MaxLength(300)]
        [Column("supplier_name")]
        public string? SupplierName { get; set; }

        /// <summary>Сумма с НДС</summary>
        [Column("total", TypeName = "decimal(14,2)")]
        public decimal Total { get; set; }

        [Column("lines_count")]
        public int LinesCount { get; set; }

        /// <summary>Сколько кодов маркировки в накладной</summary>
        [Column("codes_count")]
        public int CodesCount { get; set; }

        [MaxLength(200)]
        [Column("file_name")]
        public string? FileName { get; set; }

        /// <summary>XML УПД как есть (формат ФНС)</summary>
        [Column("xml", TypeName = "text")]
        public string? Xml { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = EdoStatuses.New;

        [MaxLength(2000)]
        [Column("error")]
        public string? Error { get; set; }

        [Column("stock_document_id")]
        public int? StockDocumentId { get; set; }

        /// <summary>Когда документ поступил в СБИС</summary>
        [Column("received_at")]
        public DateTime? ReceivedAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("imported_at")]
        public DateTime? ImportedAt { get; set; }

        [Column("imported_by_user_id")]
        public int? ImportedByUserId { get; set; }
    }

    /// <summary>
    /// Запомненное сопоставление товара поставщика с позицией склада:
    /// в следующих накладных этого поставщика строка подставится сама.
    /// </summary>
    [Table("edo_item_mappings")]
    public class EdoItemMapping
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(12)]
        [Column("supplier_inn")]
        public string SupplierInn { get; set; } = string.Empty;

        /// <summary>Ключ товара: gtin:…, code:… (артикул поставщика) или name:… (название в нижнем регистре)</summary>
        [Required]
        [MaxLength(300)]
        [Column("item_key")]
        public string ItemKey { get; set; } = string.Empty;

        [MaxLength(500)]
        [Column("item_name")]
        public string? ItemName { get; set; }

        [Column("stock_item_id")]
        public int StockItemId { get; set; }

        [ForeignKey(nameof(StockItemId))]
        public StockItem? StockItem { get; set; }

        /// <summary>Сколько единиц склада в одной единице накладной (коробка 5 кг → 5)</summary>
        [Column("factor", TypeName = "decimal(14,6)")]
        public decimal Factor { get; set; } = 1;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
