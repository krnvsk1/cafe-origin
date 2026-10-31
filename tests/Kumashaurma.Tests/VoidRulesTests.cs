using Kumashaurma.API.Models;
using Kumashaurma.API.Services;
using Xunit;

namespace Kumashaurma.Tests
{
    public class VoidRulesTests
    {
        [Theory]
        [InlineData(OrderStatuses.New, false, false)]
        [InlineData(OrderStatuses.Accepted, true, false)]
        [InlineData(OrderStatuses.Preparing, true, false)]
        [InlineData(OrderStatuses.Ready, false, true)]
        [InlineData(OrderStatuses.Completed, false, true)]
        public void KitchenDecidesOnlyWhileCooking(string status, bool needsKitchen, bool alreadyMade)
        {
            Assert.Equal(needsKitchen, VoidService.NeedsKitchen(status));
            Assert.Equal(alreadyMade, VoidService.AlreadyMade(status));
        }

        [Fact]
        public void UnitPrice_IncludesAddons()
        {
            var i = new OrderItem { Price = 300, Quantity = 2 };
            i.SelectedAddons.Add(new OrderItemAddon { Price = 50, Quantity = 2 });
            Assert.Equal(400, VoidService.UnitPrice(i));
        }
    }
}
