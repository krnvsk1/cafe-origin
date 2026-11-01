using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cafe.API.Monitoring
{
    /// <summary>Сводка для команды /status в чате тревог: состояние, фоновые задачи, последние ошибки</summary>
    public class StatusReport
    {
        private readonly HealthCheckService _health;
        private readonly JobMonitor _jobs;
        private readonly RecentLogSink _logs;
        private readonly EndpointStats _endpoints;

        public StatusReport(HealthCheckService health, JobMonitor jobs, RecentLogSink logs, EndpointStats endpoints)
        {
            _endpoints = endpoints;
            _health = health;
            _jobs = jobs;
            _logs = logs;
        }

        private static string Icon(HealthStatus s) => s switch
        {
            HealthStatus.Healthy => "🟢",
            HealthStatus.Degraded => "🟡",
            _ => "🔴"
        };

        private static string Ago(DateTime utc)
        {
            var m = (int)(DateTime.UtcNow - utc).TotalMinutes;
            return m < 1 ? "только что" : m < 60 ? $"{m} мин назад" : m < 1440 ? $"{m / 60} ч назад" : $"{m / 1440} дн назад";
        }

        private static string H(string s) => WebUtility.HtmlEncode(s);

        public async Task<string> BuildHtmlAsync(string version, DateTime startedAt, CancellationToken ct = default)
        {
            var report = await _health.CheckHealthAsync(ct);
            var up = DateTime.UtcNow - startedAt;
            var uptime = up.TotalHours < 1 ? $"{(int)up.TotalMinutes} мин" : up.TotalDays < 1 ? $"{(int)up.TotalHours} ч" : $"{(int)up.TotalDays} дн {up.Hours} ч";
            var memory = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;

            var lines = new List<string>
            {
                $"{Icon(report.Status)} <b>Сервер {(report.Status == HealthStatus.Healthy ? "в порядке" : report.Status == HealthStatus.Degraded ? "работает, есть проблемы" : "есть сбои")}</b>",
                $"работает {uptime} · память {memory} МБ · версия {H(version)}",
                ""
            };
            foreach (var (name, entry) in report.Entries)
                lines.Add($"{Icon(entry.Status)} {H(AlertService.Title(name))}: {H(entry.Description ?? entry.Exception?.Message ?? entry.Status.ToString())}");

            var jobs = _jobs.All();
            if (jobs.Count > 0)
            {
                lines.Add("");
                lines.Add("<b>Фоновые задачи</b>");
                foreach (var j in jobs)
                {
                    var ok = j.FailuresInRow == 0;
                    lines.Add($"{(ok ? "🟢" : j.FailuresInRow >= 3 ? "🔴" : "🟡")} {H(j.Name)}: " +
                              (ok ? $"успешно {(j.LastSuccess is { } s ? Ago(s) : "—")}" : $"ошибок подряд {j.FailuresInRow} — {H(j.LastError ?? "")}"));
                }
            }

            var hour = _endpoints.Summary(TimeSpan.FromHours(1));
            if (hour.Count > 0)
            {
                lines.Add("");
                lines.Add($"<b>Ручки за час</b>: запросов {hour.Sum(e => e.Count)}, ошибок сервера {hour.Sum(e => e.Errors5xx)}");
                foreach (var e in hour.Where(e => e.Errors5xx > 0).OrderByDescending(e => e.Errors5xx).Take(3))
                    lines.Add($"🔴 {H(e.Method)} {H(e.Route)} — {e.Errors5xx} из {e.Count} ({e.ErrorRate:0.#}%)");
                foreach (var e in hour.Where(e => e.Errors5xx == 0 && e.P95Ms >= 1000).OrderByDescending(e => e.P95Ms).Take(2))
                    lines.Add($"🐢 {H(e.Method)} {H(e.Route)} — 95% быстрее {e.P95Ms} мс");
            }

            var errors = _logs.Snapshot(300).Where(l => l.Level is "Error" or "Fatal").ToList();
            var lastDay = errors.Count(e => e.At > DateTime.UtcNow.AddDays(-1));
            lines.Add("");
            lines.Add(errors.Count == 0 ? "Ошибок с момента запуска нет" : $"<b>Ошибок за сутки: {lastDay}</b>, последние:");
            foreach (var e in errors.Take(3))
            {
                var msg = e.Message.Length > 200 ? e.Message[..200] + "…" : e.Message;
                lines.Add($"• {Ago(e.At)}: {H(msg)}");
            }
            return string.Join('\n', lines);
        }
    }
}
