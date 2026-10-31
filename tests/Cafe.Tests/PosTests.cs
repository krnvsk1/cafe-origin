using Cafe.API.Controllers;
using Xunit;

namespace Cafe.Tests
{
    public class PosTests
    {
        [Theory]
        [InlineData("8 (999) 123-45-67", "+79991234567")]
        [InlineData("+7 999 123 45 67", "+79991234567")]
        [InlineData("9991234567", "+79991234567")]
        [InlineData("+77999999999", "+77999999999")]
        [InlineData("123", null)]
        [InlineData("", null)]
        public void NormalizePhone_HandlesCommonFormats(string input, string? expected) =>
            Assert.Equal(expected, PosController.NormalizePhone(input));

        [Fact]
        public void StartOfToday_IsMoscowMidnight()
        {
            var start = PosController.StartOfTodayUtc();
            var moscow = start.AddHours(3);
            Assert.Equal(TimeSpan.Zero, moscow.TimeOfDay);
            Assert.True(DateTime.UtcNow - start < TimeSpan.FromDays(1));
            Assert.True(DateTime.UtcNow >= start);
        }
    }
}
