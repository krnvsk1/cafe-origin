using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Cafe.API.Data;
using Cafe.API.Models;
using Cafe.API.Services;
using Cafe.API.Options;
using Cafe.API.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading.RateLimiting;
using Prometheus;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Cafe.API.Monitoring;

var builder = WebApplication.CreateBuilder(args);

// ========== Логи и мониторинг ==========
// Консоль (в продакшене — JSON для сборщиков логов), файлы по дням в папке logs (JSON, хранятся 14 дней),
// последние предупреждения и ошибки — в памяти для экрана «Состояние системы» и тревог в Telegram.
var monitoring = builder.Configuration.GetSection("Monitoring").Get<MonitoringOptions>() ?? new MonitoringOptions();
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection("Monitoring"));
var recentLogs = new RecentLogSink();
builder.Services.AddSingleton(recentLogs);
builder.Services.AddSingleton<JobMonitor>();
builder.Services.AddSingleton<EndpointStats>();
builder.Host.UseSerilog((ctx, services, log) =>
{
    log.ReadFrom.Configuration(ctx.Configuration)
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "Cafe.API")
        .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
        .WriteTo.Sink(recentLogs);
    if (ctx.HostingEnvironment.IsDevelopment())
        log.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {RequestId}{NewLine}{Exception}");
    else
        log.WriteTo.Console(new CompactJsonFormatter());
    log.WriteTo.File(new CompactJsonFormatter(), Path.Combine(monitoring.LogDirectory, "cafe-.json"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: Math.Max(1, monitoring.RetainDays),
        fileSizeLimitBytes: 100 * 1024 * 1024, rollOnFileSizeLimit: true, shared: true);
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database", HealthStatus.Unhealthy, new[] { "ready" })
    .AddCheck<DiskSpaceHealthCheck>("disk", tags: new[] { "ready" })
    .AddCheck<JobsHealthCheck>("jobs")
    .AddCheck<EndpointsHealthCheck>("endpoints");
builder.Services.AddSingleton<AlertSettingsProvider>();
builder.Services.AddSingleton<StatusReport>();
builder.Services.AddHostedService<AlertService>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Ошибки из браузера: не больше 20 в минуту с одного адреса
    o.AddPolicy("client-log", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// PostgreSQL: разрешить DateTime с Kind=Unspecified (преобразует в UTC)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

if (!builder.Environment.IsDevelopment())
{
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    builder.WebHost.UseUrls($"http://*:{port}");
}

// ========== Services ==========

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();

// Swagger с поддержкой JWT
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Cafe API",
        Version = "v1",
        Description = "API сервиса кафе: меню, заказы, касса, кухня, склад"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Enter 'Bearer' [space] and then your token",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5433;Database=cafe_dev;Username=devuser;Password=dev123";

// Печатаем, к какой базе подключаемся (без пароля) — чтобы не перепутать локальный Postgres и Docker
{
    var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
    Console.WriteLine($"🗄  База данных: {csb.Host}:{csb.Port}/{csb.Database} (пользователь {csb.Username})");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString)
        // Не роняем старт приложения, если снапшот модели чуть разошёлся с кодом —
        // только пишем предупреждение в лог (проверка: dotnet ef migrations has-pending-model-changes)
        .ConfigureWarnings(w => w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

// Identity
builder.Services.AddIdentity<AppUser, IdentityRole<int>>(options =>
{
    // Password settings - для SMS-авторизации пароли не обязательны
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;

    // User settings
    options.User.RequireUniqueEmail = false;

    // SignIn settings
    options.SignIn.RequireConfirmedEmail = false;
    options.SignIn.RequireConfirmedPhoneNumber = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// JWT Settings
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];
if (string.IsNullOrEmpty(secretKey))
{
    if (builder.Environment.IsDevelopment())
    {
        Console.WriteLine("WARNING: JWT SecretKey not configured. Using development-only fallback. DO NOT use in production!");
        secretKey = "DevOnlySecretKey_2024_DoNotUseInProduction_MustBeAtLeast64CharactersLong!!";
    }
    else
    {
        throw new InvalidOperationException("JWT SecretKey is not configured. Set JwtSettings:SecretKey in configuration.");
    }
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"] ?? "Cafe",
        ValidAudience = jwtSettings["Audience"] ?? "CafeClient",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };

    // EventSource (SSE) не умеет отправлять заголовки — для /api/events берём токен из ?access_token=
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/api/events"))
                context.Token = accessToken;
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Права ролей: каталог разрешений в коде, раздача — в админке (role_claims)
builder.Services.AddSingleton<IPermissionService, PermissionService>();
builder.Services.AddScoped<IClaimsTransformation, PermissionClaimsTransformation>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

// Настройки заведения
builder.Services.Configure<RestaurantOptions>(builder.Configuration.GetSection(RestaurantOptions.SectionName));
builder.Services.Configure<ReferralOptions>(builder.Configuration.GetSection(ReferralOptions.SectionName));

// Custom Services
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IVerificationService, VerificationService>();

// Живые обновления заказов
builder.Services.AddSingleton<IOrderEventBus, OrderEventBus>();
builder.Services.AddScoped<IOrderStatusService, OrderStatusService>();

// Склад и техкарты
builder.Services.AddScoped<IInventoryService, InventoryService>();

// Telegram: бот, уведомления, вход из мини-приложения (выключено, если не задан Telegram:BotToken)
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.AddHttpClient<TelegramApi>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<TelegramAuth>();
builder.Services.AddHostedService<TelegramNotifier>();
// Без публичного адреса (локально) команды бота забираем опросом
builder.Services.AddHostedService<TelegramPollingService>();

// Онлайн-оплата ЮKassa (выключено, если не заданы YooKassa:ShopId и YooKassa:SecretKey)
builder.Services.Configure<YooKassaOptions>(builder.Configuration.GetSection(YooKassaOptions.SectionName));
builder.Services.AddHttpClient<YooKassaClient>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddHostedService<PaymentTimeoutService>();

// ЕГАИС через УТМ: настраивается в админке (Склад → ЕГАИС), appsettings Egais — значения по умолчанию
builder.Services.Configure<Cafe.API.Services.Egais.EgaisOptions>(builder.Configuration.GetSection(Cafe.API.Services.Egais.EgaisOptions.SectionName));
builder.Services.AddSingleton<Cafe.API.Services.Egais.EgaisSettingsProvider>();
builder.Services.AddHttpClient<Cafe.API.Services.Egais.UtmClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<Cafe.API.Services.Egais.EgaisService>();
builder.Services.AddHostedService<Cafe.API.Services.Egais.EgaisSyncWorker>();

// Честный знак (True API): подпись КЭП в браузере, сервер только передаёт запросы
builder.Services.AddSingleton<Cafe.API.Services.Chz.ChzSettingsProvider>();
builder.Services.AddHttpClient<Cafe.API.Services.Chz.ChzClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<Cafe.API.Services.Chz.ChzService>();

// ФГИС «Меркурий» (Ветис.API): входящие ВСД и гашение — настраивается в «Настройке интеграций»
builder.Services.AddSingleton<Cafe.API.Services.Mercury.MercurySettingsProvider>();
builder.Services.AddHttpClient<Cafe.API.Services.Mercury.VetisClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<Cafe.API.Services.Mercury.MercuryService>();
builder.Services.AddHostedService<Cafe.API.Services.Mercury.MercurySyncWorker>();

// Журналы ХАССП и СанПиН
builder.Services.AddScoped<Cafe.API.Services.Haccp.HaccpService>();

// Импорт техкарт из iiko (iikoServer в локальной сети)
builder.Services.AddSingleton<Cafe.API.Services.Iiko.IikoClient>();
builder.Services.AddScoped<Cafe.API.Services.Iiko.IikoImportService>();

// Электронные накладные из СБИС (Saby): настраивается в админке (Склад → Накладные СБИС)
builder.Services.AddSingleton<Cafe.API.Services.Edo.SbisSettingsProvider>();
builder.Services.AddHttpClient<Cafe.API.Services.Edo.SbisClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<Cafe.API.Services.Edo.EdoService>();
builder.Services.AddHostedService<Cafe.API.Services.Edo.SbisSyncWorker>();

// Название заведения (сайт, оплата, Telegram-бот)
builder.Services.AddSingleton<Cafe.API.Services.BrandSettingsProvider>();

// Отмена блюд из чека официантом (с подтверждением кухни)
builder.Services.AddScoped<Cafe.API.Services.VoidService>();
builder.Services.AddScoped<Cafe.API.Services.ShiftService>();
builder.Services.AddSingleton<Cafe.API.Services.WebPushService>();
builder.Services.AddScoped<Cafe.API.Services.WaiterReadyNotifier>();

// Онлайн-касса АТОЛ (АТОЛ Web Server в локальной сети): чеки по заказам кассы
builder.Services.AddSingleton<Cafe.API.Services.Fiscal.FiscalSettingsProvider>();
builder.Services.AddHttpClient<Cafe.API.Services.Fiscal.AtolClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<Cafe.API.Services.Fiscal.FiscalService>();

// Импорт меню с сайта заведения (FoodPicasso)
builder.Services.AddHttpClient<MenuImportService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; CafeMenuImport/1.0)");
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        // Адреса сайта заведения — в настройках Cors:AllowedOrigins (appsettings или переменные окружения)
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(new[] { "http://localhost:3001", "http://localhost:5173" }.Concat(origins).Distinct().ToArray())
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    });
});

