using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services.Printing
{
    /// <summary>
    /// Печать по сети: принтер (RONGTA с LAN) подключён к роутеру кафе, сервер шлёт ему ESC/POS на порт 9100.
    /// Работает, когда сервер в той же сети, что и принтер.
    /// </summary>
    public class NetworkPrintWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly PrintSettingsProvider _settings;
        private readonly ILogger<NetworkPrintWorker> _logger;

        public NetworkPrintWorker(IServiceScopeFactory scopes, PrintSettingsProvider settings, ILogger<NetworkPrintWorker> logger)
        {
            _scopes = scopes;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var s = await _settings.GetAsync();
                    if (s.Enabled && s.Mode == "network" && !string.IsNullOrWhiteSpace(s.Host))
                        await ProcessAsync(s, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "Печать по сети: ошибка очереди"); }

                try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task ProcessAsync(PrintSettings s, CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var jobs = await db.PrintJobs.Where(j => j.Status == PrintJobStatuses.Pending && j.Kind != "http").OrderBy(j => j.Id).Take(5).ToListAsync(ct);
            foreach (var job in jobs)
            {
                job.Status = PrintJobStatuses.Taken;
                job.TakenAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                try
                {
                    await SendAsync(s.Host!.Trim(), s.Port, Convert.FromBase64String(job.Payload), ct);
                    job.Status = PrintJobStatuses.Done;
                    job.Error = null;
                }
                catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
                {
                    job.Status = PrintJobStatuses.Error;
                    job.Error = $"Принтер {s.Host}:{s.Port} не отвечает — проверьте, что он включён и в сети";
                    _logger.LogWarning("Печать по сети: {Host}:{Port} — {Error}", s.Host, s.Port, ex.Message);
                }
                job.DoneAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        public static async Task SendAsync(string host, int port, byte[] data, CancellationToken ct)
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await client.ConnectAsync(host, port, timeout.Token);
                await using var stream = client.GetStream();
                await stream.WriteAsync(data, timeout.Token);
                await stream.FlushAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Нет ответа за 5 секунд");
            }
        }
    }
}
