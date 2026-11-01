using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Cafe.API.Monitoring
{
    /// <summary>Место на диске: логи, фото блюд и база растут</summary>
    public class DiskSpaceHealthCheck : IHealthCheck
    {
        private readonly IWebHostEnvironment _env;
        private readonly MonitoringOptions _options;

        public DiskSpaceHealthCheck(IWebHostEnvironment env, IOptions<MonitoringOptions> options)
        {
            _env = env;
            _options = options.Value;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_env.ContentRootPath)) ?? "/");
            var freeMb = drive.AvailableFreeSpace / 1024 / 1024;
            var data = new Dictionary<string, object> { ["freeMb"] = freeMb, ["totalMb"] = drive.TotalSize / 1024 / 1024 };
            return Task.FromResult(freeMb < _options.MinFreeDiskMb
                ? HealthCheckResult.Degraded($"На диске осталось {freeMb} МБ", data: data)
                : HealthCheckResult.Healthy($"Свободно {freeMb} МБ", data));
        }
    }

    /// <summary>Фоновые задачи: три ошибки подряд — «есть проблемы»</summary>
    public class JobsHealthCheck : IHealthCheck
    {
        private readonly JobMonitor _jobs;
        public JobsHealthCheck(JobMonitor jobs) => _jobs = jobs;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            var failing = _jobs.All().Where(j => j.FailuresInRow >= 3).ToList();
            return Task.FromResult(failing.Count == 0
                ? HealthCheckResult.Healthy("Фоновые задачи работают")
                : HealthCheckResult.Degraded("Сбои: " + string.Join("; ", failing.Select(j => $"{j.Name} — {j.LastError}"))));
        }
    }
}
