using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Kumashaurma.API.Models
{
    /// <summary>Единицы измерения складских позиций</summary>
    public static class StockUnits
    {
        public const string Kg = "kg";
        public const string Liter = "l";
        public const string Piece = "pcs";
        public static readonly string[] All = { Kg, Liter, Piece };
    }

    /// <summary>Сырьё (закупается) или полуфабрикат (готовится по своей техкарте, на складе не хранится)</summary>
    public static class StockItemKinds
    {
        public const string Raw = "raw";
        public const string Semi = "semi";
    }

    public static class StockDocumentTypes
    {
        public const string Receipt = "receipt";       // приход по накладной
        public const string WriteOff = "writeoff";     // списание
        public const string Inventory = "inventory";   // инвентаризация
        public static readonly string[] All = { Receipt, WriteOff, Inventory };
    }

    public static class StockDocumentStatuses
    {
        public const string Draft = "draft";
        public const string Posted = "posted";
        public const string Cancelled = "cancelled";
    }

    public static class StockMovementTypes
    {
        public const string Receipt = "receipt";
        public const string WriteOff = "writeoff";
        public const string Inventory = "inventory";
        public const string Sale = "sale";               // автосписание по заказу
        public const string SaleReturn = "sale_return";  // возврат при отмене заказа
        public const string Reversal = "reversal";       // отмена проведённого документа
    }

    /// <summary>Складская позиция: сырьё, упаковка или полуфабрикат</summary>
    [Table("stock_items")]
    public class StockItem
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        [Column("unit")]
        public string Unit { get; set; } = StockUnits.Kg;

        [MaxLength(100)]
        [Column("category")]
        public string? Category { get; set; }

        [Required]
        [MaxLength(10)]
        [Column("kind")]
        public string Kind { get; set; } = StockItemKinds.Raw;

        /// <summary>Текущий остаток (кэш суммы движений)</summary>
        [Column("quantity", TypeName = "decimal(14,3)")]
        public decimal Quantity { get; set; }

        /// <summary>Средневзвешенная себестоимость единицы</summary>
        [Column("avg_cost", TypeName = "decimal(14,4)")]
        public decimal AvgCost { get; set; }

        /// <summary>Цена единицы в последнем приходе</summary>
        [Column("last_price", TypeName = "decimal(14,4)")]
        public decimal LastPrice { get; set; }

        /// <summary>Порог «заканчивается»</summary>
        [Column("min_quantity", TypeName = "decimal(14,3)")]
        public decimal MinQuantity { get; set; }

        [Column("is_archived")]
        public bool IsArchived { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    [Table("suppliers")]
    public class Supplier
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(12)]
        [Column("inn")]
        public string? Inn { get; set; }

        [MaxLength(30)]
        [Column("phone")]
        public string? Phone { get; set; }

        [MaxLength(150)]
        [Column("contact_person")]
        public string? ContactPerson { get; set; }

        [MaxLength(1000)]
        [Column("notes")]
        public string? Notes { get; set; }

        [Column("is_archived")]
        public bool IsArchived { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Складской документ: приход, списание или инвентаризация</summary>
    [Table("stock_documents")]
    public class StockDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("type")]
        public string Type { get; set; } = StockDocumentTypes.Receipt;

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = StockDocumentStatuses.Draft;

        /// <summary>Номер накладной поставщика или внутренний номер</summary>
        [MaxLength(50)]
        [Column("number")]
        public string? Number { get; set; }

        /// <summary>Дата документа (дата накладной)</summary>
        [Column("date")]
        public DateTime Date { get; set; } = DateTime.UtcNow;

        [Column("supplier_id")]
        public int? SupplierId { get; set; }

        [ForeignKey(nameof(SupplierId))]
        public Supplier? Supplier { get; set; }

        /// <summary>Причина списания: spoilage, expired, staff, other</summary>
        [MaxLength(30)]
        [Column("reason")]
        public string? Reason { get; set; }

        [MaxLength(1000)]
        [Column("comment")]
        public string? Comment { get; set; }

        [Column("total", TypeName = "decimal(14,2)")]
        public decimal Total { get; set; }

        /// <summary>Фото или скан бумажной накладной</summary>
        [MaxLength(300)]
        [Column("attachment_path")]
        public string? AttachmentPath { get; set; }

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("posted_at")]
        public DateTime? PostedAt { get; set; }

        [Column("cancelled_at")]
        public DateTime? CancelledAt { get; set; }

        public ICollection<StockDocumentLine> Lines { get; set; } = new List<StockDocumentLine>();
    }

    [Table("stock_document_lines")]
    public class StockDocumentLine
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
        public StockDocument? Document { get; set; }

        [Column("stock_item_id")]
        public int StockItemId { get; set; }

        [ForeignKey(nameof(StockItemId))]
        public StockItem? StockItem { get; set; }

        /// <summary>Приход/списание — количество; инвентаризация — фактический остаток</summary>
        [Column("quantity", TypeName = "decimal(14,3)")]
        public decimal Quantity { get; set; }

        /// <summary>Цена за единицу (приход — закупочная, остальное — себестоимость на момент проведения)</summary>
        [Column("price", TypeName = "decimal(14,4)")]
        public decimal Price { get; set; }

        [Column("sum", TypeName = "decimal(14,2)")]
        public decimal Sum { get; set; }

        /// <summary>Инвентаризация: учётный остаток на момент проведения</summary>
        [Column("expected_quantity", TypeName = "decimal(14,3)")]
        public decimal? ExpectedQuantity { get; set; }

        [Column("sort_order")]
        public int SortOrder { get; set; }
    }

    /// <summary>Журнал движений: каждое изменение остатка — отдельная запись</summary>
    [Table("stock_movements")]
    public class StockMovement
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public long Id { get; set; }

        [Column("stock_item_id")]
        public int StockItemId { get; set; }

        [ForeignKey(nameof(StockItemId))]
        [JsonIgnore]
        public StockItem? StockItem { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("type")]
        public string Type { get; set; } = StockMovementTypes.Receipt;

        /// <summary>Изменение остатка: + приход, − расход</summary>
        [Column("quantity", TypeName = "decimal(14,3)")]
        public decimal Quantity { get; set; }

        [Column("unit_cost", TypeName = "decimal(14,4)")]
        public decimal UnitCost { get; set; }

        /// <summary>Остаток после движения</summary>
        [Column("balance_after", TypeName = "decimal(14,3)")]
        public decimal BalanceAfter { get; set; }

        [Column("document_id")]
        public int? DocumentId { get; set; }

        [Column("order_id")]
        public int? OrderId { get; set; }

        [MaxLength(300)]
        [Column("comment")]
        public string? Comment { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Технологическая карта. Принадлежит ровно одному: позиции меню (shawarma_id),
    /// добавке (addon_id) или полуфабрикату (stock_item_id).
    /// </summary>
    [Table("tech_cards")]
    public class TechCard
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("shawarma_id")]
        public int? ShawarmaId { get; set; }

        [ForeignKey(nameof(ShawarmaId))]
        [JsonIgnore]
        public Shawarma? Shawarma { get; set; }

        [Column("addon_id")]
        public int? AddonId { get; set; }

        [ForeignKey(nameof(AddonId))]
        [JsonIgnore]
        public Addon? Addon { get; set; }

        [Column("stock_item_id")]
        public int? StockItemId { get; set; }

        [ForeignKey(nameof(StockItemId))]
        [JsonIgnore]
        public StockItem? StockItem { get; set; }

        /// <summary>Выход готового блюда, г (для справки и печати)</summary>
        [Column("output_weight", TypeName = "decimal(10,1)")]
        public decimal? OutputWeight { get; set; }

        /// <summary>Полуфабрикат: сколько единиц получается по этой карте (например, 1 кг соуса)</summary>
        [Column("yield_quantity", TypeName = "decimal(14,3)")]
        public decimal YieldQuantity { get; set; } = 1;

        /// <summary>Технология приготовления</summary>
        [MaxLength(4000)]
        [Column("instructions")]
        public string? Instructions { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<TechCardLine> Lines { get; set; } = new List<TechCardLine>();
    }

    [Table("tech_card_lines")]
    public class TechCardLine
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("tech_card_id")]
        [JsonIgnore]
        public int TechCardId { get; set; }

        [ForeignKey(nameof(TechCardId))]
        [JsonIgnore]
        public TechCard? TechCard { get; set; }

        [Column("stock_item_id")]
        public int StockItemId { get; set; }

        [ForeignKey(nameof(StockItemId))]
        public StockItem? StockItem { get; set; }

        /// <summary>Брутто — сколько списывается со склада, в единицах позиции</summary>
        [Column("gross", TypeName = "decimal(14,4)")]
        public decimal Gross { get; set; }

        /// <summary>Потери при обработке/ужарка, %</summary>
        [Column("loss_percent", TypeName = "decimal(5,2)")]
        public decimal LossPercent { get; set; }

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [NotMapped]
        public decimal Net => Math.Round(Gross * (1 - LossPercent / 100m), 4);
    }
}
