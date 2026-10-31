using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Cafe.API.Data;
using Cafe.API.Options;

namespace Cafe.API.Services
{
    /// <summary>
    /// Раз в несколько минут сверяет зависшие онлайн-платежи с ЮKassa
    /// и отменяет заказы, которые не оплатили за PaymentTimeoutMinutes.
    /// </summary>
    public class PaymentTimeoutService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly YooKassaOptions _options;
        private readonly ILogger<PaymentTimeoutService> _logger;

        public PaymentTimeoutService(IServiceScopeFactory scopeFactory, IOptions<YooKassaOptions> options, ILogger<PaymentTimeoutService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled) return;

            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Проверка неоплаченных заказов не удалась");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task CheckAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();

            var pending = await db.Orders
                .Where(o => o.PaymentMethod == "online"
                    && o.PaymentStatus != "paid" && o.PaymentStatus != "refunded"
                    && o.Status == OrderStatuses.New)
                .OrderBy(o => o.CreatedAt)
                .Take(50)
                .ToListAsync(ct);

            var deadline = DateTime.UtcNow.AddMinutes(-_options.PaymentTimeoutMinutes);
            foreach (var order in pending)
            {
                if (order.CreatedAt.ToUniversalTime() < deadline)
                    await payments.CancelIfUnpaidAsync(order, ct);
                else
                    await payments.SyncAsync(order, ct); // подстраховка, если вебхук не дошёл
            }
        }
    }
}
