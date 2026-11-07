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
            // Логин Telegram — сохраняем и обновляем при каждом входе (его могут сменить или убрать)
            var username = Trim(tg.Username?.TrimStart('@'), 64);
            if (user.TelegramUsername != username)
            {
                user.TelegramUsername = username;
                user.UpdatedAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
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

        // POST: api/telegram/setup — вебхук и бот «только для входа» (без команд и кнопки мини-приложения); при запуске сервер делает это сам
        [HttpPost("setup")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> Setup()
        {
            if (!_options.Enabled) return BadRequest(new { Message = "Не задан Telegram:BotToken" });
            if (string.IsNullOrWhiteSpace(_options.PublicApiUrl)) return BadRequest(new { Message = "Не задан Telegram:PublicApiUrl" });
            if (string.IsNullOrWhiteSpace(_options.WebhookSecret)) return BadRequest(new { Message = "Не задан Telegram:WebhookSecret" });
            var (webhook, commands, menuButton) = await TelegramSetup.RunAsync(_telegram, _options, HttpContext.RequestAborted);
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

                // Бот нужен только для входа на сайт: /start login_<код>; остальное — подсказка
                if (command == "/start")
                    await HandleStartAsync(chatId, fromId, chatType, argument, fromUser);
                else if (chatType == "private")
                    await _telegram.SendMessageAsync(chatId, $"Этот бот нужен только для входа на сайт «{await BrandNameAsync()}»: нажмите там «Войти через Telegram».");
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
                    ? $"✅ Вход подтверждён — вернитесь на сайт «{await BrandNameAsync()}»."
                    : "Ссылка для входа устарела. Нажмите «Войти через Telegram» на сайте ещё раз.");
                return;
            }

            if (chatType == "private")
                await _telegram.SendMessageAsync(chatId, $"Этот бот нужен только для входа на сайт «{await BrandNameAsync()}»: нажмите там «Войти через Telegram».");
        }
    }
}
