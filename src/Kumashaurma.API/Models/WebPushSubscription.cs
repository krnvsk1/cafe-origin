using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kumashaurma.API.Models
{
    /// <summary>Подписка браузера на уведомления (Web Push): телефон официанта и т.п.</summary>
    [Table("push_subscriptions")]
    public class WebPushSubscription
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Required]
        [MaxLength(1000)]
        [Column("endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        [Column("p256dh")]
        public string P256dh { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        [Column("auth")]
        public string Auth { get; set; } = string.Empty;

        [MaxLength(300)]
        [Column("user_agent")]
        public string? UserAgent { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("last_used_at")]
        public DateTime? LastUsedAt { get; set; }
    }
}
