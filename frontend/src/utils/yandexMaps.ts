// Загрузка JavaScript API Яндекс Карт 2.1 по требованию (без npm-пакетов)
/* eslint-disable @typescript-eslint/no-explicit-any */

declare global {
  interface Window {
    ymaps?: any;
  }
}

const API_KEY = import.meta.env.VITE_YANDEX_MAPS_API_KEY as string | undefined;
const SUGGEST_KEY = import.meta.env.VITE_YANDEX_SUGGEST_API_KEY as string | undefined;

let loading: Promise<any> | null = null;

export const isMapsConfigured = () => !!API_KEY;
export const isSuggestConfigured = () => !!SUGGEST_KEY;

export const loadYandexMaps = (): Promise<any> => {
  if (!API_KEY) return Promise.reject(new Error('Не задан VITE_YANDEX_MAPS_API_KEY'));
  if (window.ymaps?.ready) return new Promise(resolve => window.ymaps.ready(() => resolve(window.ymaps)));
  if (loading) return loading;

  loading = new Promise((resolve, reject) => {
    const script = document.createElement('script');
    const params = new URLSearchParams({ apikey: API_KEY, lang: 'ru_RU' });
    if (SUGGEST_KEY) params.set('suggest_apikey', SUGGEST_KEY);
    script.src = `https://api-maps.yandex.ru/2.1/?${params.toString()}`;
    script.async = true;
    script.onload = () => window.ymaps.ready(() => resolve(window.ymaps));
    script.onerror = () => {
      loading = null;
      reject(new Error('Не удалось загрузить Яндекс Карты'));
    };
    document.head.appendChild(script);
  });
  return loading;
};

/** Адрес → координаты [широта, долгота] и нормализованная строка адреса */
export const geocodeAddress = async (query: string): Promise<{ coords: [number, number]; address: string } | null> => {
  const ymaps = await loadYandexMaps();
  const res = await ymaps.geocode(query, { results: 1 });
  const first = res.geoObjects.get(0);
  if (!first) return null;
  return { coords: first.geometry.getCoordinates(), address: first.getAddressLine() };
};

/** Координаты → строка адреса */
export const reverseGeocode = async (coords: [number, number]): Promise<string | null> => {
  const ymaps = await loadYandexMaps();
  const res = await ymaps.geocode(coords, { results: 1, kind: 'house' });
  const first = res.geoObjects.get(0);
  return first ? first.getAddressLine() : null;
};
