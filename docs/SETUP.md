# Настройка новых функций

Все новые функции выключены или работают «как раньше», пока не заданы ключи. Секреты задавайте
через переменные окружения (двойное подчёркивание вместо двоеточия), а не в `appsettings.json`.

## Перед первым запуском

```bash
cd src/Cafe.API
dotnet build
dotnet ef migrations has-pending-model-changes   # должно сказать, что изменений нет
dotnet ef database update                        # или просто запустите API — миграции применятся сами
```

Если `has-pending-model-changes` найдёт расхождения (снапшот модели правился вручную, а ещё раньше
в нём расходился уникальный индекс `dishes`), создайте выравнивающую миграцию:
`dotnet ef migrations add SyncModelSnapshot` — и проверьте её содержимое перед применением.

## Заведение — `Restaurant`

| Ключ | По умолчанию | Что это |
|---|---|---|
| `Restaurant__TimeZone` | `Europe/Moscow` | часовой пояс для часов работы и аналитики |
| `Restaurant__OpenTime` / `CloseTime` | `10:00` / `23:00` | часы работы (можно через полночь) |
| `Restaurant__MinLeadMinutes` | `45` | минимум до заказа ко времени |
| `Restaurant__MaxDaysAhead` | `7` | горизонт заказа ко времени |
| `Restaurant__Latitude` / `Longitude` | центр Москвы | центр карты доставки |

## ЮKassa — `YooKassa`

1. В личном кабинете ЮKassa возьмите `shopId` и секретный ключ.
2. Задайте `YooKassa__ShopId`, `YooKassa__SecretKey`, `YooKassa__ReturnUrl` (адрес сайта).
3. В кабинете ЮKassa → HTTP-уведомления укажите `https://<api>/api/payments/yookassa/webhook`
   и включите события `payment.succeeded`, `payment.canceled`, `refund.succeeded`.
4. Сначала проверьте на тестовом магазине. Чеки 54-ФЗ: `YooKassa__SendReceipt=true` и `YooKassa__VatCode`.

Неоплаченный онлайн-заказ не попадает на кухню и отменяется через `YooKassa__PaymentTimeoutMinutes` (30).
При отмене оплаченного заказа деньги возвращаются автоматически.

## Telegram — `Telegram`

1. Создайте бота у @BotFather, получите токен.
2. Задайте `Telegram__BotToken`, `Telegram__BotUsername` (без @), `Telegram__WebhookSecret`
   (любая случайная строка), `Telegram__PublicApiUrl` (адрес API), `Telegram__WebAppUrl` (адрес сайта).
3. Войдите на сайт как admin и вызовите `POST /api/telegram/setup` (через Swagger) — бот получит вебхук,
   команды и кнопку «Меню».
4. Чат персонала: добавьте бота в группу, напишите в ней `/id`, укажите число в `Telegram__StaffChatId`.
5. В @BotFather → Bot Settings → Configure Mini App укажите адрес сайта — бот откроет его как мини-приложение.

## Яндекс Карты (зоны доставки) — фронтенд

- `VITE_YANDEX_MAPS_API_KEY` — ключ «JavaScript API и HTTP Геокодер» в кабинете разработчика Яндекса.
- `VITE_YANDEX_SUGGEST_API_KEY` — необязательно, подсказки адресов при вводе.
- В ключе укажите домен сайта (ограничение по HTTP Referer).

Пока нет ни одной активной зоны, доставка принимается по любому адресу. После появления зоны адрес
обязательно отмечается на карте — поэтому без ключа карт зоны не включайте.

## Реферальная программа — `Referral`

`Referral__Enabled`, `Referral__InviterBonus` (200), `Referral__InviteeBonus` (150),
`Referral__MinOrderAmount` (500). Бонусы начисляются, когда друг получил первый заказ.

## Новые разделы

| Адрес | Кто видит | Что там |
|---|---|---|
| `/kitchen` | admin, manager | экран кухни |
| `/courier` | courier, admin, manager | экран курьера |
| `/admin/stoplist` | admin, manager | стоп-лист |
| `/admin/reviews` | admin, manager | отзывы |
| `/admin/zones` | admin, manager | зоны доставки |
| `/admin/analytics` | admin, manager | аналитика |

## Живые обновления

Работают через Server-Sent Events (`/api/events/...`). Если API стоит за nginx, для этих путей
выключите буферизацию (`proxy_buffering off;`) и увеличьте `proxy_read_timeout`. Шина событий
работает внутри одного процесса — при нескольких экземплярах API её нужно перевести на Redis.
