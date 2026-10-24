using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kumashaurma.API.Models
{
    /// <summary>Настройки, которые меняются из админки (значение — JSON)</summary>
    [Table("app_settings")]
    public class AppSetting
    {
        [Key]
        [MaxLength(100)]
        [Column("key")]
        public string Key { get; set; } = string.Empty;

        [Column("value", TypeName = "text")]
        public string Value { get; set; } = "{}";

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_by_user_id")]
        public int? UpdatedByUserId { get; set; }
    }
}
