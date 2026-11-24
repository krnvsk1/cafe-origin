import { tokens } from '../theme';

export const BADGES: Record<string, { label: string; color: string }> = {
  hit: { label: 'Хит', color: tokens.chili },
  new: { label: 'Новинка', color: tokens.herb },
  promo: { label: 'Акция', color: '#7A4DFF' },
  lean: { label: 'Постное', color: '#4E7D2E' },
};
