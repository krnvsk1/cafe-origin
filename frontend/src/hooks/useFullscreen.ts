import { useSyncExternalStore } from 'react';

const subscribe = (onChange: () => void) => {
  document.addEventListener('fullscreenchange', onChange);
  return () => document.removeEventListener('fullscreenchange', onChange);
};

const isFullscreenNow = () => !!document.fullscreenElement;

/** Развёрнута ли страница на весь экран (кухня, касса) */
export const useIsFullscreen = (): boolean => useSyncExternalStore(subscribe, isFullscreenNow, () => false);

/**
 * На весь экран разворачиваем всю страницу, а не отдельный блок: меню, диалоги и панели MUI
 * рисуются в document.body и иначе пропадают. Шапку и подвал приложение прячет само (см. App).
 */
export const toggleFullscreen = () => {
  if (document.fullscreenElement) {
    void document.exitFullscreen().catch(() => { /* уже вышли */ });
    return;
  }
  void document.documentElement.requestFullscreen?.().catch(() => { /* браузер не разрешил */ });
};
