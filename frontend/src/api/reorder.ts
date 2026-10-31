import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import { useCartStore } from '../store/cartStore';
import type { SelectedAddon, Dish } from '../types';

interface ReorderAddon {
  addonId: number;
  name: string;
  price: number;
  categoryId: number;
  categoryName: string;
  quantity: number;
  available: boolean;
}

interface ReorderItem {
  dishId: number;
  cardId: number;
  name: string;
  quantity: number;
  available: boolean;
  price: number;
  oldPrice: number;
  addons: ReorderAddon[];
}

export interface RepeatOrderResult {
  added: number;
  skipped: string[];        // позиции, которых нет в наличии
  skippedAddons: string[];  // добавки, которых нет в наличии
  priceChanged: boolean;
}

/**
 * Повтор заказа: берёт актуальные цены и наличие с сервера и кладёт позиции в корзину.
 * Недоступные позиции и добавки пропускаются, о них сообщается в результате.
 */
export const useRepeatOrder = () => {
  const queryClient = useQueryClient();
  const addItem = useCartStore(state => state.addItem);

  return useMutation<RepeatOrderResult, Error, number>({
    mutationFn: async (orderId) => {
      const [{ data }, menu] = await Promise.all([
        apiClient.get<{ orderId: number; items: ReorderItem[] }>(`/api/orders/${orderId}/reorder`),
        queryClient.fetchQuery<Dish[]>({
          queryKey: ['dishes'],
          queryFn: () => apiClient.get('/api/dishes').then(res => res.data),
          staleTime: 60 * 1000,
        }),
      ]);

      const cards = menu.filter(s => s.parentId == null);
      const result: RepeatOrderResult = { added: 0, skipped: [], skippedAddons: [], priceChanged: false };

      for (const item of data.items) {
        const card = cards.find(c => c.id === item.cardId);
        if (!item.available || !card) {
          result.skipped.push(item.name);
          continue;
        }
        const child = item.cardId !== item.dishId
          ? card.children?.find(c => c.id === item.dishId)
          : undefined;
        if (item.cardId !== item.dishId && !child) {
          result.skipped.push(item.name);
          continue;
        }

        const addons: SelectedAddon[] = [];
        for (const a of item.addons) {
          if (!a.available) {
            result.skippedAddons.push(a.name);
            continue;
          }
          addons.push({
            addonId: a.addonId,
            addonName: a.name,
            price: a.price,
            quantity: a.quantity,
            categoryId: a.categoryId,
            categoryName: a.categoryName,
          });
        }

        if (item.price !== item.oldPrice) result.priceChanged = true;
        addItem(card, item.quantity, addons, '', child);
        result.added += item.quantity;
      }

      return result;
    },
  });
};

export const describeRepeatResult = (orderId: number, r: RepeatOrderResult): { message: string; severity: 'success' | 'warning' | 'error' } => {
  if (r.added === 0) {
    return { message: `Позиции из заказа #${orderId} сейчас недоступны`, severity: 'error' };
  }
  const notes: string[] = [];
  if (r.skipped.length) notes.push(`нет в наличии: ${r.skipped.join(', ')}`);
  if (r.skippedAddons.length) notes.push(`без добавок: ${Array.from(new Set(r.skippedAddons)).join(', ')}`);
  if (r.priceChanged) notes.push('цены обновились');
  return {
    message: `Заказ #${orderId} добавлен в корзину${notes.length ? ` (${notes.join('; ')})` : ''}`,
    severity: notes.length ? 'warning' : 'success',
  };
};
