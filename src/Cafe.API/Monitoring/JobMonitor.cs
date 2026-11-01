using System.Collections.Concurrent;

namespace Cafe.API.Monitoring
{
    /// <summary>Состояние фоновой задачи: когда последний раз прошла успешно, последняя ошибка и сколько ошибок подряд</summary>
    public record JobState(string Name, DateTime? LastSuccess, DateTime? LastFailure, string? LastError, int FailuresInRow);

    /// <summary>Фоновые задачи (ЕГАИС, Меркурий, СБИС, оплаты) отмечают здесь каждый проход</summary>
    public class JobMonitor
    {
        private readonly ConcurrentDictionary<string, JobState> _jobs = new();

        public void Success(string name) =>
            _jobs.AddOrUpdate(name, n => new JobState(n, DateTime.UtcNow, null, null, 0),
                (_, s) => s with { LastSuccess = DateTime.UtcNow, FailuresInRow = 0 });

        public void Failure(string name, string error) =>
            _jobs.AddOrUpdate(name, n => new JobState(n, null, DateTime.UtcNow, error, 1),
                (_, s) => s with { LastFailure = DateTime.UtcNow, LastError = error, FailuresInRow = s.FailuresInRow + 1 });

        public IReadOnlyList<JobState> All() => _jobs.Values.OrderBy(j => j.Name).ToList();
    }
}
