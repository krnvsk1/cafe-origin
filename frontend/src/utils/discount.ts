export type DiscountType = 'percent' | 'amount';

/** Сумма скидки — так же, как на сервере (не больше суммы, до копейки). */
export const discountOf = (type: DiscountType | null | undefined, value: number | null | undefined, sum: number) => {
  if (!value || value <= 0 || sum <= 0) return 0;
  const discount = type === 'percent' ? sum * Math.min(value, 100) / 100 : type === 'amount' ? value : 0;
  return Math.round(Math.min(discount, sum) * 100) / 100;
};
