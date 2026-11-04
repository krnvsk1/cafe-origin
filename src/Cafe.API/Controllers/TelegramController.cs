using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Cafe.API.Data;
using Cafe.API.DTOs.Auth;
using Cafe.API.Extensions;
using Cafe.API.Models;
using Cafe.API.Options;
using Cafe.API.Services;
using Cafe.API.Authorization;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Telegram: привязка аккаунта, вебхук бота, вход из мини-приложения
    /// </summary>
    [ApiController]
    [Route("api/telegram")]
    public class TelegramController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly TelegramApi _telegram;
        private readonly TelegramAuth _auth;
        private readonly TelegramOptions _options;
        private readonly UserManager<AppUser> _userManager;
        private readonly ITokenService _tokenService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TelegramController> _logger;
        private readonly IServiceProvider _services;

        public TelegramController(
            ApplicationDbContext context,
            TelegramApi telegram,
            TelegramAuth auth,
            IOptions<TelegramOptions> options,
            UserManager<AppUser> userManager,
            ITokenService tokenService,
            IConfiguration configuration,
            ILogger<TelegramController> logger,
            IServiceProvider services)
        {
            _services = services;
            _context = context;
            _telegram = telegram;
            _auth = auth;
            _options = options.Value;
            _userManager = userManager;
            _tokenService = tokenService;
            _configuration = configuration;
            _logger = logger;
        }

        public class NotificationsRequest { public bool Enabled { get; set; } }
        public class LoginRequest { public string InitData { get; set; } = string.Empty; }

        // ==================== Привязка аккаунта ====================

        // GET: api/telegram/status
        [HttpGet("status")]
        [Authorize]
        public async Task<IActionResult> Status()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var user = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId.Value)
                .Select(u => new { u.TelegramUserId, u.TelegramNotifications })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                Enabled = _options.Enabled && !string.IsNullOrWhiteSpace(_options.BotUsername),
                _options.BotUsername,
                Linked = user?.TelegramUserId != null,
                Notifications = user?.TelegramNotifications ?? true
            });
        }

        // POST: api/telegram/link — ссылка t.me/<bot>?start=<token> для привязки
        [HttpPost("link")]
        [Authorize]
        public IActionResult CreateLink()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BotUsername))
                return BadRequest(new { Message = "Telegram-бот не настроен" });

            var token = _auth.CreateLinkToken(userId.Value);
            return Ok(new { Url = $"https://t.me/{_options.BotUsername}?start={token}" });
        }

        // DELETE: api/telegram/link — отвязать
        [HttpDelete("link")]
        [Authorize]
        public async Task<IActionResult> Unlink()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            await _context.Users
                .Where(u => u.Id == userId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TelegramUserId, (long?)null));
            return Ok(new { Linked = false });
        }

        // PUT: api/telegram/notifications — включить/выключить уведомления
        [HttpPut("notifications")]
        [Authorize]
        public async Task<IActionResult> SetNotifications([FromBody] NotificationsRequest request)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            await _context.Users
                .Where(u => u.Id == userId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TelegramNotifications, request.Enabled));
            return Ok(new { Notifications = request.Enabled });
        }

        // ==================== Вход на сайте через Telegram ====================

        // GET: api/telegram/web-login — доступен ли вход через Telegram (бот подключён)
        [HttpGet("web-login")]
        public IActionResult WebLoginInfo() =>
            Ok(new { Enabled = _options.Enabled && !string.IsNullOrWhiteSpace(_options.BotUsername) });

        // POST: api/telegram/web-login — код входа и ссылка t.me/<бот>?start=login_<код>
        [HttpPost("web-login")]
        public IActionResult StartWebLogin()
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BotUsername))
                return BadRequest(new { Message = "Вход через Telegram не настроен" });
            var code = _services.GetRequiredService<TelegramLoginSessions>().Create();
            return Ok(new { Code = code, Url = $"https://t.me/{_options.BotUsername}?start={TelegramLoginSessions.Prefix}{code}" });
        }

        // GET: api/telegram/web-login/{code} — опрос: ждём подтверждения в боте; подтвердили — вход (аккаунт создаётся при первом входе)
        [HttpGet("web-login/{code}")]
        public async Task<IActionResult> PollWebLogin(string code)
        {
            var sessions = _services.GetRequiredService<TelegramLoginSessions>();
            var session = sessions.Get(code);
            if (session == null) return StatusCode(StatusCodes.Status410Gone, new { Message = "Время входа истекло — начните заново" });
            if (session.User == null) return Ok(new { Pending = true });
            var tg = sessions.Take(code);
            if (tg == null) return StatusCode(StatusCodes.Status410Gone, new { Message = "Вход уже выполнен — начните заново" });

            var (user, error) = await FindOrCreateAsync(tg);
            if (user == null) return BadRequest(new AuthResponseDto { Success = false, Message = error });
            return Ok(await IssueTokensAsync(user, "telegram-web"));
        }

        /// <summary>Аккаунт по Telegram; нет — создаём (имя из Telegram, телефон гость укажет при заказе)</summary>
        private async Task<(AppUser? User, string? Error)> FindOrCreateAsync(TelegramAuth.TelegramUser tg)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.TelegramUserId == tg.Id);
            if (user == null)
            {
                user = new AppUser
                {
                    UserName = $"tg{tg.Id}",
                    FirstName = Trim(tg.FirstName, 100),
                    LastName = Trim(tg.LastName, 100),
                    TelegramUserId = tg.Id,
                    TelegramNotifications = true,
                    CreatedAt = DateTime.UtcNow,
                };
                var created = await _userManager.CreateAsync(user);
                if (!created.Succeeded) return (null, string.Join(", ", created.Errors.Select(e => e.Description)));
                await _userManager.AddToRoleAsync(user, AppRoles.User);
                _logger.LogInformation("Новый пользователь через Telegram: {TelegramId}", tg.Id);
            }
            return (user, null);
        }

        private static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];

        private async Task<AuthResponseDto> IssueTokensAsync(AppUser user, string source)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var accessToken = _tokenService.GenerateAccessToken(user, roles);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id, HttpContext.Connection.RemoteIpAddress?.ToString() ?? source);
            _context.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");
            return new AuthResponseDto
            {
                Success = true,
                Message = "Login successful",
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes),
                User = await UserDtoFactory.CreateAsync(user, roles, HttpContext.RequestServices.GetRequiredService<IPermissionService>())
            };
        }

        // ==================== Вход из мини-приложения ====================

        // POST: api/telegram/login — вход по initData, если Telegram привязан к аккаунту
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequest request)
        {
            var tgUser = _auth.ValidateInitData(request.InitData);
            if (tgUser == null)
                return BadRequest(new AuthResponseDto { Success = false, Message = "Неверные данные Telegram" });

            // Нет аккаунта с этим Telegram — создаём: войти можно сразу через Telegram, без телефона
            var (user, error) = await FindOrCreateAsync(tgUser);
            if (user == null)
                return Ok(new AuthResponseDto { Success = false, Message = error ?? "Не удалось войти через Telegram" });

            var roles = await _userManager.GetRolesAsync(user);
            var accessToken = _tokenService.GenerateAccessToken(user, roles);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "telegram");
            _context.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();

            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");
            return Ok(new AuthResponseDto
            {
                Success = true,
                Message = "Login successful",
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes),
                User = await UserDtoFactory.CreateAsync(user, roles, HttpContext.RequestServices.GetRequiredService<IPermissionService>())
            });
        }

        // ==================== Настройка бота ====================

        // POST: api/telegram/setup — установить вебхук, кнопку меню и команды (один раз после деплоя)
        [HttpPost("setup")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> Setup()
        {
            if (!_options.Enabled) return BadRequest(new { Message = "Не задан Telegram:BotToken" });
            if (string.IsNullOrWhiteSpace(_options.PublicApiUrl)) return BadRequest(new { Message = "Не задан Telegram:PublicApiUrl" });
            if (string.IsNullOrWhiteSpace(_options.WebhookSecret)) return BadRequest(new { Message = "Не задан Telegram:WebhookSecret" });

            var webhook = await _telegram.CallAsync("setWebhook", new
            {
                Url = _options.PublicApiUrl.TrimEnd('/') + "/api/telegram/webhook",
                SecretToken = _options.WebhookSecret,
                AllowedUpdates = new[] { "message" },
                DropPendingUpdates = true
            });

            var commands = await _telegram.CallAsync("setMyCommands", new
            {
                Commands = new[]
                {
                    new { Command = "menu", Description = "Открыть меню и заказать" },
                    new { Command = "orders", Description = "Мои последние заказы" },
                    new { Command = "help", Description = "Помощь" }
                }
            });

            var menuButton = true;
            if (!string.IsNullOrWhiteSpace(_options.WebAppUrl))
            {
                menuButton = await _telegram.CallAsync("setChatMenuButton", new
                {
                    MenuButton = new { Type = "web_app", Text = "Меню", WebApp = new { Url = _options.WebAppUrl } }
                });
            }

            return Ok(new { Webhook = webhook, Commands = commands, MenuButton = menuButton });
        }

        // POST: api/telegram/webhook — входящие сообщения бота
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook([FromBody] JsonElement update)
        {
            // Проверяем, что запрос пришёл от Telegram
            var secret = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
            if (string.IsNullOrEmpty(_options.WebhookSecret)
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(_options.WebhookSecret)))
                return Unauthorized();

            await ProcessUpdateAsync(update);
            return Ok();
        }

        /// <summary>Обработка входящего сообщения бота — из вебхука или из опроса (когда сервер без публичного адреса)</summary>
        [NonAction]
        public async Task ProcessUpdateAsync(JsonElement update)
        {
            try
            {
                if (!update.TryGetProperty("message", out var message)) return;
                if (!message.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl)) return;
                var chatId = chatIdEl.GetInt64();
                var chatType = chat.TryGetProperty("type", out var t) ? t.GetString() : "private";
                var fromId = message.TryGetProperty("from", out var from) && from.TryGetProperty("id", out var fid) ? fid.GetInt64() : chatId;
                TelegramAuth.TelegramUser? fromUser = message.TryGetProperty("from", out var f2) ? new TelegramAuth.TelegramUser(fromId,
                    f2.TryGetProperty("first_name", out var fn) ? fn.GetString() : null,
                    f2.TryGetProperty("last_name", out var ln) ? ln.GetString() : null,
                    f2.TryGetProperty("username", out var un) ? un.GetString() : null) : null;
                var text = message.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty;

                var command = text.Split(' ', 2)[0].Split('@')[0].ToLowerInvariant();
                var argument = text.Contains(' ') ? text.Split(' ', 2)[1].Trim() : string.Empty;

                switch (command)
                {
                    case "/start":
                        await HandleStartAsync(chatId, fromId, chatType, argument, fromUser);
                        break;
                    case "/menu":
                        await _telegram.SendMessageAsync(chatId, "Выбирайте и заказывайте прямо в Telegram 👇",
                            _telegram.WebAppKeyboard("🍽️ Открыть меню"));
                        break;
                    case "/orders":
                        await HandleOrdersAsync(chatId, fromId);
                        break;
                    case "/status":
                        await HandleStatusAsync(chatId);
                        break;
                    case "/id":
                        // Удобно, чтобы узнать ID группы: для заказов (Telegram:StaffChatId) и отдельной — для тревог
                        await _telegram.SendMessageAsync(chatId, $"ID этого чата: <code>{chatId}</code>");
                        break;
                    default:
                        await _telegram.SendMessageAsync(chatId,
                            $"Я бот «{await BrandNameAsync()}» 🍽️\n\n/menu — открыть меню\n/orders — мои заказы\n\nУведомления о заказах приходят сюда, если вы привязали Telegram в профиле на сайте.",
                            _telegram.WebAppKeyboard("🍽️ Открыть меню"));
                        break;
                }
            }
            catch (Exception ex)
            {
                // Telegram повторяет запрос при ошибке — всегда отвечаем 200
                _logger.LogWarning(ex, "Telegram: ошибка обработки сообщения");
            }
        }

        private async Task<string> BrandNameAsync() =>
            (await _services.GetRequiredService<BrandSettingsProvider>().GetAsync()).DisplayName;

        private async Task HandleStartAsync(long chatId, long fromId, string? chatType, string argument, TelegramAuth.TelegramUser? from = null)
        {
            // Вход на сайте: /start login_<код>
            if (argument.StartsWith(TelegramLoginSessions.Prefix, StringComparison.Ordinal) && chatType == "private")
            {
                var ok = _services.GetRequiredService<TelegramLoginSessions>()
                    .Approve(argument[TelegramLoginSessions.Prefix.Length..], from ?? new TelegramAuth.TelegramUser(fromId, null, null, null));
                await _telegram.SendMessageAsync(chatId, ok
                    ? $"✅ Вход подтверждён — вернитесь на сайт «{await BrandNameAsync()}». Сюда будут приходить статусы ваших заказов."
                    : "Ссылка для входа устарела. Нажмите «Войти через Telegram» на сайте ещё раз.");
                return;
            }

            if (!string.IsNullOrEmpty(argument) && chatType == "private")
            {
                var userId = _auth.ValidateLinkToken(argument);
                if (userId == null)
                {
                    await _telegram.SendMessageAsync(chatId, "Ссылка устарела. Нажмите «Подключить Telegram» на сайте ещё раз.");
                    return;
                }

                // Этот Telegram мог быть привязан к другому аккаунту — отвязываем
                await _context.Users
                    .Where(u => u.TelegramUserId == fromId && u.Id != userId.Value)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.TelegramUserId, (long?)null));

                var updated = await _context.Users
                    .Where(u => u.Id == userId.Value)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.TelegramUserId, (long?)fromId)
                        .SetProperty(u => u.TelegramNotifications, true));

                await _telegram.SendMessageAsync(chatId,
                    updated > 0
                        ? "✅ Telegram подключён! Сюда будут приходить статусы ваших заказов."
                        : "Не удалось найти аккаунт. Попробуйте ещё раз с сайта.",
                    _telegram.WebAppKeyboard("🍽️ Открыть меню"));
                return;
            }

            await _telegram.SendMessageAsync(chatId,
                $"Привет! Это «{await BrandNameAsync()}» 🍽️\nЗаказывайте прямо здесь — нажмите кнопку ниже.",
                _telegram.WebAppKeyboard("🍽️ Открыть меню"));
        }

        private async Task HandleOrdersAsync(long chatId, long fromId)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramUserId == fromId);
            if (user == null)
            {
                await _telegram.SendMessageAsync(chatId, "Telegram ещё не привязан к аккаунту. Откройте профиль на сайте → «Подключить Telegram».");
                return;
            }

            var orders = await _context.Orders.AsNoTracking()
                .Where(o => o.UserId == user.Id)
                .OrderByDescending(o => o.CreatedAt)
                .Take(3)
                .Select(o => new { o.Id, o.Status, o.Total, o.CreatedAt })
                .ToListAsync();

            if (orders.Count == 0)
            {
                await _telegram.SendMessageAsync(chatId, "У вас пока нет заказов.", _telegram.WebAppKeyboard("🍽️ Открыть меню"));
                return;
            }

            var lines = orders.Select(o => $"#{o.Id} — <b>{TelegramApi.Escape(o.Status)}</b> · {o.Total:0.##} ₽");
            await _telegram.SendMessageAsync(chatId, "Ваши последние заказы:\n" + string.Join('\n', lines),
                _telegram.WebAppKeyboard("Мои заказы", "/orders"));
        }

        /// <summary>/status — мониторинг перенесён во внешний сервис, здесь только подсказка</summary>
        private async Task HandleStatusAsync(long chatId) =>
            await _telegram.SendMessageAsync(chatId, "Состояние сервера — в «Состоянии системы» на сайте; тревоги приходят из внешнего мониторинга.");
    }
}
