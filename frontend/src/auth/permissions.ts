import { useCallback } from 'react';
import { useAuthStore } from '../store/authStore';
import type { User } from '../types';

/** Ключи разрешений — те же, что на бэкенде (Authorization/Permissions.cs) */
export const P = {
  Dashboard: 'dashboard',
  Analytics: 'analytics',
  OrdersView: 'orders.view',
  OrdersManage: 'orders.manage',
  Pos: 'pos',
  Kitchen: 'kitchen',
  Delivery: 'delivery',
  CouriersAssign: 'couriers.assign',
  Menu: 'menu',
  MenuImport: 'menu.import',
  StopList: 'stoplist',
  Zones: 'zones',
  PromoCodes: 'promocodes',
  Reviews: 'reviews',
  Points: 'points',
  InventoryView: 'inventory.view',
  InventoryManage: 'inventory.manage',
  TechCards: 'techcards',
  Chz: 'chz',
  Finance: 'finance',
  Haccp: 'haccp',
  HaccpManage: 'haccp.manage',
  UsersView: 'users.view',
  UsersRoles: 'users.roles',
  Roles: 'roles',
  Settings: 'settings',
} as const;

export type Permission = typeof P[keyof typeof P];

/** Есть ли у пользователя хотя бы одно из разрешений (администратор — всегда) */
export const userCan = (user: User | null | undefined, ...permissions: string[]) =>
  !!user && (user.roles?.includes('admin') || permissions.some(p => user.permissions?.includes(p)));

/** Сотрудник, которому видны все заказы */
export const STAFF_PERMISSIONS = [P.OrdersView, P.Pos, P.Kitchen, P.Delivery];

/** Куда вести из «Панели управления»: первый доступный раздел */
const STAFF_HOME: [string[], string][] = [
  [[P.Dashboard], '/admin/dashboard'],
  [[P.Pos], '/pos'],
  [[P.OrdersView, P.OrdersManage], '/orders'],
  [[P.Kitchen], '/kitchen'],
  [[P.Haccp, P.HaccpManage], '/haccp'],
  [[P.Delivery, P.CouriersAssign], '/courier'],
  [[P.Menu], '/admin/menu'],
  [[P.InventoryView, P.TechCards, P.Chz], '/admin/inventory'],
  [[P.Finance], '/admin/finance'],
  [[P.Analytics], '/admin/analytics'],
  [[P.StopList], '/admin/stoplist'],
  [[P.Zones], '/admin/zones'],
  [[P.PromoCodes], '/admin/promocodes'],
  [[P.Reviews], '/admin/reviews'],
  [[P.UsersView, P.UsersRoles], '/admin/users'],
  [[P.Roles], '/admin/roles'],
];

export const staffHomePath = (user: User | null | undefined): string | null =>
  STAFF_HOME.find(([perms]) => userCan(user, ...perms))?.[1] ?? null;

/** Хук: can(...permissions) — пересчитывается при смене пользователя */
export const useCan = () => {
  const user = useAuthStore(s => s.user);
  return useCallback((...permissions: string[]) => userCan(user, ...permissions), [user]);
};
