using System.Text.Json;
using System.Text.Json.Serialization;
using Cafe.API.Models;
using Xunit;

namespace Cafe.Tests
{
    public class OrderSerializationTests
    {
        // Те же настройки, что в Program.cs (AddJsonOptions)
        private static readonly JsonSerializerOptions Api = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        [Fact]
        public void OrderJson_ContainsItemsAndAddons_WithoutBackReferences()
        {
            var order = new Order { Id = 15, CustomerName = "Гость", Total = 700 };
            var item = new OrderItem { Id = 1, DishId = 2, Name = "Сырники", Price = 350, Quantity = 2, Order = order };
            item.SelectedAddons.Add(new OrderItemAddon { Id = 1, AddonId = 3, AddonName = "Сметана", Price = 50, OrderItem = item });
            order.OrderItems.Add(item);

            using var json = JsonDocument.Parse(JsonSerializer.Serialize(order, Api));

            // Список заказов, дашборд и «Состав заказа» читают orderItems — без них состав пустой
            var items = json.RootElement.GetProperty("orderItems");
            Assert.Equal(1, items.GetArrayLength());
            var first = items[0];
            Assert.Equal("Сырники", first.GetProperty("name").GetString());
            Assert.Equal(700, first.GetProperty("subtotal").GetDecimal());
            Assert.False(first.TryGetProperty("order", out _));

            var addon = first.GetProperty("selectedAddons")[0];
            Assert.Equal("Сметана", addon.GetProperty("addonName").GetString());
            Assert.False(addon.TryGetProperty("orderItem", out _));
        }
    }
}
