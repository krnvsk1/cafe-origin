using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Cafe.API.Data;
using Cafe.API.Models;
using Cafe.API.Services;
using Cafe.API.Services.Fiscal;
using Xunit;

namespace Cafe.Tests
{
    public class FiscalSafetyTests
    {
        /// <summary>Касса-заглушка: POST можно «оборвать», GET отвечает заданным результатом</summary>
        private class FakeAtol : HttpMessageHandler
        {
            public int Posts;
            public bool DropPost;
            public string? Result;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (request.Method == HttpMethod.Post)
                {
                    Posts++;
                    if (DropPost) throw new HttpRequestException("соединение оборвалось");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
                }
                return Task.FromResult(Result == null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Result, Encoding.UTF8, "application/json") });
            }
        }

        private const string Done = """{"results":[{"status":"ready","errorCode":0,"result":{"fiscalParams":{"fiscalDocumentNumber":42,"shiftNumber":3}}}]}""";

        private static (ServiceProvider sp, FakeAtol atol) Setup()
        {
            var atol = new FakeAtol();
            var name = $"fiscal-{Guid.NewGuid()}";
            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            services.AddSingleton<FiscalSettingsProvider>();
            services.AddSingleton<BrandSettingsProvider>();
            services.AddSingleton(new AtolClient(new HttpClient(atol)));
            services.AddScoped(sp => new FiscalService(sp.GetRequiredService<ApplicationDbContext>(), sp.GetRequiredService<AtolClient>(),
                sp.GetRequiredService<FiscalSettingsProvider>(), NullLogger<FiscalService>.Instance, sp.GetRequiredService<BrandSettingsProvider>()));
            return (services.BuildServiceProvider(), atol);
        }

        private static async Task<int> SeedOrderAsync(ServiceProvider sp, int? checkId = null)
        {
            await sp.GetRequiredService<FiscalSettingsProvider>().SaveAsync(new FiscalOptions { Enabled = true }, null);
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = new Order { CustomerName = "Касса", Total = 300, PaymentMethod = "cash", Status = OrderStatuses.Completed, CheckId = checkId };
            order.OrderItems.Add(new OrderItem { Name = "Борщ", Price = 300, Quantity = 1 });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return order.Id;
        }

        [Fact]
        public async Task LostConnection_KeepsReceiptPending_AndRetryDoesNotPrintTwice()
        {
            var (sp, atol) = Setup();
            var orderId = await SeedOrderAsync(sp);
            atol.DropPost = true;

            using (var scope = sp.CreateScope())
            {
                var r = await scope.ServiceProvider.GetRequiredService<FiscalService>().PrintAsync(orderId, FiscalKinds.Sell, null);
                Assert.Equal(FiscalStatuses.Processing, r.Status);   // не «ошибка»: чек мог пробиться
            }

            // Касса снова на связи и знает задание — чек был пробит
            atol.DropPost = false;
            atol.Result = Done;
            using (var scope = sp.CreateScope())
            {
                var r = await scope.ServiceProvider.GetRequiredService<FiscalService>().PrintAsync(orderId, FiscalKinds.Sell, null);
                Assert.Equal(FiscalStatuses.Done, r.Status);
                Assert.Equal(42L, r.FiscalDocumentNumber);
            }
            Assert.Equal(1, atol.Posts);   // второго задания на кассу не было
        }

        [Fact]
        public async Task Reconcile_UnknownTask_BecomesErrorSoItCanBeRetried()
        {
            var (sp, atol) = Setup();
            var orderId = await SeedOrderAsync(sp);
            atol.DropPost = true;
            using (var scope = sp.CreateScope())
                await scope.ServiceProvider.GetRequiredService<FiscalService>().PrintAsync(orderId, FiscalKinds.Sell, null);

            atol.DropPost = false;   // касса на связи, но задания не знает (404) — до неё не дошло
            using var s2 = sp.CreateScope();
            var db = s2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var receipt = await db.FiscalReceipts.SingleAsync();
            Assert.True(await s2.ServiceProvider.GetRequiredService<FiscalService>().ReconcileAsync(receipt, default));
            Assert.Equal(FiscalStatuses.Error, receipt.Status);
            Assert.Single(await s2.ServiceProvider.GetRequiredService<FiscalService>().UnfinishedAsync(DateTime.UtcNow.AddDays(-1)));
        }

        [Fact]
        public async Task StaffCheck_IsNeverSentToAtol()
        {
            var (sp, atol) = Setup();
            await SeedOrderAsync(sp, checkId: 9);
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TableChecks.Add(new TableCheck { Id = 9, TableId = 1, Status = CheckStatuses.Closed, PaymentMethod = CheckPayments.Staff, Total = 300, StaffUserId = 5, StaffReason = "Питание персонала" });
            await db.SaveChangesAsync();
            var ex = await Assert.ThrowsAsync<FiscalException>(() =>
                scope.ServiceProvider.GetRequiredService<FiscalService>().PrintCheckAsync(9, FiscalKinds.Sell, null));
            Assert.Contains("на сотрудника", ex.Message);
            Assert.Equal(0, atol.Posts);
            Assert.Empty(db.FiscalReceipts);
        }

        [Fact]
        public async Task OrderFromTableCheck_IsNotFiscalizedSeparately()
        {
            var (sp, _) = Setup();
            var orderId = await SeedOrderAsync(sp, checkId: 7);
            using var scope = sp.CreateScope();
            var ex = await Assert.ThrowsAsync<FiscalException>(() =>
                scope.ServiceProvider.GetRequiredService<FiscalService>().PrintAsync(orderId, FiscalKinds.Return, null));
            Assert.Contains("по счёту", ex.Message);
        }

        [Fact]
        public void ReturnOf_MirrorsSellReceipt()
        {
            var o = new FiscalOptions();
            var sell = FiscalTasks.Receipt(FiscalKinds.Sell, o, new List<ReceiptLine> { new("Борщ", 300, 1, 300, false) }, "card", "Иванова", null);
            var ret = FiscalTasks.ReturnOf(sell.ToJsonString(), o, "Петров");
            Assert.NotNull(ret);
            Assert.Equal("sellReturn", ret!["type"]!.GetValue<string>());
            Assert.Equal("Петров", ret["operator"]!["name"]!.GetValue<string>());
            Assert.Equal(300m, ret["total"]!.GetValue<decimal>());
            Assert.Equal("electronically", ret["payments"]![0]!["type"]!.GetValue<string>());
            Assert.Null(FiscalTasks.ReturnOf("not json", o, "x"));
        }

        [Fact]
        public void Unfinished_OnlyGroupsWithoutDoneReceipt()
        {
            var list = new List<FiscalReceipt>
            {
                new() { Id = 1, OrderId = 1, CheckId = 10, Kind = FiscalKinds.Sell, Status = FiscalStatuses.Error },
                new() { Id = 2, OrderId = 1, CheckId = 10, Kind = FiscalKinds.Sell, Status = FiscalStatuses.Done },
                new() { Id = 3, OrderId = 5, Kind = FiscalKinds.Sell, Status = FiscalStatuses.Error },
                new() { Id = 4, OrderId = 5, Kind = FiscalKinds.Sell, Status = FiscalStatuses.Processing },
                new() { Id = 5, OrderId = 1, CheckId = 10, Kind = FiscalKinds.Return, Status = FiscalStatuses.Error },
            };
            var u = FiscalService.Unfinished(list);
            Assert.Equal(new[] { 4, 5 }, u.Select(r => r.Id).OrderBy(x => x).ToArray());
        }
    }
}
