using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace Kumashaurma.API.Models
{
    [Table("users")]
    public class AppUser : IdentityUser<int>
    {
        [PersonalData]
        [MaxLength(100)]
        [Column("first_name")]
        public string? FirstName { get; set; }

        [PersonalData]
        [MaxLength(100)]
        [Column("last_name")]
        public string? LastName { get; set; }

        [Column("phone_verified")]
        public bool PhoneVerified { get; set; } = false;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [Column("points_balance")]
        public int PointsBalance { get; set; } = 0;

        // Привязанный Telegram (ID пользователя Telegram = ID личного чата с ботом)
        [Column("telegram_user_id")]
        public long? TelegramUserId { get; set; }

        // Присылать уведомления о статусе заказов в Telegram
        [Column("telegram_notifications")]
        public bool TelegramNotifications { get; set; } = true;

        // Navigation properties
        [JsonIgnore]
        public ICollection<UserAddress> Addresses { get; set; } = new List<UserAddress>();
        [JsonIgnore]
        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}
