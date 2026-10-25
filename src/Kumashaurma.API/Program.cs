using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;
using Kumashaurma.API.Services;
using Kumashaurma.API.Options;
using Kumashaurma.API.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

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
        Title = "Kumashaurma API",
        Version = "v1",
        Description = "API для сервиса доставки шаурмы"
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
    ?? "Host=localhost;Port=5433;Database=kumashaurma_dev;Username=devuser;Password=dev123";

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
        ValidIssuer = jwtSettings["Issuer"] ?? "Kumashaurma",
        ValidAudience = jwtSettings["Audience"] ?? "KumashaurmaClient",
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

// Онлайн-оплата ЮKassa (выключено, если не заданы YooKassa:ShopId и YooKassa:SecretKey)
builder.Services.Configure<YooKassaOptions>(builder.Configuration.GetSection(YooKassaOptions.SectionName));
builder.Services.AddHttpClient<YooKassaClient>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddHostedService<PaymentTimeoutService>();

// ЕГАИС через УТМ: настраивается в админке (Склад → ЕГАИС), appsettings Egais — значения по умолчанию
builder.Services.Configure<Kumashaurma.API.Services.Egais.EgaisOptions>(builder.Configuration.GetSection(Kumashaurma.API.Services.Egais.EgaisOptions.SectionName));
builder.Services.AddSingleton<Kumashaurma.API.Services.Egais.EgaisSettingsProvider>();
builder.Services.AddHttpClient<Kumashaurma.API.Services.Egais.UtmClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<Kumashaurma.API.Services.Egais.EgaisService>();
builder.Services.AddHostedService<Kumashaurma.API.Services.Egais.EgaisSyncWorker>();

// Честный знак (True API): подпись КЭП в браузере, сервер только передаёт запросы
builder.Services.AddSingleton<Kumashaurma.API.Services.Chz.ChzSettingsProvider>();
builder.Services.AddHttpClient<Kumashaurma.API.Services.Chz.ChzClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<Kumashaurma.API.Services.Chz.ChzService>();

// Импорт техкарт из iiko (iikoServer в локальной сети)
builder.Services.AddSingleton<Kumashaurma.API.Services.Iiko.IikoClient>();
builder.Services.AddScoped<Kumashaurma.API.Services.Iiko.IikoImportService>();

// Электронные накладные из СБИС (Saby): настраивается в админке (Склад → Накладные СБИС)
builder.Services.AddSingleton<Kumashaurma.API.Services.Edo.SbisSettingsProvider>();
builder.Services.AddHttpClient<Kumashaurma.API.Services.Edo.SbisClient>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<Kumashaurma.API.Services.Edo.EdoService>();
builder.Services.AddHostedService<Kumashaurma.API.Services.Edo.SbisSyncWorker>();

// Импорт меню с kumashaurma.ru
builder.Services.AddHttpClient<MenuImportService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; KumashaurmaMenuImport/1.0)");
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
            "http://localhost:3001",
            "http://localhost:5173",
            "https://kumashaurma-frontend.vercel.app"
        )
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    });
});

// ========== App ==========

var app = builder.Build();

// Глобальный обработчик исключений — логирует реальную ошибку
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ UNHANDLED EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        Console.WriteLine($"   StackTrace: {ex.StackTrace}");

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        // Детали ошибки показываем только в разработке, наружу — общее сообщение
        if (app.Environment.IsDevelopment())
            await context.Response.WriteAsJsonAsync(new { Message = ex.Message, Detail = ex.InnerException?.Message });
        else
            await context.Response.WriteAsJsonAsync(new { Message = "Внутренняя ошибка сервера" });
    }
});

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

// Seed database & initialize roles
try
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Console.WriteLine("Database initializing...");
        DbInitializer.Initialize(dbContext);

        // Roles must be created AFTER migrations
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
        foreach (var role in AppRoles.AllRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<int>(role));
                Console.WriteLine($"Created role: {role}");
            }
        }

        // Названия встроенных ролей и права по умолчанию (один раз)
        await RoleSeeder.SeedAsync(roleManager);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error initializing database: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"Inner error: {ex.InnerException.Message}");
}

app.Run();
