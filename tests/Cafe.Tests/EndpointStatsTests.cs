using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Cafe.API.Monitoring;
using Xunit;

namespace Cafe.Tests
{
    public class EndpointStatsTests
    {
        private static HttpContext Request(string method, string path, string template, int status)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Method = method;
            ctx.Request.Path = path;
            ctx.Response.StatusCode = status;
            ctx.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, null, null));
            return ctx;
        }

        [Fact]
        public void GroupsByRouteTemplate_CountsErrorsAndPercentiles()
        {
            var stats = new EndpointStats();
            for (var i = 0; i < 18; i++) stats.Record(Request("GET", $"/api/orders/{i}", "api/orders/{id}", 200), 40);
            stats.Record(Request("GET", "/api/orders/7", "api/orders/{id}", 500), 900);
            stats.Record(Request("GET", "/api/orders/8", "api/orders/{id}", 404), 20);

            var row = Assert.Single(stats.Summary(TimeSpan.FromMinutes(15)));
            Assert.Equal("/api/orders/{id}", row.Route);
            Assert.Equal(20, row.Count);
            Assert.Equal(1, row.Errors5xx);
            Assert.Equal(1, row.Errors4xx);
            Assert.Equal(5.0, row.ErrorRate);
            Assert.Equal(50, row.P50Ms);   // корзина «до 50 мс»
            Assert.Equal(900, row.MaxMs);
            Assert.NotNull(row.LastError);
        }

        [Fact]
        public void SkipsLiveConnectionsHealthAndFiles()
        {
            var stats = new EndpointStats();
            stats.Record(Request("GET", "/api/events/orders", "api/events/orders", 200), 60000);
            stats.Record(Request("GET", "/health/ready", "health/ready", 200), 5);
            Assert.Empty(stats.Summary(TimeSpan.FromHours(1)));
        }

        [Fact]
        public async Task HealthCheck_DegradedWhenErrorRateAboveFivePercent()
        {
            var stats = new EndpointStats();
            for (var i = 0; i < 18; i++) stats.Record(Request("POST", "/api/pos/orders", "api/pos/orders", 200), 30);
            for (var i = 0; i < 2; i++) stats.Record(Request("POST", "/api/pos/orders", "api/pos/orders", 500), 30);
            var result = await new EndpointsHealthCheck(stats).CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());
            Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
            Assert.Contains("/api/pos/orders", result.Description);
        }
    }
}
