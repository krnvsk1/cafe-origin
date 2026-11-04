import { useEffect, useState, useSyncExternalStore } from 'react';

/**
 * «Во весь экран» для рабочих экранов (официант, кухня, касса).
 * Два слоя: режим без шапки и подвала (работает везде, в том числе на iPhone, где браузер
 * не умеет разворачивать страницу) и, где можно, настоящий полноэкранный режим браузера.
 */
const FOCUS_KEY = 'work-focus-mode';

const readFocus = () => {
  try { return localStorage.getItem(FOCUS_KEY) === '1'; } catch { return false; }
};

let focus = typeof window !== 'undefined' ? readFocus() : false;
/** Включили вместе с полноэкранным режимом браузера — выход из него (Esc, жест «назад») выключает и режим */
let viaBrowser = false;
let workScreens = 0;
const listeners = new Set<() => void>();
const emit = () => listeners.forEach(l => l());

const setFocus = (value: boolean) => {
  focus = value;
  try { localStorage.setItem(FOCUS_KEY, value ? '1' : '0'); } catch { /* ignore */ }
  emit();
};

if (typeof document !== 'undefined') {
  document.addEventListener('fullscreenchange', () => {
    if (!document.fullscreenElement && viaBrowser) {
      viaBrowser = false;
      setFocus(false);
    } else emit();
  });
}

const subscribe = (onChange: () => void) => {
  listeners.add(onChange);
  return () => { listeners.delete(onChange); };
};

const isFullscreenNow = () => !!document.fullscreenElement || (focus && workScreens > 0);

/** Развёрнут ли рабочий экран: шапку и подвал приложение прячет (см. App) */
export const useIsFullscreen = (): boolean => useSyncExternalStore(subscribe, isFullscreenNow, () => false);

let wideScreens = 0;
const isWideNow = () => wideScreens > 0;

/** Страница на всю ширину экрана (кухня на телевизоре), без ограничения ширины содержимого */
export const useWidePage = () => {
  useEffect(() => {
    wideScreens += 1;
    emit();
    return () => { wideScreens -= 1; emit(); };
  }, []);
};

export const useIsWidePage = (): boolean => useSyncExternalStore(subscribe, isWideNow, () => false);

/**
 * Масштаб для больших экранов (телевизор на кухне): всё крупнее, чтобы читалось издалека,
 * а раскладка — как на экране шириной около baseWidth. На обычных мониторах и планшетах — 1.
 */
export const useScreenScale = (baseWidth = 1600, max = 2.5): number => {
  const read = () => (typeof window === 'undefined' ? 1 : Math.min(max, Math.max(1, window.innerWidth / baseWidth)));
  const [scale, setScale] = useState(read);
  useEffect(() => {
    const onResize = () => setScale(read());
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  return scale;
};

/** Рабочий экран, где можно убрать всё лишнее (официант, кухня, касса) */
export const useWorkScreen = () => {
  useEffect(() => {
    workScreens += 1;
    emit();
    return () => { workScreens -= 1; emit(); };
  }, []);
};

/**
 * На весь экран разворачиваем всю страницу, а не отдельный блок: меню, диалоги и панели MUI
 * рисуются в document.body и иначе пропадают.
 */
export const toggleFullscreen = () => {
  if (focus || document.fullscreenElement) {
    viaBrowser = false;
    setFocus(false);
    if (document.fullscreenElement) void document.exitFullscreen().catch(() => { /* уже вышли */ });
    return;
  }
  setFocus(true);
  const request = document.documentElement.requestFullscreen?.bind(document.documentElement);
  if (request && document.fullscreenEnabled) {
    void request().then(() => { viaBrowser = true; }).catch(() => { /* браузер не разрешил — остаёмся без шапки */ });
  }
};
