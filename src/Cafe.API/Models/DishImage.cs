using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Cafe.API.Models
{
    [Table("dish_images")]
    public class DishImage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }
        
        [Column("dish_id")]
        public int DishId { get; set; }
        
        [ForeignKey("DishId")]
        [JsonIgnore]
        public Dish? Dish { get; set; }
        
        [Required]
        [Column("file_name")]
        public string FileName { get; set; } = string.Empty;
        
        [Column("file_path")]
        public string FilePath { get; set; } = string.Empty;
        
        [Column("is_primary")]
        public bool IsPrimary { get; set; }
        
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}