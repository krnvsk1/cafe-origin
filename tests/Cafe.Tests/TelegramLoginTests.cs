using Cafe.API.Services;
using Xunit;

namespace Cafe.Tests
{
    public class TelegramLoginTests
    {
        [Fact]
        public void Session_ApprovedOnce_TakenOnce()
        {
            var s = new TelegramLoginSessions();
            var code = s.Create();
            Assert.Matches("^[A-Za-z0-9_-]+$", code);
            Assert.Null(s.Get(code)!.User);

            var tg = new TelegramAuth.TelegramUser(42, "Анна", null, "anna");
            Assert.True(s.Approve(code, tg));
            Assert.False(s.Approve(code, new TelegramAuth.TelegramUser(7, null, null, null)));   // второй раз не перехватить

            Assert.Equal(42, s.Take(code)!.Id);
            Assert.Null(s.Take(code));
            Assert.Null(s.Get(code));
        }

        [Fact]
        public void UnknownCode_IsNotApproved()
        {
            var s = new TelegramLoginSessions();
            Assert.False(s.Approve("nope", new TelegramAuth.TelegramUser(1, null, null, null)));
            Assert.Null(s.Get("nope"));
        }
    }
}
