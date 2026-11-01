# Cafe — сервис для кафе
Сервис для кафе: меню и доставка, касса, официант, кухня, склад

## 🚀 Быстрый старт

### Предварительные требования

- .NET 9.0 SDK
- Node.js 18+ / Bun
- PostgreSQL

### Запуск Backend

```bash
cd src/Cafe.API

# Настройка подключения к БД в appsettings.json
# Запуск миграций
dotnet ef database update

# Запуск сервера
dotnet run
```

Backend будет доступен на `http://localhost:5000`

### Запуск Frontend

```bash
cd frontend

# Установка зависимостей
npm install
# или
bun install

# Запуск dev сервера
npm run dev
```

Frontend будет доступен на `http://localhost:5173`

### Запуск AI Service (ИИ-ассистент)

```bash
cd ai-service

# Установка зависимостей
npm install
# или
bun install

# Запуск
npm run dev
```

AI Service будет доступен на `http://localhost:3001`

## 🤖 ИИ-ассистент

В проект интегрирован ИИ-ассистент для помощи в разработке:

- **Генерация кода** — React компоненты, C# контроллеры, TypeScript типы
- **Вопросы по проекту** — архитектура, структура, паттерны
- **Отладка** — помощь в поиске и исправлении багов

### Использование

На любой странице нажмите на кнопку 🤖 в правом нижнем углу, чтобы открыть чат с ИИ-ассистентом.

## 📁 Структура проекта

```
cafe/
├── src/
│   ├── Cafe.API/       # Backend API (ASP.NET Core)
│   │   ├── Controllers/       # API контроллеры
│   │   ├── Models/           # Модели данных
│   │   ├── Services/         # Бизнес-логика
│   │   └── Data/             # DbContext, миграции
│   └── Cafe.Core/     # Общая логика
├── frontend/                  # Frontend (React + TypeScript)
│   ├── src/
│   │   ├── components/       # React компоненты
│   │   ├── pages/           # Страницы
│   │   ├── api/             # API клиент
│   │   ├── store/           # Zustand stores
│   │   └── types/           # TypeScript типы
│   └── public/
├── ai-service/               # AI микросервис (Node.js)
└── tests/                    # Тесты
```

## 🛠 Технологии

| Компонент | Технологии |
|-----------|------------|
| Backend | ASP.NET Core 9.0, Entity Framework, PostgreSQL |
| Frontend | React 18, TypeScript, Vite, Material UI |
| Auth | ASP.NET Identity, JWT, SMS-верификация |
| State | Zustand, React Query |
| AI | Node.js, z-ai-web-dev-sdk |

## 📋 Функционал

- ✅ Каталог меню с категориями и изображениями
- ✅ Система добавок к блюдам
- ✅ Корзина и оформление заказов
- ✅ Авторизация с SMS-верификацией
- ✅ Роли пользователей (admin, manager, user)
- ✅ Админ-панель с дашбордом
- ✅ Управление товарами и заказами
- ✅ Тёмная/светлая тема
- ✅ ИИ-ассистент для разработки

## 🔧 Конфигурация

### Backend (appsettings.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=cafe_dev;Username=devuser;Password=dev123"
  },
  "JwtSettings": {
    "SecretKey": "your-secret-key",
    "Issuer": "Cafe",
    "Audience": "CafeClient"
  }
}
```

### Frontend (.env)

```env
VITE_API_URL=http://localhost:5199
VITE_MEDIA_URL=http://localhost:5199
```

`VITE_MEDIA_URL` используется для построения абсолютных URL к изображениям товаров. Если backend и frontend работают с одного origin и картинки доступны по относительным путям, переменную можно не задавать.

## 📈 Мониторинг и логи

**Логи.** Сервер пишет журнал через Serilog:
- в консоль (в продакшене — JSON, удобно для сборщиков логов);
- в файлы `logs/cafe-ГГГГММДД.json` рядом с приложением: по файлу на день, хранятся 14 дней (`Monitoring:RetainDays`), файл не больше 100 МБ;
- каждый запрос — одной строкой: метод, путь, статус, время, пользователь; у каждого запроса есть номер (`X-Request-Id`), он же приходит клиенту в ответе на ошибку — по нему ошибку легко найти в журнале.

Ошибки из браузера (сайт, касса, кухня, официант) тоже попадают в журнал сервера.

**Состояние системы** — раздел «Состояние системы» в панели (право «Настройка интеграций»): проверки, фоновые задачи (ЕГАИС, Меркурий, СБИС, оплаты), последние предупреждения и ошибки.

**Проверки для внешнего мониторинга:**
- `GET /health/live` — процесс жив (200);
- `GET /health/ready` — база и диск в порядке (200), иначе 503. Его удобно поставить в UptimeRobot или Healthchecks.io с проверкой раз в минуту.

**Тревоги в Telegram** — в отдельный чат, не в чат заказов. Создайте группу, добавьте в неё бота, напишите `/id` и вставьте номер в «Состояние системы» → «Тревоги в Telegram» (там же кнопка «Проверить»; чат заказов сервер не примет). Запасной вариант — `Monitoring:TelegramChatId` в настройках сервера. Придут:
- ошибки сервера (одинаковые — не чаще раза в 15 минут, `Monitoring:AlertThrottleMinutes`);
- «база недоступна», «мало места на диске», «фоновая задача падает три раза подряд» — и сообщение, когда всё снова в порядке.

**С телефона:**
- `/status` в чате тревог — бот присылает сводку: сервер и база, место на диске, фоновые задачи, последние ошибки. В других чатах команда не отвечает. Бот получает команды через вебхук, поэтому работает, когда сервер доступен из интернета по HTTPS.
- **Uptime Kuma** — свой монитор доступности с историей и графиками (`docker-compose.monitoring.yml`):
  1. `docker compose -f docker-compose.monitoring.yml up -d`, открыть `http://<сервер>:3002`, создать пользователя.
  2. Add New Monitor → HTTP(s): `https://<адрес API>/health/ready`, интервал 60 с. Второй монитор — главная страница сайта.
  3. Settings → Notifications → Telegram: токен бота и ID чата тревог; включить уведомления для мониторов.
  4. Страницу Uptime Kuma удобно добавить на экран «Домой» телефона. Он заметит, если сервер упал целиком и сам ничего прислать не может.

**Метрики Prometheus** — `GET /metrics` (запросы, время ответа, память, потоки). Доступны только с самого сервера; для внешнего Prometheus задайте `Monitoring:MetricsToken` и передавайте `Authorization: Bearer <токен>`. Графики — в Grafana по желанию.

## 🚢 Деплой

### Frontend (Vercel)
```bash
cd frontend
vercel --prod
```

### Backend (Railway/Render)
```bash
# Docker build
docker build -t cafe-api ./src/Cafe.API
```

## 📄 Лицензия

MIT License
