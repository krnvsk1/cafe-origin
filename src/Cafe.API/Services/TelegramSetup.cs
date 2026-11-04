using Cafe.API.Options;
using Microsoft.Extensions.Options;

namespace Cafe.API.Services
{
    /// <summary>
    /// Бот только для входа на сайт: вебхук, без команд и без кнопки мини-приложения.
    /// Telegram в кафе без VPN не работает, поэтому заказы и уведомления в него не шлём.
    /// </summary>
    public static class TelegramSetup
    {
        public static async Task<(bool Webhook, bool Commands, bool MenuButton)> RunAsync(TelegramApi telegram, TelegramOptions options, CancellationToken ct = default)
        {
            var webhook = await telegram.CallAsync("setWebhook", new
            {
                Url = options.PublicApiUrl!.TrimEnd('/') + "/api/telegram/webhook",
                SecretToken = options.WebhookSecret,
                AllowedUpdates = new[] { "message" },
                DropPendingUpdates = false
            }, ct);
            var commands = await telegram.CallAsync("deleteMyCommands", new { }, ct);
            var menuButton = await telegram.CallAsync("setChatMenuButton", new { MenuButton = new { Type = "default" } }, ct);
            return (webhook, commands, menuButton);
        }
    }

    /// <summary>На сервере с публичным адресом ставит вебхук бота сам после запуска (несколько попыток)</summary>
    public class TelegramStartup : BackgroundService
    {
        private readonly TelegramOptions _options;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<TelegramStartup> _logger;

        public TelegramStartup(IOptions<TelegramOptions> options, IServiceScopeFactory scopes, ILogger<TelegramStartup> logger)
        {
            _options = options.Value;
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.PublicApiUrl) || string.IsNullOrWhiteSpace(_options.WebhookSecret)) return;
            for (var attempt = 1; attempt <= 5 && !stoppingToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? 10 : 60), stoppingToken);
                    using var scope = _scopes.CreateScope();
                    var (webhook, _, _) = await TelegramSetup.RunAsync(scope.ServiceProvider.GetRequiredService<TelegramApi>(), _options, stoppingToken);
                    if (webhook) { _logger.LogInformation("Telegram: вебхук для входа через бота установлен"); return; }
                    _logger.LogWarning("Telegram не принял вебхук (попытка {Attempt})", attempt);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { _logger.LogWarning("Telegram недоступен (попытка {Attempt}): {Message}", attempt, ex.Message); }
            }
        }
    }
}
