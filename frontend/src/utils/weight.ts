import type { Dish } from '../types';

/** Весовые блюда: цена в меню — за 100 г, при заказе выбирают вес в граммах */
export const WEIGHT_PRESETS = [100, 200, 300, 500, 1000];
export const DEFAULT_GRAMS = 300;
export const MIN_GRAMS = 10;
export const MAX_GRAMS = 10000;

/** Весовое ли блюдо: признак на карточке действует на все её варианты */
export const isWeightedDish = (card?: Pick<Dish, 'isWeighted'> | null, child?: Pick<Dish, 'isWeighted'> | null) =>
  !!(card?.isWeighted || child?.isWeighted);

/** Цена за вес при цене за 100 г — как на сервере */
export const priceForWeight = (per100: number, grams: number) => Math.round(per100 * grams) / 100;

/** «350 г», «1,5 кг» */
export const weightLabel = (grams: number) =>
  grams >= 1000 && grams % 100 === 0 ? `${(grams / 1000).toLocaleString('ru-RU')} кг` : `${grams} г`;

export const validGrams = (g: number) => Number.isInteger(g) && g >= MIN_GRAMS && g <= MAX_GRAMS;
