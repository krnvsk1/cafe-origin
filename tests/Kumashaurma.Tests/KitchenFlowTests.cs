using Kumashaurma.API.Services;
using Xunit;

namespace Kumashaurma.Tests
{
    public class KitchenFlowTests
    {
        [Fact]
        public void Overall_ReadyOnlyWhenAllStationsReady()
        {
            Assert.Equal(OrderStatuses.Preparing,
                KitchenFlow.Overall(new[] { OrderStatuses.Ready, OrderStatuses.Accepted }, OrderStatuses.Accepted));
            Assert.Equal(OrderStatuses.Ready,
                KitchenFlow.Overall(new[] { OrderStatuses.Ready, OrderStatuses.Ready }, OrderStatuses.Preparing));
            Assert.Equal(OrderStatuses.Accepted,
                KitchenFlow.Overall(new[] { OrderStatuses.Accepted, OrderStatuses.New }, OrderStatuses.New));
        }

        [Fact]
        public void Overall_NeverMovesBackward()
        {
            Assert.Equal(OrderStatuses.Preparing,
                KitchenFlow.Overall(new[] { OrderStatuses.Accepted, OrderStatuses.New }, OrderStatuses.Preparing));
            Assert.Equal(OrderStatuses.Cancelled,
                KitchenFlow.Overall(new[] { OrderStatuses.Ready }, OrderStatuses.Cancelled));
        }

        [Fact]
        public void Effective_FollowsOrderWhenItMovedAhead()
        {
            Assert.Equal(OrderStatuses.Preparing, KitchenFlow.Effective(null, OrderStatuses.Preparing));
            Assert.Equal(OrderStatuses.Ready, KitchenFlow.Effective(OrderStatuses.Ready, OrderStatuses.Accepted));
            Assert.Equal(OrderStatuses.Ready, KitchenFlow.Effective(OrderStatuses.Accepted, OrderStatuses.Completed));
            Assert.Equal(OrderStatuses.New, KitchenFlow.Effective(null, OrderStatuses.New));
        }

        [Fact]
        public void StationFor_OwnThenParentThenDefault()
        {
            Assert.Equal(2, KitchenFlow.StationFor(2, 1, 1));
            Assert.Equal(1, KitchenFlow.StationFor(null, 1, 5));
            Assert.Equal(5, KitchenFlow.StationFor(null, null, 5));
        }
    
        [Fact]
        public void Effective_ByItems_PartlyReadyIsPreparing_AllReadyIsReady()
        {
            Assert.Equal(OrderStatuses.Preparing, KitchenFlow.Effective(OrderStatuses.Accepted, OrderStatuses.Accepted, 3, 1));
            Assert.Equal(OrderStatuses.Ready, KitchenFlow.Effective(OrderStatuses.Preparing, OrderStatuses.Preparing, 3, 3));
            Assert.Equal(OrderStatuses.Accepted, KitchenFlow.Effective(OrderStatuses.Accepted, OrderStatuses.New, 2, 0));
        }

        [Fact]
        public void Clamp_ServedNeverExceedsReady()
        {
            Assert.Equal((2, 1), KitchenFlow.Clamp(2, 5, 1));
            Assert.Equal((1, 1), KitchenFlow.Clamp(2, 1, 3));
            Assert.Equal((0, 0), KitchenFlow.Clamp(2, -1, 0));
        }
    }
}
