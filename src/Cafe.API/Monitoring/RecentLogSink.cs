using System.Threading.Channels;
using Serilog.Core;
using Serilog.Events;

namespace Cafe.API.Monitoring
{
    /// <summary>Запись журнала для экрана «Состояние системы»</summary>
    public record LogEntry(DateTime At, string Level, string Message, string? Exception, string? Source, string? RequestId, string Template);

    /// <summary>
    /// Последние предупреждения и ошибки — в памяти (для экрана «Состояние системы»),
    /// ошибки ещё и в очередь тревог (внешний мониторинг).
    /// </summary>
    public class RecentLogSink : ILogEventSink
    {
        private const int Capacity = 300;
        private readonly LinkedList<LogEntry> _entries = new();
        private readonly object _lock = new();

        public Channel<LogEntry> Alerts { get; } = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        public void Emit(LogEvent e)
        {
            if (e.Level < LogEventLevel.Warning) return;
            string? Prop(string name) => e.Properties.TryGetValue(name, out var v) ? v.ToString().Trim('"') : null;
            var message = e.RenderMessage();
            if (message.Length > 2000) message = message[..2000] + "…";
            var entry = new LogEntry(e.Timestamp.UtcDateTime, e.Level.ToString(), message,
                e.Exception == null ? null : Trim(e.Exception.ToString(), 4000), Prop("SourceContext"), Prop("RequestId"), e.MessageTemplate.Text);
            lock (_lock)
            {
                _entries.AddFirst(entry);
                while (_entries.Count > Capacity) _entries.RemoveLast();
            }
            if (e.Level >= LogEventLevel.Error) Alerts.Writer.TryWrite(entry);
        }

        public IReadOnlyList<LogEntry> Snapshot(int take)
        {
            lock (_lock) return _entries.Take(take).ToList();
        }

        private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
    }
}
