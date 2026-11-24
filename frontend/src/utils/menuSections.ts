import type { Dish } from '../types';

export interface MenuSection {
  name: string;
  cards: Dish[];
}

/** Разделы по первой карточке; карточки внутри раздела — по sortOrder. */
export const buildSections = (parents: Dish[]): MenuSection[] => {
  const sorted = [...parents].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || a.id - b.id);
  const grouped = new Map<string, Dish[]>();
  sorted.forEach(dish => {
    const name = dish.category?.trim() ?? '';
    const cards = grouped.get(name) ?? [];
    grouped.set(name, [...cards, dish]);
  });
  return Array.from(grouped, ([name, cards]) => ({ name, cards }));
};

/** Сквозная нумерация задаёт порядок разделов по первой карточке. */
export const toReorderItems = (sections: MenuSection[]) =>
  sections.flatMap(section => section.cards.map(card => ({ id: card.id, category: section.name })))
    .map((card, index) => ({ ...card, sortOrder: (index + 1) * 10 }));
