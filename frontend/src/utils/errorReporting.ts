import { apiClient } from '../api/client';

/**
 * Ошибки в браузере (сайт, касса, кухня, официант) уходят в журнал сервера —
 * их видно на экране «Состояние системы». Не больше 10 за загрузку страницы, повторы не шлём.
 */
const sent = new Set<string>();
let count = 0;

export const reportClientError = (message: string, stack?: string) => {
  const key = `${message}|${(stack ?? '').slice(0, 200)}`;
  if (!message || sent.has(key) || count >= 10) return;
  sent.add(key);
  count += 1;
  apiClient.post('/api/client-log', {
    message: message.slice(0, 500),
    stack: stack?.slice(0, 2000),
    url: window.location.pathname,
    userAgent: navigator.userAgent,
  }).catch(() => { /* журнал не должен ломать страницу */ });
};

export const installErrorReporting = () => {
  window.addEventListener('error', e => {
    // Не загрузилась картинка или скрипт расширения — не наша ошибка
    if (!e.message || e.filename?.startsWith('chrome-extension://')) return;
    reportClientError(e.message, e.error?.stack);
  });
  window.addEventListener('unhandledrejection', e => {
    const r = e.reason;
    // Ошибки запросов к серверу и так видны в его журнале
    if (r?.isAxiosError) return;
    reportClientError(r?.message ?? String(r), r?.stack);
  });
};
