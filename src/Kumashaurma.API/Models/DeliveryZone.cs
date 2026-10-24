using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kumashaurma.API.Models
{
    /// <summary>
    /// Зона доставки: многоугольник на карте со своей ценой, минимальной суммой и временем
    /// </summary>
    [Table("delivery_zones")]
    public class DeliveryZone
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        // Координаты вершин в JSON: [[широта, долгота], ...]
        [Required]
        [JsonIgnore]
        [Column("polygon", TypeName = "text")]
        public string PolygonJson { get; set; } = "[]";

        [Column("delivery_price", TypeName = "decimal(10,2)")]
        public decimal DeliveryPrice { get; set; }

        [Column("min_order_amount", TypeName = "decimal(10,2)")]
        public decimal MinOrderAmount { get; set; }

        // Бесплатная доставка от этой суммы (null — всегда платная)
        [Column("free_delivery_from", TypeName = "decimal(10,2)")]
        public decimal? FreeDeliveryFrom { get; set; }

        // Ориентировочное время доставки, минут
        [Column("delivery_minutes")]
        public int DeliveryMinutes { get; set; } = 45;

        [MaxLength(20)]
        [Column("color")]
        public string Color { get; set; } = "#0891b2";

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [NotMapped]
        public double[][] Polygon
        {
            get
            {
                try { return JsonSerializer.Deserialize<double[][]>(PolygonJson) ?? Array.Empty<double[]>(); }
                catch (JsonException) { return Array.Empty<double[]>(); }
            }
            set => PolygonJson = JsonSerializer.Serialize(value ?? Array.Empty<double[]>());
        }

        /// <summary>Стоимость доставки для суммы заказа</summary>
        public decimal PriceFor(decimal subtotal) =>
            FreeDeliveryFrom.HasValue && subtotal >= FreeDeliveryFrom.Value ? 0 : DeliveryPrice;

        /// <summary>Попадает ли точка в зону (лучевой алгоритм; для масштабов города плоской геометрии достаточно)</summary>
        public bool Contains(double lat, double lng)
        {
            var poly = Polygon;
            if (poly.Length < 3) return false;

            var inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (poly[i].Length < 2 || poly[j].Length < 2) continue;
                double yi = poly[i][0], xi = poly[i][1];
                double yj = poly[j][0], xj = poly[j][1];
                var intersects = ((yi > lat) != (yj > lat))
                    && (lng < (xj - xi) * (lat - yi) / (yj - yi) + xi);
                if (intersects) inside = !inside;
            }
            return inside;
        }
    }
}
