using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cafe.API.Models
{
    /// <summary>Что контролируем: холодильник, склад, фритюрница, критическая контрольная точка ХАССП</summary>
    public static class HaccpObjectKinds
    {
        public const string Fridge = "fridge";
        public const string Storage = "storage";
        public const string Fryer = "fryer";
        public const string Ccp = "ccp";
        public static readonly string[] All = { Fridge, Storage, Fryer, Ccp };
    }

    [Table("haccp_objects")]
    public class HaccpObject
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        [Column("kind")]
        public string Kind { get; set; } = HaccpObjectKinds.Fridge;

        [Required]
        [MaxLength(200)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Помещение / где стоит (для журнала холодильников — «наименование производственного помещения»)</summary>
        [MaxLength(200)]
        [Column("location")]
        public string? Location { get; set; }

        /// <summary>Норма температуры, °C (холодильник +2…+6, морозильник −18…; ККТ — «не ниже 75»)</summary>
        [Column("min_value", TypeName = "decimal(6,1)")]
        public decimal? MinValue { get; set; }

        [Column("max_value", TypeName = "decimal(6,1)")]
        public decimal? MaxValue { get; set; }

        /// <summary>Норма влажности для складов, %</summary>
        [Column("min_humidity", TypeName = "decimal(5,1)")]
        public decimal? MinHumidity { get; set; }

        [Column("max_humidity", TypeName = "decimal(5,1)")]
        public decimal? MaxHumidity { get; set; }

        /// <summary>Сколько раз в день проверять</summary>
        [Column("checks_per_day")]
        public int ChecksPerDay { get; set; } = 1;

        /// <summary>Для ККТ — что делать при отклонении (подсказка в форме)</summary>
        [MaxLength(1000)]
        [Column("note")]
        public string? Note { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Сотрудник для гигиенического журнала: медкнижка и аттестация со сроками (аккаунт в системе не обязателен)</summary>
    [Table("haccp_staff")]
    public class HaccpStaff
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        [Column("full_name")]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(100)]
        [Column("position")]
        public string? Position { get; set; }

        [MaxLength(50)]
        [Column("medical_book_number")]
        public string? MedicalBookNumber { get; set; }

        /// <summary>Медосмотр действителен до</summary>
        [Column("medical_exam_until")]
        public DateTime? MedicalExamUntil { get; set; }

        /// <summary>Гигиеническое обучение (аттестация) действительно до</summary>
        [Column("hygiene_training_until")]
        public DateTime? HygieneTrainingUntil { get; set; }

        /// <summary>Аккаунт в системе, если есть</summary>
        [Column("user_id")]
        public int? UserId { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [MaxLength(500)]
        [Column("note")]
        public string? Note { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Запись в журнале. Значения граф — JSON; определения граф — в коде (HaccpJournals)</summary>
    [Table("haccp_entries")]
    public class HaccpEntry
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        [Column("journal")]
        public string Journal { get; set; } = string.Empty;

        [Column("object_id")]
        public int? ObjectId { get; set; }

        [ForeignKey(nameof(ObjectId))]
        public HaccpObject? Object { get; set; }

        [Column("staff_id")]
        public int? StaffId { get; set; }

        [ForeignKey(nameof(StaffId))]
        public HaccpStaff? Staff { get; set; }

        /// <summary>Приход на склад, по которому сделан бракераж сырья</summary>
        [Column("stock_document_id")]
        public int? StockDocumentId { get; set; }

        /// <summary>Когда проводился замер / осмотр (может отличаться от времени внесения)</summary>
        [Column("occurred_at")]
        public DateTime OccurredAt { get; set; }

        [Required]
        [Column("data", TypeName = "jsonb")]
        public string Data { get; set; } = "{}";

        [Column("has_deviation")]
        public bool HasDeviation { get; set; }

        /// <summary>Что вышло за норму (формируется автоматически)</summary>
        [MaxLength(1000)]
        [Column("deviation")]
        public string? Deviation { get; set; }

        /// <summary>Корректирующее действие — обязательно при отклонении</summary>
        [MaxLength(1000)]
        [Column("corrective_action")]
        public string? CorrectiveAction { get; set; }

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }

        [MaxLength(200)]
        [Column("created_by_name")]
        public string? CreatedByName { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Номер редакции: 1 — исходная запись, дальше — правки (каждая в haccp_entry_revisions)</summary>
        [Column("version")]
        public int Version { get; set; } = 1;

        [Column("is_annulled")]
        public bool IsAnnulled { get; set; }

        [MaxLength(500)]
        [Column("annul_reason")]
        public string? AnnulReason { get; set; }
    }

    /// <summary>Прежние редакции записи: что было до правки, кто и почему изменил</summary>
    [Table("haccp_entry_revisions")]
    public class HaccpEntryRevision
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("entry_id")]
        public int EntryId { get; set; }

        [ForeignKey(nameof(EntryId))]
        public HaccpEntry? Entry { get; set; }

        /// <summary>Номер редакции, которую заменили</summary>
        [Column("version")]
        public int Version { get; set; }

        /// <summary>edit или annul</summary>
        [Required]
        [MaxLength(10)]
        [Column("action")]
        public string Action { get; set; } = "edit";

        [Column("occurred_at")]
        public DateTime OccurredAt { get; set; }

        [Required]
        [Column("data", TypeName = "jsonb")]
        public string Data { get; set; } = "{}";

        [MaxLength(1000)]
        [Column("corrective_action")]
        public string? CorrectiveAction { get; set; }

        [Required]
        [MaxLength(500)]
        [Column("reason")]
        public string Reason { get; set; } = string.Empty;

        [Column("changed_by_user_id")]
        public int? ChangedByUserId { get; set; }

        [MaxLength(200)]
        [Column("changed_by_name")]
        public string? ChangedByName { get; set; }

        [Column("changed_at")]
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }
}
