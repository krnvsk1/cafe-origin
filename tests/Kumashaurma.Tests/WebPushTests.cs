using Kumashaurma.API.Services;
using Xunit;

namespace Kumashaurma.Tests
{
    public class WebPushTests
    {
        [Theory]
        [InlineData("https://fcm.googleapis.com/fcm/send/abc", true)]
        [InlineData("https://web.push.apple.com/QGx", true)]
        [InlineData("https://updates.push.services.mozilla.com/wpush/v2/x", true)]
        [InlineData("http://fcm.googleapis.com/fcm/send/abc", false)]
        [InlineData("https://evil.example.com/fcm.googleapis.com", false)]
        [InlineData("https://notpush.apple.com.evil.ru/x", false)]
        [InlineData("https://localhost:5000/x", false)]
        public void OnlyKnownPushServices(string endpoint, bool allowed) =>
            Assert.Equal(allowed, WebPushService.IsAllowedEndpoint(endpoint));
    }
}
