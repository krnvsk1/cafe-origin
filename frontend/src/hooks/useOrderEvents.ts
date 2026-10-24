import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { getAccessToken } from '../api/client';
import type { OrderEvent } from '../types';

const API_URL = (import.meta.env.VITE_API_URL as string | undefined) ?? '';

/**
 * Подписка на живые события заказов (Server-Sent Events).
 * path — '/api/events/orders' (все доступные пользователю) или '/api/events/order/{id}'.
 * EventSource сам переподключается при обрыве связи.
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

    const url = new URL(`${API_URL}${path}`, window.location.origin);
    const token = getAccessToken();
    if (token) url.searchParams.set('access_token', token);
    Object.entries(JSON.parse(paramsKey) as Record<string, string | null>).forEach(([k, v]) => {
      if (v) url.searchParams.set(k, v);
    });

    const source = new EventSource(url.toString());
    const listener = (e: MessageEvent) => {
      try {
        handlerRef.current(JSON.parse(e.data) as OrderEvent);
      } catch {
        // пропускаем битое сообщение
      }
    };
    source.addEventListener('order', listener as EventListener);

    return () => {
      source.removeEventListener('order', listener as EventListener);
      source.close();
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
