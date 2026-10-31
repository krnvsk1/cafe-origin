/* Service worker: уведомления сотрудникам (официанту — «блюдо готово») · v2 */
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('push', event => {
  let data = {};
  try { data = event.data ? event.data.json() : {}; } catch { data = { body: event.data ? event.data.text() : '' }; }
  const title = data.title || 'Уведомление';
  event.waitUntil(self.registration.showNotification(title, {
    body: data.body || '',
    tag: data.tag,
    renotify: !!data.tag,
    icon: '/icon-192.png',
    badge: '/icon-192.png',
    vibrate: [200, 100, 200],
    requireInteraction: false,
    data: { url: data.url || '/' },
  }));
});

self.addEventListener('notificationclick', event => {
  event.notification.close();
  const url = new URL((event.notification.data && event.notification.data.url) || '/', self.location.origin).href;
  event.waitUntil((async () => {
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    // Сначала — вкладка, где уже открыт экран официанта, иначе любая вкладка сайта
    const ours = windows.filter(c => new URL(c.url).origin === self.location.origin);
    const client = ours.find(c => new URL(c.url).pathname.startsWith('/waiter')) || ours[0];
    if (client) {
      // Переходим по адресу прямо во вкладке: надёжнее, чем просить страницу перейти самой
      try {
        const moved = await client.navigate(url);
        if (moved) { await moved.focus(); return; }
      } catch { /* вкладка не под этим service worker — ниже запасной путь */ }
      try { await client.focus(); } catch { /* ignore */ }
      client.postMessage({ type: 'open', url });
      return;
    }
    await self.clients.openWindow(url);
  })());
});
