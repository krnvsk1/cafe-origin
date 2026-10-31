// src/main.tsx
import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';

const root = ReactDOM.createRoot(
  document.getElementById('root') as HTMLElement
);

// Нажали на уведомление, а приложение уже открыто — переходим по ссылке из уведомления
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.addEventListener('message', event => {
    const data = event.data as { type?: string; url?: string } | null;
    if (data?.type !== 'open' || !data.url) return;
    const target = new URL(data.url, window.location.origin);
    if (target.origin !== window.location.origin) return;
    const handled = !window.dispatchEvent(new CustomEvent('sw-open', { detail: target.pathname + target.search, cancelable: true }));
    if (!handled) window.location.assign(target.pathname + target.search);
  });
}

root.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);