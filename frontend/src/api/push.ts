import * as React from 'react';
import { apiClient } from './client';

/**
 * Уведомления на телефон (Web Push).
 * unsupported — браузер не умеет; insecure — сайт открыт не по https;
 * ios-install — на iPhone уведомления работают только у сайта, добавленного на экран «Домой»;
 * denied — уведомления запрещены в настройках; off / on — выключены / включены.
 */
export type PushState = 'loading' | 'unsupported' | 'insecure' | 'ios-install' | 'denied' | 'off' | 'on';

const isIos = () => /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
const isStandalone = () =>
  window.matchMedia?.('(display-mode: standalone)').matches || (navigator as Navigator & { standalone?: boolean }).standalone === true;

const keyToBytes = (base64: string) => {
  const pad = '='.repeat((4 - (base64.length % 4)) % 4);
  const raw = atob((base64 + pad).replace(/-/g, '+').replace(/_/g, '/'));
  return Uint8Array.from(raw, c => c.charCodeAt(0));
};

const baseState = (): PushState | null => {
  if (!window.isSecureContext) return 'insecure';
  if (isIos() && !isStandalone()) return 'ios-install';
  if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) return 'unsupported';
  if (Notification.permission === 'denied') return 'denied';
  return null;
};

const registration = () => navigator.serviceWorker.register('/sw.js').then(() => navigator.serviceWorker.ready);

const saveSubscription = (sub: PushSubscription) => apiClient.post('/api/push/subscribe', sub.toJSON());

/**
 * Выход из аккаунта: этот браузер больше не получает уведомления прежнего сотрудника.
 * Иначе после входа под другим (например, поваром) уведомления официанта продолжат приходить сюда.
 */
export const detachPushSubscription = async () => {
  try {
    if (!('serviceWorker' in navigator)) return;
    const reg = await navigator.serviceWorker.getRegistration('/');
    const sub = reg ? await reg.pushManager.getSubscription() : null;
    if (!sub) return;
    await apiClient.post('/api/push/unsubscribe', { endpoint: sub.endpoint }).catch(() => undefined);
    await sub.unsubscribe();
  } catch { /* не мешаем выйти */ }
};

export const usePushNotifications = () => {
  const [state, setState] = React.useState<PushState>('loading');
  const [busy, setBusy] = React.useState(false);

  React.useEffect(() => {
    let alive = true;
    (async () => {
      const b = baseState();
      if (b) { if (alive) setState(b); return; }
      try {
        const reg = await navigator.serviceWorker.getRegistration('/');
        // Подтянуть свежую версию service worker, если она вышла
        reg?.update().catch(() => undefined);
        const sub = reg ? await reg.pushManager.getSubscription() : null;
        if (sub && Notification.permission === 'granted') {
          // Подписка этого телефона привязывается к тому, кто сейчас вошёл
          saveSubscription(sub).catch(() => undefined);
          if (alive) setState('on');
        } else if (alive) setState('off');
      } catch { if (alive) setState('off'); }
    })();
    return () => { alive = false; };
  }, []);

  const enable = async () => {
    setBusy(true);
    try {
      const permission = await Notification.requestPermission();
      if (permission !== 'granted') { setState(permission === 'denied' ? 'denied' : 'off'); return false; }
      const reg = await registration();
      const { data } = await apiClient.get<{ publicKey: string }>('/api/push/key');
      let sub = await reg.pushManager.getSubscription();
      if (!sub) sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyToBytes(data.publicKey) });
      await saveSubscription(sub);
      setState('on');
      return true;
    } finally { setBusy(false); }
  };

  const disable = async () => {
    setBusy(true);
    try {
      const reg = await navigator.serviceWorker.getRegistration('/');
      const sub = reg ? await reg.pushManager.getSubscription() : null;
      if (sub) {
        await apiClient.post('/api/push/unsubscribe', { endpoint: sub.endpoint }).catch(() => undefined);
        await sub.unsubscribe();
      }
      setState('off');
    } finally { setBusy(false); }
  };

  const test = () => apiClient.post<{ sent: number }>('/api/push/test').then(r => r.data.sent);

  return { state, busy, enable, disable, test };
};
