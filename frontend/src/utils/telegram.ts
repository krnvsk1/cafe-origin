import { apiClient } from '../api/client';
import { useAuthStore } from '../store/authStore';

// Минимальные типы Telegram WebApp (https://core.telegram.org/bots/webapps)
interface TelegramWebApp {
  initData: string;
  colorScheme: 'light' | 'dark';
  ready: () => void;
  expand: () => void;
  openTelegramLink?: (url: string) => void;
}

declare global {
  interface Window {
    Telegram?: { WebApp?: TelegramWebApp };
  }
}

/**
 * Скрипт Telegram подключаем только внутри Telegram и не задерживая страницу:
 * в обычном браузере он не нужен, а если telegram.org недоступен, сайт не должен висеть на белом экране.
 */
const SDK_URL = 'https://telegram.org/js/telegram-web-app.js';
// Telegram передаёт данные запуска в адресе (#tgWebAppData=…) — смотрим сразу, пока роутер его не поменял
const launchedInTelegram = typeof window !== 'undefined' && (
  /tgWebAppData|tgWebAppPlatform/.test(window.location.hash)
  || 'TelegramWebviewProxy' in window
  || /Telegram/i.test(navigator.userAgent));

const loadTelegramSdk = (): Promise<void> => new Promise(resolve => {
  if (!launchedInTelegram || window.Telegram?.WebApp) return resolve();
  const script = document.createElement('script');
  script.src = SDK_URL;
  script.async = true;
  const done = () => resolve();
  script.onload = done;
  script.onerror = done;
  setTimeout(done, 5000);
  document.head.appendChild(script);
});

/** WebApp, если сайт открыт как мини-приложение внутри Telegram */
export const getTelegramWebApp = (): TelegramWebApp | null => {
  const app = window.Telegram?.WebApp;
  return app && app.initData ? app : null;
};

export const isTelegramMiniApp = () => getTelegramWebApp() !== null;

/**
 * Запуск внутри Telegram: разворачиваем окно и пробуем войти по initData
 * (работает, если пользователь уже привязал Telegram к аккаунту на сайте).
 */
export const initTelegramMiniApp = async () => {
  await loadTelegramSdk();
  const app = getTelegramWebApp();
  if (!app) return;

  try {
    app.ready();
    app.expand();
  } catch {
    // старые клиенты Telegram
  }

  const { isAuthenticated, setAuth } = useAuthStore.getState();
  if (isAuthenticated) return;

  try {
    const { data } = await apiClient.post('/api/telegram/login', { initData: app.initData });
    if (data?.success && data.accessToken && data.user) {
      setAuth(data.user, data.accessToken, data.refreshToken || '');
    }
  } catch {
    // не привязан или бот не настроен — работаем как гость
  }
};

/** Открыть ссылку на бота (внутри Telegram — нативно) */
export const openTelegramLink = (url: string) => {
  const app = getTelegramWebApp();
  if (app?.openTelegramLink) app.openTelegramLink(url);
  else window.open(url, '_blank', 'noopener');
};
