using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services.Fiscal
{
    /// <summary>
    /// Раз в минуту спрашивает кассу о чеках «отправлен, ответа нет» (обрыв связи, касса печатала долго)
    /// и записывает итог: пробит — с номером ФД, касса не знает задания — «не пробит», можно повторить.
    /// Сам чеки не пробивает.
    /// </summary>
    public class FiscalReconcileWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<FiscalReconcileWorker> _logger;

        public FiscalReconcileWorker(IServiceScopeFactory scopes, ILogger<FiscalReconcileWorker> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "Проверка отправленных чеков не удалась"); }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var fiscal = scope.ServiceProvider.GetRequiredService<FiscalService>();
            if (!await fiscal.EnabledAsync()) return;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow;
            var stuck = await db.FiscalReceipts
                .Where(r => r.Status == FiscalStatuses.Processing && r.CreatedAt < now.AddSeconds(-45) && r.CreatedAt > now.AddDays(-7))
                .OrderBy(r => r.Id).Take(20).ToListAsync(ct);
            foreach (var r in stuck)
            {
                if (await fiscal.ReconcileAsync(r, ct))
                    _logger.LogInformation("Чек #{Id} (заказ #{OrderId}, счёт {CheckId}) после проверки: {Status}", r.Id, r.OrderId, r.CheckId, r.Status);
            }
        }
    }
}
