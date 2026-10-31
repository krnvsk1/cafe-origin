using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Cafe.API.Models
{
    /// <summary>
    /// Избранная позиция меню пользователя (карточка или конкретный вариант)
    /// </summary>
    [Table("user_favorites")]
    public class UserFavorite
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("dish_id")]
        public int DishId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public AppUser? User { get; set; }

        [JsonIgnore]
        public Dish? Dish { get; set; }
    }
}
