import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { getAccessToken, isAccessTokenExpired, refreshAccessToken } from '../api/client';
import type { OrderEvent } from '../types';

const API_URL = (import.meta.env.VITE_API_URL as string | undefined) ?? '';

// Пауза перед новой попыткой, если браузер закрыл поток насовсем (растёт с каждой неудачей)
const RECONNECT_DELAYS_MS = [2000, 5000, 15000, 30000];

/**
 * Подписка на живые события заказов (Server-Sent Events).
 * path — '/api/events/orders' (все доступные пользователю) или '/api/events/order/{id}'.
 * Обрыв связи EventSource переживает сам. Но если сервер ответил ошибкой (например, 401 — токен в ссылке
 * истёк, пока шёл перезапуск API), браузер закрывает поток навсегда: тогда переподключаемся сами
 * со свежим токеном. Иначе кухня молча оставалась без событий и без звука новых заказов.
 */
export const useEventStream = (
  path: string | null,
  onEvent: (evt: OrderEvent) => void,
  params: Record<string, string | null | undefined> = {}
) => {
  const handlerRef = useRef(onEvent);
  handlerRef.current = onEvent;
  const paramsKey = JSON.stringify(params);

  useEffect(() => {
    if (!path || typeof EventSource === 'undefined') return;

    let source: EventSource | null = null;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    let failures = 0;
    let disposed = false;

    const listener = (e: MessageEvent) => {
      try {
        handlerRef.current(JSON.parse(e.data) as OrderEvent);
      } catch {
        // пропускаем битое сообщение
      }
    };

    const buildUrl = () => {
      const url = new URL(`${API_URL}${path}`, window.location.origin);
      const token = getAccessToken();
      if (token) url.searchParams.set('access_token', token);
      Object.entries(JSON.parse(paramsKey) as Record<string, string | null>).forEach(([k, v]) => {
        if (v) url.searchParams.set(k, v);
      });
      return url.toString();
    };

    const reconnect = async () => {
      if (disposed) return;
      const token = getAccessToken();
      if (token && isAccessTokenExpired(token)) {
        // Сервер недоступен — просто попробуем позже; отклонённый refresh разлогинит обычный запрос
        await refreshAccessToken().catch(() => null);
      }
      if (!disposed) connect();
    };

    const connect = () => {
      source = new EventSource(buildUrl());
      source.addEventListener('order', listener as EventListener);
      source.onopen = () => { failures = 0; };
      source.onerror = () => {
        if (source?.readyState !== EventSource.CLOSED) return; // браузер переподключится сам
        source.close();
        const delay = RECONNECT_DELAYS_MS[Math.min(failures, RECONNECT_DELAYS_MS.length - 1)];
        failures += 1;
        retryTimer = setTimeout(() => { void reconnect(); }, delay);
      };
    };

    connect();

    return () => {
      disposed = true;
      clearTimeout(retryTimer);
      source?.removeEventListener('order', listener as EventListener);
      source?.close();
    };
  }, [path, paramsKey]);
};

/**
 * Обновляет кэш заказов при любом событии. Возвращает обработчик для доп. действий.
 */
export const useInvalidateOnOrderEvent = () => {
  const queryClient = useQueryClient();
  return (evt: OrderEvent) => {
    queryClient.invalidateQueries({ queryKey: ['orders'] });
    queryClient.invalidateQueries({ queryKey: ['my-orders'] });
    queryClient.invalidateQueries({ queryKey: ['order', evt.orderId] });
    queryClient.invalidateQueries({ queryKey: ['order-stats'] });
    queryClient.invalidateQueries({ queryKey: ['kitchen-orders'] });
    queryClient.invalidateQueries({ queryKey: ['courier-orders'] });
    if (evt.status === 'Доставлен' || evt.status === 'Выполнен') {
      queryClient.invalidateQueries({ queryKey: ['points-balance'] });
      queryClient.invalidateQueries({ queryKey: ['points-history'] });
    }
  };
};
