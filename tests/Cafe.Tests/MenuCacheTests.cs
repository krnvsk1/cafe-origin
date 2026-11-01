using Cafe.API.Services;
using Xunit;

namespace Cafe.Tests
{
    public class MenuCacheTests
    {
        [Fact]
        public void ChangeDuringLoad_IsNotCached_AndInvalidateClears()
        {
            MenuCache.Invalidate();
            var v = MenuCache.Version;
            MenuCache.Invalidate();            // меню поменяли, пока грузили
            MenuCache.Set(new List<int> { 1 }, v);
            Assert.Null(MenuCache.Get<List<int>>());

            MenuCache.Set(new List<int> { 2 }, MenuCache.Version);
            Assert.Equal(2, MenuCache.Get<List<int>>()![0]);

            MenuCache.Invalidate();
            Assert.Null(MenuCache.Get<List<int>>());
        }
    }
}
