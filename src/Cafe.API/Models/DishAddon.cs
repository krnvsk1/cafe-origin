using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Cafe.API.Models
{
    [Table("dish_addons")]
    public class DishAddon
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }
        
        [Column("dish_id")]
        public int DishId { get; set; }
        
        [ForeignKey("DishId")]
        [JsonIgnore]
        public Dish Dish { get; set; } = null!;
        
        [Column("addon_id")]
        public int AddonId { get; set; }
        
        [ForeignKey("AddonId")]
        [JsonIgnore]
        public Addon Addon { get; set; } = null!;
        
        [Column("custom_price", TypeName = "decimal(10,2)")]
        public decimal? CustomPrice { get; set; } // Специальная цена для этого товара
        
        [Column("is_default")]
        public bool IsDefault { get; set; } // Добавка по умолчанию
        
        [Column("max_quantity")]
        public int MaxQuantity { get; set; } = 1; // Максимальное количество для этой добавки
    }
}