using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Kumashaurma.API.Models
{
    /// <summary>
    /// Отзыв клиента о заказе (один на заказ)
    /// </summary>
    [Table("order_reviews")]
    public class OrderReview
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("order_id")]
        public int OrderId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        // Оценка 1–5
        [Range(1, 5)]
        [Column("rating")]
        public int Rating { get; set; }

        [MaxLength(1000)]
        [Column("comment")]
        public string? Comment { get; set; }

        // Ответ заведения
        [MaxLength(1000)]
        [Column("admin_reply")]
        public string? AdminReply { get; set; }

        [Column("replied_at")]
        public DateTime? RepliedAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public Order? Order { get; set; }

        [JsonIgnore]
        public AppUser? User { get; set; }
    }
}
