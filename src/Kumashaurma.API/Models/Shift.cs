using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kumashaurma.API.Models
{
    /// <summary>Смена заведения на день: открыта — официанты могут открывать свои смены и чеки</summary>
    [Table("venue_shifts")]
    public class VenueShift
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>Рабочий день (по Москве, полночь)</summary>
        [Column("business_date")]
        public DateTime BusinessDate { get; set; }

        [Column("opened_at")]
        public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

        [Column("opened_by_user_id")]
        public int? OpenedByUserId { get; set; }

        [Column("closed_at")]
        public DateTime? ClosedAt { get; set; }

        [Column("closed_by_user_id")]
        public int? ClosedByUserId { get; set; }

        [MaxLength(500)]
        [Column("comment")]
        public string? Comment { get; set; }
    }

    /// <summary>Смена официанта внутри смены заведения</summary>
    [Table("waiter_shifts")]
    public class WaiterShift
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("venue_shift_id")]
        public int VenueShiftId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("opened_at")]
        public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

        [Column("closed_at")]
        public DateTime? ClosedAt { get; set; }
    }
}
