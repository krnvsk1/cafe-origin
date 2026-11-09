using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Cafe.API.Services.Printing;
using Xunit;

namespace Cafe.Tests
{
    public class PrintingTests
    {
        static PrintingTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        private static string Text(byte[] bytes, int encoding = 866) => Encoding.GetEncoding(encoding).GetString(bytes);

        [Fact]
        public void Wrap_SplitsByWords_AndLongWords()
        {
            Assert.Equal(new[] { "Свиная шея", "на мангале" }, EscPos.Wrap("Свиная шея на мангале", 12));
            Assert.Equal(new[] { "abcde", "fgh" }, EscPos.Wrap("abcdefgh", 5));
        }

        [Fact]
        public void Precheck_HasLinesTotalAndCut_InCp866()
        {
            var s = new PrintSettings { Width = 48, CodeTable = 17, Encoding = 866, Footer = "Спасибо!" };
            var data = new PrecheckData("Кафе", "5", "счёт №12", "Никита", 2, DateTime.UtcNow, DateTime.UtcNow,
                new[] { new PrecheckLine("Свиная шея · 350 г", 1, 1050, 1050), new PrecheckLine("Пиво", 2, 200, 400) }, s.Footer);
            var bytes = PrintService.Precheck(s, data);

            Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x74, 17 }, bytes[..5]);   // сброс и кодовая таблица
            Assert.Equal(new byte[] { 0x1D, 0x56, 0x42, 0x00 }, bytes[^4..]);       // обрезка
            var text = Text(bytes);
            Assert.Contains("СЧЕТ", text);
            Assert.Contains("Стол 5", text);
            Assert.Contains("Свиная шея · 350 г".Replace("·", "?"), text.Replace("·", "?"));
            Assert.Contains("1 450,00", text);   // итог 1050 + 400
            Assert.Contains("Это не кассовый чек", text);
            // Ни одна строка не длиннее ширины принтера
            foreach (var line in text.Split('\n'))
                Assert.True(new string(line.Where(ch => ch >= ' ').ToArray()).Length <= 48 + 8);
        }

        [Fact]
        public void Precheck_OnHouse_ShowsWhoClosedAndNoPayment()
        {
            var s = new PrintSettings { Width = 48, CodeTable = 17, Encoding = 866, Footer = "Спасибо!" };
            var data = new PrecheckData("Кафе", "5", "счёт №12", "Никита", null, DateTime.UtcNow, DateTime.UtcNow,
                new[] { new PrecheckLine("Борщ", 1, 300, 300) }, s.Footer, new PrecheckStaff("Анна Петрова", "Наличные (Принтер)"));
            var text = Text(PrintService.Precheck(s, data));
            Assert.Contains("Наличные (Принтер)", text);
            Assert.Contains("Закрыл: Анна Петрова", text);
            Assert.Contains("ЗА СЧЕТ ЗАВЕДЕНИЯ", text);  // принтер печатает Ё как Е
            Assert.Contains("Это не кассовый чек", text);
            Assert.DoesNotContain("Спасибо!", text);
        }

        [Fact]
        public void Pair_FitsWidth()
        {
            var p = new EscPos(32, 17, 866).Pair("ИТОГО", "1 450,00");
            var text = Text(p.ToArray()[5..]);
            Assert.Contains("ИТОГО" + new string(' ', 32 - 5 - 8) + "1 450,00", text);
        }

        [Fact]
        public void AgentScript_HasServerAndToken()
        {
            var script = PrintAgentScript.Build("http://192.168.1.10:5173", "abc123");
            Assert.Contains("$Base  = 'http://192.168.1.10:5173'", script);
            Assert.Contains("$Token = 'abc123'", script);
            Assert.Contains("/api/print-agent/jobs", script);
        }

        [Fact]
        public void AgentScript_ParamIsFirstStatement_AndKnowsDriverMode()
        {
            var script = PrintAgentScript.Build("https://cafe.example", "t");
            // В PowerShell param() должен идти первым — до него только комментарии и пустые строки
            var first = script.Split('\n').Select(l => l.Trim()).First(l => l.Length > 0 && !l.StartsWith('#'));
            Assert.StartsWith("param(", first);
            Assert.Contains("atolDriverBase", script);
            Assert.Contains("/api/v2/requests", script);
            Assert.Contains("AddIn.Fptr10", script);
        }
    
        [Fact]
        public async Task NetworkSend_DeliversBytesToPort9100Style()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var accept = Task.Run(async () =>
            {
                using var c = await listener.AcceptTcpClientAsync();
                using var ms = new MemoryStream();
                await c.GetStream().CopyToAsync(ms);
                return ms.ToArray();
            });
            await NetworkPrintWorker.SendAsync("127.0.0.1", port, new byte[] { 1, 2, 3 }, CancellationToken.None);
            Assert.Equal(new byte[] { 1, 2, 3 }, await accept);
            listener.Stop();
        }

        [Fact]
        public async Task NetworkSend_UnreachablePrinter_Throws()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop(); // порт закрыт — соединения не будет
            await Assert.ThrowsAnyAsync<Exception>(() => NetworkPrintWorker.SendAsync("127.0.0.1", port, new byte[] { 1 }, CancellationToken.None));
        }
    
        private sealed class FailIfCalled : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                throw new InvalidOperationException("Запрос не должен идти напрямую");
        }

        [Fact]
        public async Task DeviceRelay_SendsRequestThroughAgent_AndReturnsDeviceAnswer()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            var dbName = $"relay-{Guid.NewGuid()}";
            Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions.AddDbContext<Cafe.API.Data.ApplicationDbContext>(
                services, o => Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions.UseInMemoryDatabase(o, dbName));
            using var sp = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
            var scopes = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp);
            var settings = new PrintSettingsProvider(scopes);
            await settings.SaveAsync(new PrintSettings { RelayDevices = true, AgentToken = "t" }, null);
            PrintSettingsProvider.AgentSeenAt = DateTime.UtcNow;

            using var client = new HttpClient(new DeviceRelayHandler(scopes, settings) { InnerHandler = new FailIfCalled() });
            var call = client.PostAsync("http://127.0.0.1:16732/api/v2/requests", new StringContent("{\"uuid\":\"1\"}", Encoding.UTF8, "application/json"));

            // «Программа на кассовом ПК»: забирает запрос и возвращает ответ кассы
            Cafe.API.Models.PrintJob? job = null;
            for (var i = 0; i < 50 && job == null; i++)
            {
                await Task.Delay(50);
                using var scope = scopes.CreateScope();
                var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Cafe.API.Data.ApplicationDbContext>(scope.ServiceProvider);
                job = db.PrintJobs.FirstOrDefault(j => j.Kind == "http");
                if (job != null)
                {
                    var req = System.Text.Json.JsonSerializer.Deserialize<DeviceRelayHandler.RelayRequest>(job.Payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
                    Assert.Equal("POST", req.Method);
                    Assert.Equal("http://127.0.0.1:16732/api/v2/requests", req.Url);
                    Assert.Equal("{\"uuid\":\"1\"}", Encoding.UTF8.GetString(Convert.FromBase64String(req.Body!)));
                    job.Status = Cafe.API.Models.PrintJobStatuses.Done;
                    job.Result = System.Text.Json.JsonSerializer.Serialize(new DeviceRelayHandler.RelayResponse(201, "application/json", Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"ok\":1}"))),
                        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                    db.SaveChanges();
                }
            }
            Assert.NotNull(job);
            using var res = await call;
            Assert.Equal(System.Net.HttpStatusCode.Created, res.StatusCode);
            Assert.Equal("{\"ok\":1}", await res.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task DeviceRelay_Off_GoesDirect()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            var dbName = $"relay-off-{Guid.NewGuid()}";
            Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions.AddDbContext<Cafe.API.Data.ApplicationDbContext>(
                services, o => Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions.UseInMemoryDatabase(o, dbName));
            using var sp = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
            var scopes = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp);
            var settings = new PrintSettingsProvider(scopes);
            await settings.SaveAsync(new PrintSettings { RelayDevices = false, AgentToken = "t" }, null);
            using var client = new HttpClient(new DeviceRelayHandler(scopes, settings) { InnerHandler = new FailIfCalled() });
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("http://127.0.0.1:16732/x"));
        }
    }
}