// ========== App ==========

var app = builder.Build();

// Номер запроса: приходит от клиента в X-Request-Id или создаётся; есть в каждой строке лога и в ответе
app.Use(async (context, next) =>
{
    var incoming = context.Request.Headers["X-Request-Id"].ToString();
    // Свой номер запроса ASP.NET уже кладёт в журнал (RequestId) — берём его же, чтобы все строки одного запроса совпадали
    var requestId = incoming.Length is > 0 and <= 64 && incoming.All(c => char.IsLetterOrDigit(c) || c == '-')
        ? incoming : context.TraceIdentifier;
    context.TraceIdentifier = requestId;
    context.Response.Headers["X-Request-Id"] = requestId;
    using (Serilog.Context.LogContext.PushProperty("RequestId", requestId))
        await next();
});

// Стабильность ручек: шаблон маршрута, статус и время ответа каждого запроса — для вкладки «Ручки» и тревог
app.Use(async (context, next) =>
{
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    try { await next(); }
    finally
    {
        // Ошибку внутри превращает в ответ 500 обработчик ниже — сюда приходит уже готовый статус
        var stats = context.RequestServices.GetRequiredService<EndpointStats>();
        stats.Record(context, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
});

// Журнал запросов: метод, путь (без параметров — в них бывают токены), статус, время, пользователь
app.UseSerilogRequestLogging(o =>
{
    o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} → {StatusCode} за {Elapsed:0} мс";
    o.GetLevel = (ctx, elapsed, ex) =>
        ex != null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
        // Живые подключения (они и должны длиться долго), проверки состояния и картинки — только в подробном режиме
        : ctx.Request.Path.StartsWithSegments("/api/events") || ctx.Request.Path.StartsWithSegments("/health")
          || ctx.Request.Path.StartsWithSegments("/metrics") || ctx.Request.Path.StartsWithSegments("/uploads") ? LogEventLevel.Debug
        : elapsed > 3000 ? LogEventLevel.Warning
        : LogEventLevel.Information;
    o.EnrichDiagnosticContext = (diag, ctx) =>
    {
        var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId != null) diag.Set("UserId", userId);
        if (ctx.Connection.RemoteIpAddress is { } ip) diag.Set("ClientIp", ip.ToString());
    };
});

