using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Cafe.API.Models
{
    [Table("order_items")]
    public class OrderItem
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }
        
        [Column("order_id")]
        [JsonIgnore]
        public int OrderId { get; set; }
        
        [ForeignKey("OrderId")]
        [JsonIgnore]
        public Order? Order { get; set; }
        
        [Column("dish_id")]
        public int DishId { get; set; }
        
        [MaxLength(100)]
        [Column("name")]
        public string Name { get; set; } = string.Empty;
        
        [Column("quantity")]
        public int Quantity { get; set; } = 1;
        
        /// <summary>Цена одной позиции; у весового блюда — уже за указанный вес</summary>
        [Column("price", TypeName = "decimal(10,2)")]
        public decimal Price { get; set; }

        /// <summary>Вес весового блюда, г (цена в меню — за 100 г); null — обычная позиция</summary>
        [Column("weight")]
        public int? Weight { get; set; }
        
        [Column("subtotal", TypeName = "decimal(10,2)")]
        public decimal Subtotal => Price * Quantity;
        
        /// <summary>Счёт стола, куда перенесли позицию при делении счёта; null — счёт заказа</summary>
        [Column("check_id")]
        [JsonIgnore]
        public int? CheckId { get; set; }

        // 👇 ДОБАВЛЯЕМ: связь с выбранными добавками
        public ICollection<OrderItemAddon> SelectedAddons { get; set; } = new List<OrderItemAddon>();
    }
}