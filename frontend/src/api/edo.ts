import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { StockUnit } from './inventory';

export type EdoStatus = 'new' | 'imported' | 'ignored' | 'error';

export interface EdoState {
  enabled: boolean;
  login: string;
  hasPassword: boolean;
  account: string | null;
  autoSyncMinutes: number;
  initialDays: number;
  lastSyncAt: string | null;
  lastError: string | null;
  newCount: number;
}

export interface EdoDocumentRow {
  id: number;
  number: string | null;
  date: string | null;
  supplierInn: string | null;
  supplierName: string | null;
  total: number;
  /** Сумма НДС; null — ещё не посчитана */
  vatTotal: number | null;
  withoutVat: boolean;
  linesCount: number;
  codesCount: number;
  status: EdoStatus;
  error: string | null;
  stockDocumentId: number | null;
  receivedAt: string | null;
  importedAt: string | null;
}

export interface EdoLine {
  no: number;
  name: string;
  code: string | null;
  gtin: string | null;
  okei: string | null;
  unitName: string | null;
  quantity: number;
  price: number;
  vatRate: string | null;
  vat: number;
  withoutVat: boolean;
  sum: number;
  codes: string[];
  stockItemId: number | null;
  stockItemName: string | null;
  stockUnit: StockUnit | null;
  factor: number;
  /** Позиция подставлена из прошлых накладных этого поставщика */
  remembered: boolean;
}

/** НДС по накладной — для платёжки */
export interface EdoVat {
  total: number;
  withoutVat: boolean;
  byRate: { rate: string; sum: number; vat: number }[];
  /** «в т.ч. НДС 20% — 200,00 руб.» / «НДС не облагается» */
  phrase: string;
  /** Готовое назначение платежа */
  paymentPurpose: string;
}

export interface EdoDocumentDetail extends EdoDocumentRow {
  supplierKpp: string | null;
  fileName: string | null;
  vat: EdoVat | null;
  lines: EdoLine[] | null;
}

export interface EdoSettingsInput {
  enabled: boolean;
  login: string;
  password: string | null;
  account: string | null;
  autoSyncMinutes: number;
  initialDays: number;
}

export const EDO_STATUS: Record<EdoStatus, { label: string; color: string }> = {
  new: { label: 'Новая', color: '#2563EB' },
  imported: { label: 'Оприходована', color: '#2E9E5B' },
  ignored: { label: 'Скрыта', color: '#7B776F' },
  error: { label: 'Ошибка', color: '#E2462C' },
};

/** Единица по ОКЕИ — для подсказки, если в накладной нет названия */
export const OKEI: Record<string, string> = { '796': 'шт', '166': 'кг', '163': 'г', '112': 'л', '111': 'мл', '778': 'упак', '168': 'т' };

/** Сколько единиц склада в одной единице накладной по умолчанию (как на сервере) */
export const defaultFactor = (okei: string | null, unit: StockUnit) =>
  okei === '163' && unit === 'kg' ? 0.001 : okei === '111' && unit === 'l' ? 0.001 : okei === '168' && unit === 'kg' ? 1000 : 1;

/** Коды из накладной → вкладка «Честный знак» (подхватит при открытии) */
export const CHZ_PREFILL_KEY = 'chz-prefill';

export const useEdoState = () =>
  useQuery<EdoState>({
    queryKey: ['edo', 'status'],
    queryFn: () => apiClient.get('/api/edo/status').then(r => r.data),
  });

export const useEdoDocuments = (status: EdoStatus | 'all') =>
  useQuery<EdoDocumentRow[]>({
    queryKey: ['edo', 'documents', status],
    queryFn: () => apiClient.get('/api/edo/documents', { params: status === 'all' ? {} : { status } }).then(r => r.data),
  });

export const useEdoDocument = (id: number | null) =>
  useQuery<EdoDocumentDetail>({
    queryKey: ['edo', 'document', id],
    queryFn: () => apiClient.get(`/api/edo/documents/${id}`).then(r => r.data),
    enabled: !!id,
  });

export const useSaveEdoSettings = () => {
  const qc = useQueryClient();
  return useMutation<EdoState, Error, EdoSettingsInput>({
    mutationFn: (data) => apiClient.put('/api/edo/settings', data).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['edo'] }),
  });
};

export const testEdoSettings = (data: EdoSettingsInput) =>
  apiClient.post<{ ok: boolean; message: string }>('/api/edo/settings/test', data).then(r => r.data);

export const edoSync = () =>
  apiClient.post<{ received: number; errors: number }>('/api/edo/sync', null, { timeout: 180000 }).then(r => r.data);

export const edoImport = (id: number, lines: { no: number; stockItemId: number | null; factor: number; skip: boolean }[], post: boolean) =>
  apiClient.post<{ stockDocumentId: number; posted: boolean }>(`/api/edo/documents/${id}/import`, { lines, post }).then(r => r.data);

export const edoIgnore = (id: number) => apiClient.post(`/api/edo/documents/${id}/ignore`);
export const edoRestore = (id: number) => apiClient.post(`/api/edo/documents/${id}/restore`);