// Глобальный обработчик исключений: ошибка — в журнал (и в Telegram), клиенту — номер запроса
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogError(ex, "Необработанная ошибка: {Method} {Path}", context.Request.Method, context.Request.Path.Value);
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        // Детали ошибки показываем только в разработке, наружу — общее сообщение и номер запроса
        if (app.Environment.IsDevelopment())
            await context.Response.WriteAsJsonAsync(new { Message = ex.Message, Detail = ex.InnerException?.Message, RequestId = context.TraceIdentifier });
        else
            await context.Response.WriteAsJsonAsync(new { Message = $"Внутренняя ошибка сервера (запрос {context.TraceIdentifier})", RequestId = context.TraceIdentifier });
    }
});

app.UseRateLimiter();

// Метрики Prometheus (/metrics): с токеном Monitoring:MetricsToken или только с этого компьютера
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/metrics"), branch => branch.Use(async (ctx, next) =>
{
    var token = monitoring.MetricsToken;
    var allowed = string.IsNullOrEmpty(token)
        ? ctx.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip)
        : ctx.Request.Headers.Authorization.ToString() == $"Bearer {token}";
    if (!allowed) { ctx.Response.StatusCode = 404; return; }
    await next();
}));
app.UseHttpMetrics();

// CORS
app.UseCors("AllowFrontend");

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Проверки состояния для внешнего мониторинга (UptimeRobot, Docker, балансировщик):
// /health/live — процесс жив; /health/ready — база и диск в порядке (иначе 503)
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    ResultStatusCodes = { [HealthStatus.Healthy] = 200, [HealthStatus.Degraded] = 200, [HealthStatus.Unhealthy] = 503 },
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString(), description = e.Value.Description })
        });
    }
});
app.MapMetrics();

// Seed database & initialize roles
try
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        app.Logger.LogInformation("Инициализация базы данных…");
        DbInitializer.Initialize(dbContext);

        // Roles must be created AFTER migrations
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
        foreach (var role in AppRoles.AllRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<int>(role));
                app.Logger.LogInformation("Создана роль {Role}", role);
            }
        }

        // Названия встроенных ролей и права по умолчанию (один раз)
        await RoleSeeder.SeedAsync(roleManager);
    }
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Не удалось подготовить базу данных");
}

app.Logger.LogInformation("Cafe API {Version} запущен ({Environment})", Cafe.API.Controllers.SystemController.Version, app.Environment.EnvironmentName);
try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
