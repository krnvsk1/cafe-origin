using Kumashaurma.API.Authorization;
using Kumashaurma.API.Services;
using Xunit;

namespace Kumashaurma.Tests
{
    public class ShiftTests
    {
        [Fact]
        public void BusinessDate_IsMoscowDay()
        {
            // 22:30 UTC — уже 01:30 следующего дня по Москве
            Assert.Equal(new DateTime(2026, 11, 1), ShiftService.BusinessDate(new DateTime(2026, 10, 31, 22, 30, 0)));
            Assert.Equal(new DateTime(2026, 10, 31), ShiftService.BusinessDate(new DateTime(2026, 10, 31, 20, 59, 0)));
        }

        [Fact]
        public void ShiftPermissions_AreInCatalogAndManagerDefaults()
        {
            Assert.Contains(Permissions.Shift, Permissions.Keys);
            Assert.Contains(Permissions.ChecksAll, Permissions.Keys);
            Assert.Contains(Permissions.Shift, Permissions.Defaults["manager"]);
            Assert.Contains(Permissions.ChecksAll, Permissions.Defaults["manager"]);
        }
    }
}
