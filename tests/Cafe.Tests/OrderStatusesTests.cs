using Cafe.API.Services;
using Xunit;

namespace Cafe.Tests
{
    public class OrderStatusesTests
    {
        [Fact]
        public void Accepted_IsKnownStatus_BetweenNewAndPreparing()
        {
            var all = OrderStatuses.All;
            Assert.Contains("Принят", all);
            Assert.True(Array.IndexOf(all, OrderStatuses.New) < Array.IndexOf(all, OrderStatuses.Accepted));
            Assert.True(Array.IndexOf(all, OrderStatuses.Accepted) < Array.IndexOf(all, OrderStatuses.Preparing));
        }

        [Theory]
        [InlineData(OrderStatuses.New, true)]
        [InlineData(OrderStatuses.Accepted, true)]
        [InlineData(OrderStatuses.Preparing, false)]
        [InlineData(OrderStatuses.Ready, false)]
        [InlineData(OrderStatuses.Cancelled, false)]
        public void IsNotStarted_OnlyBeforeCooking(string status, bool expected) =>
            Assert.Equal(expected, OrderStatuses.IsNotStarted(status));
    }
}
