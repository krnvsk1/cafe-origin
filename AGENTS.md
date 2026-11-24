# Cafe — гайд для Codex

Сервис для кафе: меню и доставка, касса, официант, кухня, склад, банкеты,
фискализация, ЕГАИС/ЧЗ/Меркурий, финансы, HACCP.

## Стек

| Часть | Технологии |
|-------|-----------|
| Бэкенд | .NET 9, ASP.NET Core MVC, EF Core 9 + Npgsql, PostgreSQL, Identity + JWT, Serilog, Prometheus |
| Фронтенд | React 18, TypeScript, Vite 6, MUI 6, TanStack Query 5, Zustand 5, axios |
| Тесты | xUnit + EF InMemory (`tests/Cafe.Tests`, ~142 теста) |
| CI | `.github/workflows/build.yml`: restore → build → test → проверка EF snapshot |

## Карта репозитория

```
src/Cafe.API/Controllers    HTTP-контроллеры (43 шт.)
src/Cafe.API/Services       бизнес-логика, интеграции (Fiscal, Egais, Chz, Mercury, Edo, Iiko, Printing, Finance)
src/Cafe.API/Models         EF-сущности и константы статусов
src/Cafe.API/Data           ApplicationDbContext, DbInitializer
src/Cafe.API/Authorization  Permissions, PermissionService, HasPermission
src/Cafe.API/Options        настройки из appsettings (*Options)
src/Cafe.API/Migrations     EF-миграции
frontend/src/api            axios-клиент + React Query хуки по доменам
frontend/src/pages          экраны
frontend/src/components     переиспользуемые блоки
frontend/src/store          Zustand (auth, cart, orderFlow)
frontend/src/auth           P.* и useCan()
tests/Cafe.Tests            xUnit-тесты
```

## Команды

```
dotnet build Cafe.sln -c Release
dotnet test Cafe.sln
cd src/Cafe.API && dotnet ef migrations add <Name>
cd frontend && npm run lint
cd frontend && npm run build
cd frontend && npm run dev
```

## Скиллы

**Проектные (`.Codex/skills/`)** — читать при работе в соответствующих папках:

- `backend-dotnet` — контроллеры, сервисы, EF Core, права, миграции, xUnit.
  Обязателен перед изменением `src/Cafe.API`, `src/Cafe.Core`, `tests/Cafe.Tests`.
- `frontend-react` — хуки `src/api`, страницы, MUI-паттерны, права, ошибки.
  Обязателен перед изменением `frontend/`.

**Глобальные (ECC, `~/.Codex/skills/`)** — подключаются автоматически:

- `api-design` — REST-контракты, статус-коды, пагинация
- `backend-patterns` — общие серверные паттерны (Node-ориентирован, для .NET
  сверяться с `backend-dotnet`)
- `frontend-patterns` — React-композиция, состояние, производительность
- `coding-standards`, `security-review`, `tdd-workflow`
- `postgres-patterns`, `database-migrations`
- `e2e-testing`, `deployment-patterns`, `docker-patterns`, `search-first`

## Жёсткие правила проекта

1. **Права.** Новый эндпоинт — `[HasPermission(Permissions.X)]`, ключ в
   `Permissions.All` и в `P` (`frontend/src/auth/permissions.ts`).
   `[Authorize(Roles=...)]` не использовать.
2. **Ошибки API** — `new { message = "..." }` (фронт читает `message`).
3. **Схема БД** — только миграцией; `ApplicationDbContextModelSnapshot.cs`
   коммитится вместе с моделью (CI проверяет расхождение).
4. **Время** — в БД UTC, пользовательские сутки по Москве
   (`ShiftService.BusinessDate`, `OrdersController.PeriodUtc`).
5. **Иммутабельность** — не мутировать объекты и ответы сервера; новые копии.
6. **Секреты** — только `appsettings.Development.json` / user-secrets / env,
   в код не хардкодить.
7. **Файлы ≤ 800 строк**, функции ≤ 50 строк, без глубокого nesting.
8. После изменения кода — прогнать сборку/тесты/линт затронутой части.
