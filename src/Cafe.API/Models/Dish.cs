using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json.Serialization;

namespace Cafe.API.Models
{
    [Table("dishes")]
    public class Dish
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        [Range(0, 10000)]
        [Column("price", TypeName = "decimal(10,2)")]
        public decimal Price { get; set; }
        
        [MaxLength(500)]
        [Column("description")]
        public string Description { get; set; } = string.Empty;
        
        [MaxLength(50)]
        [Column("category")]
        public string Category { get; set; } = string.Empty;
        
        [Column("is_spicy")]
        public bool IsSpicy { get; set; }
        
        [Column("is_available")]
        public bool IsAvailable { get; set; } = true;

        // Иерархия: parent_id = null → карточка-категория (заголовок группы, не продаётся),
        // parent_id = N → дочерняя позиция внутри карточки N (продаваемый товар)
        [Column("parent_id")]
        [JsonPropertyName("parentId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int? ParentId { get; set; }

        [ForeignKey("ParentId")]
        [JsonIgnore]
        public Dish? Parent { get; set; }

        public ICollection<Dish> Children { get; set; } = new List<Dish>();

        // Вычисляемое: является ли карточкой-категорией
        [NotMapped]
        public bool IsCard => ParentId == null;

        [Column("is_promo")]
        public bool IsPromo { get; set; } = false;

        // Метка на карточке: hit | new | promo | lean (null — без метки)
        [MaxLength(20)]
        [Column("badge")]
        public string? Badge { get; set; }
        
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        /// <summary>
        /// Где позицию можно заказать: «Доставка», «Самовывоз», «В зале» через запятую.
        /// null — везде. Например, пиво — только «В зале».
        /// </summary>
        [MaxLength(60)]
        [Column("order_types")]
        [System.Text.Json.Serialization.JsonIgnore]
        public string? OrderTypes { get; set; }

        /// <summary>Способы получения, для которых позиция доступна (null — все)</summary>
        [NotMapped]
        public string[]? AvailableOrderTypes
        {
            get => SalesChannels.Parse(OrderTypes);
            set => OrderTypes = SalesChannels.Format(value);
        }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        public ICollection<DishImage> Images { get; set; } = new List<DishImage>();
    
        [NotMapped]
        public string? PrimaryImage => Images?.FirstOrDefault(i => i.IsPrimary)?.FilePath;

        [Column("sort_order")]
        public int SortOrder { get; set; }

        /// <summary>На какую кухню идёт блюдо (у варианта без своей — как у карточки; не задано — первая кухня)</summary>
        [Column("kitchen_station_id")]
        public int? KitchenStationId { get; set; }
        
        // Связь с добавками
        public ICollection<DishAddon> Addons { get; set; } = new List<DishAddon>();

        // Минимальная цена среди дочерних позиций или своя цена
        [NotMapped]
        public decimal DisplayPrice => Children != null && Children.Any()
            ? Children.OrderBy(c => c.Price).First().Price
            : Price;
    }
}