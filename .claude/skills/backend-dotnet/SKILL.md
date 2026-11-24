---
name: backend-dotnet
description: Project-specific backend patterns for the Cafe .NET 9 API (ASP.NET Core controllers, EF Core + PostgreSQL, permission-based authorization, domain exceptions, xUnit tests). Use when adding or changing controllers, services, EF models, migrations, permission checks, or backend tests in src/Cafe.API or src/Cafe.Core.
---

# Cafe Backend (.NET 9 / ASP.NET Core / EF Core / PostgreSQL)

Стек: .NET 9 (`net9.0`), ASP.NET Core MVC-контроллеры, EF Core 9 + Npgsql,
PostgreSQL, ASP.NET Identity (JWT + refresh), Serilog, Prometheus, xUnit.
Проекты: `src/Cafe.API` (web + бизнес-логика), `src/Cafe.Core`, `tests/Cafe.Tests`.

> For generic REST design rules see the global `api-design` skill; for schema
> and migration safety see `database-migrations` and `postgres-patterns`; for
> security review use `security-review`. This skill covers how this repo does it.

## Structure

| Слой | Где | Правило |
|------|-----|---------|
| Controllers | `src/Cafe.API/Controllers` | только HTTP: разбор запроса, вызов сервиса/DbContext, ответ |
| Services | `src/Cafe.API/Services/<Domain>` | бизнес-логика, транзакции, внешние API |
| Models | `src/Cafe.API/Models` | EF-сущности, доменные статусы-константы (`OrderStatuses`, `BanquetStatuses`) |
| Data | `src/Cafe.API/Data/ApplicationDbContext.cs` | DbSet'ы + `OnModelCreating` |
| Auth | `src/Cafe.API/Authorization` | `Permissions`, `PermissionService`, `HasPermissionAttribute` |
| Options | `src/Cafe.API/Options` | настройки из `appsettings` с `const string SectionName` |
| DTOs | `src/Cafe.API/DTOs/<Area>` | только для сложных контрактов; простые — вложенные классы в контроллере |

## Controller conventions

```csharp
[ApiController]
[Route("api/banquets")]                       // явный маршрут; [controller] только в старых контроллерах
[HasPermission(Permissions.Banquets)]         // права — на контроллере или на действии
public class BanquetsController : ControllerBase
{
    // зависимости через конструктор, readonly-поля, ILogger<TController>
    [HttpGet]
    [HasPermission(Permissions.Banquets, Permissions.Dashboard)]  // любое из прав
    public async Task<IActionResult> GetAll([FromQuery] DateOnly? from) { ... }
}
```

- **Права только через `HasPermission`**, не `[Authorize(Roles = "...")]`.
  Ключи — константы `Permissions.*`; новые ключи добавляются в
  `Permissions.All` (иначе `Keys`/`Defaults` их не увидят) и дублируются во
  фронтенде `frontend/src/auth/permissions.ts` (`P.*`).
- `[Authorize]` — только там, где нужен любой залогиненный пользователь
  («мои заказы», личный профиль).
- Ошибки: `BadRequest(new { message = "..." })` — **именно `message`**,
  фронтовый `apiError()` читает `message` (и `Message` для старых ручек).
  `NotFound`, `Forbid`, `StatusCode(500, ...)` аналогично.
- Необязательные параметры — `[FromQuery]`, тело — типизированный класс
  (вложенный `XxxRequest`), без `dynamic` и ручного парсинга JSON.
- Время: любые пользовательские даты — московские сутки через
  `ShiftService.BusinessDate(...)` / `OrdersController.PeriodUtc(...)`.
  В базу пишем UTC (`DateTimeKind.Utc`), наружу отдаём ISO.
- Действия с фильтрами по периоду всегда ограничены: `from/to` + дефолт
  (`defaultDays`), никаких `ToListAsync()` по всей таблице.

## EF Core / PostgreSQL

- Чтение без изменения — `.AsNoTracking()` (в репозитории это норма,
  ~53 файла).
- `.Include(...).ThenInclude(...)` для нужных навигаций; при нескольких
  `Include` — `.AsSplitQuery()`.
- Пагинация и лимиты — всегда (`Take(...)`), суммы — `decimal`.
- Деньги и веса: `decimal`/`int`, без `float`/`double`.
- Уникальные индексы, конфигурация связей — в `OnModelCreating`
  (`ApplicationDbContext`), не атрибутами в моделях.
- **Схема меняется только через миграцию**: `dotnet ef migrations add <Name>`
  в `src/Cafe.API`. CI проверяет `dotnet ef migrations has-pending-model-changes`
  и падает, если модель и snapshot расходятся. Держите `ApplicationDbContextModelSnapshot.cs`
  в коммите.
- `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` уже
  включён — `DateTime` с `Kind=Unspecified` трактуется как UTC. Не полагайтесь
  на локальное время.

## Services & domain exceptions

- Внешние системы и бизнес-правила живут в `Services/<Domain>/` и бросают
  доменное исключение своего домена (`FiscalException`, `ShiftException`,
  `BanquetException`, `InventoryException`, ...). Контроллер ловит их и
  переводит в `BadRequest/Conflict` с человеческим текстом; необработанное —
  `StatusCode(500)` + `_logger.LogError`.
- Регистрация в `Program.cs`: настройки-провайдеры (`*SettingsProvider`) —
  `AddSingleton`, сервисы с `DbContext` — `AddScoped`, HTTP-клиенты —
  `AddHttpClient(nameof(...))`.
- Фоновые задачи — `AddHostedService` + `JobMonitor` для экрана состояния.
- Никакого `Console.WriteLine` — только `ILogger<T>` (Serilog).

## Testing (xUnit)

- Тесты — `tests/Cafe.Tests/<Area>Tests.cs`, xUnit + `Microsoft.EntityFrameworkCore.InMemory`.
- Имена тестов — утверждение о поведении: `BusinessDate_IsMoscowDay`,
  `VoidRules_...`. Структура Arrange-Act-Assert.
- Минимальный набор на новую фичу: правило бизнес-логики (чистая функция или
  сервис на InMemory-базе) + права (`Permissions.Keys`/`Defaults` содержат
  ключ) + сериализация/контракт, если меняется форма ответа.
- Прогон: `dotnet test Cafe.sln`; сборка: `dotnet build Cafe.sln -c Release`.

## Checklist перед коммитом (backend)

- [ ] Права проверены через `HasPermission`, ключ добавлен в `Permissions.All`
      и в `P` на фронте
- [ ] Ошибки — `{ message }`, 400/403/404/409 по смыслу, 500 с логом
- [ ] Чтение — `AsNoTracking`, запросы ограничены периодом/лимитом
- [ ] Схема изменена только миграцией, snapshot обновлён
- [ ] Деньги — `decimal`, время — UTC + московские сутки
- [ ] Тест на новое правило, `dotnet test` зелёный
