---
name: frontend-react
description: Project-specific frontend patterns for the Cafe React 18 + TypeScript + Vite + MUI app (React Query hooks in src/api, Zustand stores, permission gating, axios client with token refresh). Use when adding or changing pages, components, API hooks, stores, or permission checks in frontend/.
---

# Cafe Frontend (React 18 / TypeScript / Vite / MUI / React Query)

Стек: React 18, TypeScript 5.6, Vite 6, MUI 6 (`@mui/material`, `@mui/icons-material`),
TanStack React Query 5, Zustand 5, react-router-dom 6, axios.
Код — `frontend/src`, ESLint flat config (`npm run lint`), сборка `npm run build` (`tsc -b && vite build`).

> Generic component/state guidance — global `frontend-patterns` skill;
> E2E — `e2e-testing`. This file covers this repo's conventions.

## Folder map

| Папка | Что там |
|-------|---------|
| `src/api/client.ts` | axios `apiClient`, токены, refresh на 401 |
| `src/api/<domain>.ts` | доменные модули: типы + React Query хуки (`waiter.ts`, `banquets.ts`, `inventory.ts`, ...) |
| `src/api/hooks.ts` | общие хуки каталога/заказов (dishes, orders, users) |
| `src/api/reorder.ts` | перестановка элементов (drag-and-drop порядок) |
| `src/pages/*.tsx` | экраны (27 страниц), подпапки `haccp/`, `inventory/`, `finance/` |
| `src/components/*.tsx` | переиспользуемые блоки (диалоги, панели, графики) |
| `src/store/*.ts` | Zustand: `authStore`, `cartStore`, `orderFlowStore` |
| `src/auth/permissions.ts` | `P` — ключи прав, `useCan()` |
| `src/utils/*` | `apiError`, даты, вес, печать, звук, карты |
| `src/types/index.ts` | общие доменные типы |

## API layer rules

- Один домен — один файл в `src/api/`. Запросы только через `apiClient`
  (`src/api/client.ts`), не через голый `axios`: иначе не будет подстановки
  токена и обновления сессии на 401.
- Хук возвращает типизированный результат:

```ts
export const useDish = (id: number) =>
  useQuery<Dish>({
    queryKey: ['dish', id],
    queryFn: () => apiClient.get(`/api/dishes/${id}`).then(r => r.data),
    enabled: !!id,
  });

export const useUpdateDish = () => {
  const qc = useQueryClient();
  return useMutation<Dish, Error, Partial<Dish> & { id: number }>({
    mutationFn: ({ id, ...data }) => apiClient.put(`/api/dishes/${id}`, data).then(r => r.data),
    onSuccess: (_, v) => {
      qc.invalidateQueries({ queryKey: ['dishes'] });
      qc.invalidateQueries({ queryKey: ['dish', v.id] });
    },
  });
};
```

- `queryKey` — массив, первый элемент = домен (`['shifts', id]`), инвалидация
  по префиксу. Стабильные данные (меню) — `staleTime` (5 мин), живые
  (смены, заказы кухни) — короткий `staleTime` или опрос через `refetchInterval`.
- Ошибки показывать через `apiError(e)` из `src/api/inventory.ts` (читает
  `message`/`Message`, отдельный текст на 403). Никаких `alert()`.
- Даты с бэкенда — ISO; для отображения `parseUtc(...)` (см. `api/waiter.ts`),
  «сегодня» по Москве — `todayMsk()` (`api/haccp.ts`).

## UI conventions

- MUI-компоненты; `sx` для разметки, `Box`/`Stack` вместо ручного CSS.
- Обратная связь: `Snackbar` для результата действия, `Alert` внутри формы,
  `Dialog` для подтверждений и ввода (см. `ShiftsPage`, `BanquetsPage`).
- Деньги — одна функция форматирования на страницу
  (`const money = (v: number) => \`${Math.round(v).toLocaleString('ru-RU')} ₽\``).
- Тексты интерфейса — по-русски, короткие, в нижнем регистре для статусов
  (`открыт`, `оплачен`, `отменён`).
- Состояние сервера — только React Query, не дублировать в Zustand. Zustand —
  для клиентского: сессия (`authStore`), корзина, шаги оформления.
- Никакой мутации объектов/массивов из ответов сервера: `map`, spread, новые
  объекты — иммutability соблюдается во всём `src`.

## Permissions

```ts
import { useCan, P } from '../auth/permissions';
const can = useCan();
if (can(P.Shift)) { /* ... */ }
```

- Ключи `P.*` должны совпадать с бэкендовыми `Permissions.*`
  (`src/Cafe.API/Authorization/Permissions.cs`). Новое право — синхронно в
  двух местах.
- Скрывать/дизейблить по правам и в навигации, и на самой странице (прямой
  заход по URL не должен давать доступ).

## Quality gates

- `npm run lint` и `npm run build` (`tsc -b`) обязаны быть зелёными — типы не
  глушить через `any`, для внешних ответов — `interface` + явные поля.
- Файл > 800 строк — повод вынести подкомпоненты в `components/`.
- Не добавлять зависимость ради одной функции: сначала проверить, есть ли
  хук/утилита в `src/api` и `src/utils`.

## Checklist перед коммитом (frontend)

- [ ] Запросы через `apiClient`, хуки в `src/api/<domain>.ts`, ключи
      инвалидации верные
- [ ] Ошибки показаны через `apiError`, есть состояние загрузки и пустое
- [ ] Права через `P`/`useCan`, ключ совпадает с бэкендом
- [ ] Никакой мутации props/ответов, список через `map` + `key`
- [ ] `npm run lint` и `npm run build` зелёные
