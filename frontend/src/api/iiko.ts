import { apiClient } from './client';

export interface IikoTarget { kind: 'dish' | 'addon'; id: number; name: string; hasCard: boolean }
export interface IikoIngredient { name: string; type: string; gross: number; unit: string | null }
export interface IikoDish {
  iikoId: string;
  name: string;
  type: string;
  yield: number;
  unit: string | null;
  ingredients: IikoIngredient[];
  /** Блюдо нашего меню, найденное по названию */
  match: IikoTarget | null;
}
export interface IikoPreview {
  snapshotId: string;
  dishes: IikoDish[];
  prepared: number;
  goods: number;
  goodsMatched: number;
  targets: IikoTarget[];
}
export interface IikoImportResult { cards: number; semiCards: number; createdItems: number; skipped: number; warnings: string[] }

export const fetchIikoConnection = () => apiClient.get<{ url: string; login: string }>('/api/iiko/connection').then(r => r.data);

export const iikoPreview = (url: string, login: string, password: string) =>
  apiClient.post<IikoPreview>('/api/iiko/preview', { url, login, password }, { timeout: 180000 }).then(r => r.data);

export const iikoImport = (snapshotId: string, dishes: { iikoId: string; kind: string; id: number }[], overwrite: boolean) =>
  apiClient.post<IikoImportResult>('/api/iiko/import', { snapshotId, dishes, overwrite }, { timeout: 180000 }).then(r => r.data);
