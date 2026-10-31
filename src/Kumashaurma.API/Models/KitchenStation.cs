using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Kumashaurma.API.Services;

namespace Kumashaurma.API.Models
{
    /// <summary>Кухня (цех): «Горячая кухня», «Мангал»… Блюдо идёт на ту кухню, что указана у него в меню</summary>
    [Table("kitchen_stations")]
    public class KitchenStation
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(60)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("sort_order")]
        public int SortOrder { get; set; }
    }

    /// <summary>Как идёт часть заказа на конкретной кухне: принят, готовится, готов</summary>
    [Table("order_station_statuses")]
    public class OrderStationStatus
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("order_id")]
        public int OrderId { get; set; }

        [Column("station_id")]
        public int StationId { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("status")]
        public string Status { get; set; } = OrderStatuses.Accepted;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_by_user_id")]
        public int? UpdatedByUserId { get; set; }
    }

    /// <summary>
    /// Готовность позиции заказа на кухне: сколько штук уже готово и сколько отдано гостю.
    /// Блюда отдаются по мере готовности, а не всем заказом сразу.
    /// </summary>
    [Table("order_item_kitchen_states")]
    public class OrderItemKitchenState
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("order_item_id")]
        public int OrderItemId { get; set; }

        [Column("ready_quantity")]
        public int ReadyQuantity { get; set; }

        [Column("served_quantity")]
        public int ServedQuantity { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
