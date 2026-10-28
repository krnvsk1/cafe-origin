import { P } from '../../auth/permissions';

/** Вкладка внутри раздела склада */
export type InventoryTab =
  | 'stock' | 'items'
  | 'receipts' | 'writeoffs' | 'counts'
  | 'edo' | 'dishes' | 'reports'
  | 'egais' | 'mercury' | 'chz'
  | 'suppliers';

export interface InventorySection {
  key: string;
  title: string;
  subtitle: string;
  /** Адрес раздела; вторая и следующие вкладки — base/<tab> */
  base: string;
  tabs: { value: InventoryTab; label: string }[];
  /** Достаточно любого из прав */
  perms: string[];
}

/**
 * Бывший «Склад» с 12 вкладками, разнесённый по пунктам меню «Управление»:
 * группа «Склад» — учёт, «Госсистемы» — ЕГАИС, Меркурий, Честный знак, «Финансы» — поставщики.
 */
export const INVENTORY_SECTIONS: InventorySection[] = [
  {
    key: 'stock', title: 'Остатки', subtitle: 'Что есть на складе и справочник номенклатуры', base: '/admin/stock',
    tabs: [{ value: 'stock', label: 'Остатки' }, { value: 'items', label: 'Номенклатура' }],
    perms: [P.InventoryView],
  },
  {
    key: 'documents', title: 'Документы', subtitle: 'Приходы, списания и инвентаризации', base: '/admin/documents',
    tabs: [{ value: 'receipts', label: 'Приходы' }, { value: 'writeoffs', label: 'Списания' }, { value: 'counts', label: 'Инвентаризация' }],
    perms: [P.InventoryView],
  },
  {
    key: 'edo', title: 'Накладные СБИС', subtitle: 'Входящие УПД от поставщиков', base: '/admin/edo',
    tabs: [{ value: 'edo', label: 'Накладные СБИС' }],
    perms: [P.InventoryView],
  },
  {
    key: 'techcards', title: 'Техкарты', subtitle: 'Блюда и полуфабрикаты: состав, выход, себестоимость', base: '/admin/techcards',
    tabs: [{ value: 'dishes', label: 'Техкарты' }],
    perms: [P.InventoryView, P.TechCards],
  },
  {
    key: 'reports', title: 'Отчёты склада', subtitle: 'Движение товаров и себестоимость', base: '/admin/stock-reports',
    tabs: [{ value: 'reports', label: 'Отчёты' }],
    perms: [P.InventoryView],
  },
  {
    key: 'egais', title: 'ЕГАИС', subtitle: 'Алкоголь: входящие накладные и акты', base: '/admin/egais',
    tabs: [{ value: 'egais', label: 'ЕГАИС' }],
    perms: [P.InventoryView],
  },
  {
    key: 'mercury', title: 'Меркурий', subtitle: 'Входящие ВСД и их гашение', base: '/admin/mercury',
    tabs: [{ value: 'mercury', label: 'Меркурий' }],
    perms: [P.InventoryView],
  },
  {
    key: 'chz', title: 'Честный знак', subtitle: 'Остатки маркированного товара и вывод из оборота', base: '/admin/chz',
    tabs: [{ value: 'chz', label: 'Честный знак' }],
    perms: [P.Chz],
  },
  {
    key: 'suppliers', title: 'Поставщики', subtitle: 'Справочник поставщиков для приходов', base: '/admin/suppliers',
    tabs: [{ value: 'suppliers', label: 'Поставщики' }],
    perms: [P.InventoryView],
  },
];

export const sectionOf = (tab: string | undefined) => INVENTORY_SECTIONS.find(s => s.tabs.some(t => t.value === tab));

/** Адрес вкладки склада (в том числе для старых ссылок вида /admin/inventory/<tab>) */
export const inventoryPath = (tab: InventoryTab) => {
  const s = sectionOf(tab)!;
  return s.tabs[0].value === tab ? s.base : `${s.base}/${tab}`;
};
