import type { Shawarma } from '../types';

export const ORDER_TYPES = ['Доставка', 'Самовывоз', 'В зале'] as const;
export type OrderTypeName = typeof ORDER_TYPES[number];

/** Способы получения, доступные для позиции (с учётом карточки и варианта); null — без ограничений */
export const allowedOrderTypes = (item: Pick<Shawarma, 'availableOrderTypes'> | null | undefined, child?: Pick<Shawarma, 'availableOrderTypes'> | null): string[] | null => {
  const own = item?.availableOrderTypes?.length ? item.availableOrderTypes : null;
  const ch = child?.availableOrderTypes?.length ? child.availableOrderTypes : null;
  if (!own) return ch;
  if (!ch) return own;
  return own.filter(t => ch.includes(t));
};

export const isAllowedFor = (types: string[] | null, orderType: string) => !types || types.includes(orderType);

/** «только в зале», «в зале и самовывоз» */
export const describeOrderTypes = (types: string[]) => {
  if (types.length === 1) {
    return types[0] === 'В зале' ? 'только в зале' : types[0] === 'Самовывоз' ? 'только самовывоз' : 'только доставка';
  }
  return types.map(t => t.toLowerCase()).join(' и ');
};
