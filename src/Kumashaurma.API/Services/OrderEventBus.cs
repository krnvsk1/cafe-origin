using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Kumashaurma.API.Services
{
    /// <summary>
    /// Событие по заказу: создан / сменился статус / обновлён
    /// </summary>
    public record OrderEvent(
        string Type,          // "created" | "status" | "updated" | "deleted"
        int OrderId,
        int? UserId,
        string Status,
        string? PreviousStatus,
        DateTime OccurredAt);

    public interface IOrderEventBus
    {
        void Publish(OrderEvent evt);
        OrderEventSubscription Subscribe();
    }

    /// <summary>
    /// Подписка на поток событий. Dispose — отписка.
    /// </summary>
    public sealed class OrderEventSubscription : IDisposable
    {
        private readonly Action _onDispose;
        public ChannelReader<OrderEvent> Reader { get; }

        internal OrderEventSubscription(ChannelReader<OrderEvent> reader, Action onDispose)
        {
            Reader = reader;
            _onDispose = onDispose;
        }

        public void Dispose() => _onDispose();
    }

    /// <summary>
    /// Простая внутрипроцессная шина событий заказов (для SSE, Telegram и т.д.).
    /// Для нескольких экземпляров API её можно заменить на Redis pub/sub.
    /// </summary>
    public class OrderEventBus : IOrderEventBus
    {
        private readonly ConcurrentDictionary<Guid, Channel<OrderEvent>> _subscribers = new();
        private readonly ILogger<OrderEventBus> _logger;

        public OrderEventBus(ILogger<OrderEventBus> logger)
        {
            _logger = logger;
        }

        public void Publish(OrderEvent evt)
        {
            foreach (var channel in _subscribers.Values)
            {
                // Медленный подписчик не блокирует остальных: старые события вытесняются
                channel.Writer.TryWrite(evt);
            }
            _logger.LogDebug("📣 Событие заказа {Type} #{OrderId} → {Status} ({Count} подписчиков)",
                evt.Type, evt.OrderId, evt.Status, _subscribers.Count);
        }

        public OrderEventSubscription Subscribe()
        {
            var id = Guid.NewGuid();
            var channel = Channel.CreateBounded<OrderEvent>(new BoundedChannelOptions(200)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
            _subscribers[id] = channel;
            return new OrderEventSubscription(channel.Reader, () =>
            {
                if (_subscribers.TryRemove(id, out var removed))
                    removed.Writer.TryComplete();
            });
        }
    }
}
