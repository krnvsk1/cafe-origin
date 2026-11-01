using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cafe.API.Models
{
    public static class PrintJobStatuses
    {
        /// <summary>Ждёт программу печати на кассовом ПК</summary>
        public const string Pending = "pending";
        /// <summary>Программа забрала задание и печатает</summary>
        public const string Taken = "taken";
        public const string Done = "done";
        public const string Error = "error";
    }

    /// <summary>
    /// Задание для программы на кассовом ПК: напечатать счёт на принтере (RONGTA) или передать запрос
    /// устройству в сети кафе (АТОЛ Web Server, УТМ ЕГАИС), когда сервер в интернете и сам до них не достаёт.
    /// </summary>
    [Table("print_jobs")]
    public class PrintJob
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>precheck — счёт для гостя, test — проверочная печать, http — запрос к устройству в кафе (АТОЛ, УТМ)</summary>
        [Required]
        [MaxLength(20)]
        [Column("kind")]
        public string Kind { get; set; } = "precheck";

        [Column("check_id")]
        public int? CheckId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>Печать — байты ESC/POS в base64; http — запрос (JSON: метод, адрес, тело)</summary>
        [Required]
        [Column("payload", TypeName = "text")]
        public string Payload { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        [Column("status")]
        public string Status { get; set; } = PrintJobStatuses.Pending;

        [MaxLength(300)]
        [Column("error")]
        public string? Error { get; set; }

        /// <summary>http — ответ устройства (JSON: код, тип, тело в base64)</summary>
        [Column("result", TypeName = "text")]
        public string? Result { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("taken_at")]
        public DateTime? TakenAt { get; set; }

        [Column("done_at")]
        public DateTime? DoneAt { get; set; }

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }
    }
}
