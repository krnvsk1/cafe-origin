using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Kumashaurma.API.Data;
using Kumashaurma.API.Options;

namespace Kumashaurma.API.Services
{
    /// <summary>
    /// Фоновая рассылка в Telegram по событиям заказов:
    /// клиенту — смена статуса его заказа, персоналу — новые заказы.
    /// </summary>
    public class TelegramNotifier : BackgroundService
    {
        private static readonly Dictionary<string, string> CustomerTexts = new()
        {
            [OrderStatuses.Accepted] = "👍 Кухня приняла ваш заказ",
            [OrderStatuses.Preparing] = "👨‍🍳 Начали готовить ваш заказ",
            [OrderStatuses.Ready] = "✅ Заказ готов",
            [OrderStatuses.OnTheWay] = "🛵 Курьер уже в пути",
            [OrderStatuses.Delivered] = "🎉 Заказ доставлен. Приятного аппетита!",
            [OrderStatuses.Completed] = "🎉 Заказ выдан. Приятного аппетита!",
            [OrderStatuses.Cancelled] = "❌ Заказ отменён"
        };

        private readonly IOrderEventBus _bus;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TelegramOptions _options;
        private readonly RestaurantOptions _restaurant;
        private readonly ILogger<TelegramNotifier> _logger;

        public TelegramNotifier(
            IOrderEventBus bus,
            IServiceScopeFactory scopeFactory,
            IOptions<TelegramOptions> options,
            IOptions<RestaurantOptions> restaurant,
            ILogger<TelegramNotifier> logger)
        {
            _restaurant = restaurant.Value;
            _bus = bus;
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Telegram: BotToken не задан — уведомления выключены");
                return;
            }

            using var subscription = _bus.Subscribe();
            try
            {
                await foreach (var evt in subscription.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await HandleAsync(evt, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Telegram: ошибка обработки события заказа #{OrderId}", evt.OrderId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // остановка приложения
            }
        }

        private async Task HandleAsync(OrderEvent evt, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var telegram = scope.ServiceProvider.GetRequiredService<TelegramApi>();

            if (evt.Type == "created" && _options.StaffChatId.HasValue)
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var order = await db.Orders.AsNoTracking()
                    .Include(o => o.OrderItems).ThenInclude(i => i.SelectedAddons)
                    .FirstOrDefaultAsync(o => o.Id == evt.OrderId, ct);
                if (order != null)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"🆕 <b>Заказ #{order.Id}</b> · {TelegramApi.Escape(order.DeliveryType)} · <b>{order.Total:0.##} ₽</b>");
                    if (order.ScheduledFor.HasValue)
                    {
                        var local = TimeZoneInfo.ConvertTimeFromUtc(
                            DateTime.SpecifyKind(order.ScheduledFor.Value.ToUniversalTime(), DateTimeKind.Utc),
                            _restaurant.GetTimeZone());
                        sb.AppendLine($"⏰ Ко времени: {local:dd.MM HH:mm}");
                    }
                    sb.AppendLine($"👤 {TelegramApi.Escape(order.CustomerName)} {TelegramApi.Escape(order.Phone)}");
                    if (order.DeliveryType == "Доставка")
                        sb.AppendLine($"📍 {TelegramApi.Escape(order.Address)}");
                    foreach (var item in order.OrderItems)
                    {
                        var addons = item.SelectedAddons.Any()
                            ? " (+ " + string.Join(", ", item.SelectedAddons.Select(a => a.AddonName)) + ")"
                            : string.Empty;
                        sb.AppendLine($"• {item.Quantity} × {TelegramApi.Escape(item.Name)}{TelegramApi.Escape(addons)}");
                    }
                    if (!string.IsNullOrWhiteSpace(order.Notes))
                        sb.AppendLine($"💬 {TelegramApi.Escape(order.Notes)}");

                    await telegram.SendMessageAsync(_options.StaffChatId.Value, sb.ToString(), null, ct);
                }
                return;
            }

            if (evt.Type == "status" && evt.UserId.HasValue && CustomerTexts.TryGetValue(evt.Status, out var text))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await db.Users.AsNoTracking()
                    .Where(u => u.Id == evt.UserId.Value)
                    .Select(u => new { u.TelegramUserId, u.TelegramNotifications })
                    .FirstOrDefaultAsync(ct);

                if (user?.TelegramUserId is long chatId && user.TelegramNotifications)
                {
                    await telegram.SendMessageAsync(
                        chatId,
                        $"{text}\nЗаказ <b>#{evt.OrderId}</b>",
                        telegram.WebAppKeyboard("Открыть заказ", $"/order/{evt.OrderId}/success"),
                        ct);
                }
            }
        }
    }
}
