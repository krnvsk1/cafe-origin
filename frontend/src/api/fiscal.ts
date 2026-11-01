import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';

export interface FiscalSettings {
  enabled: boolean;
  url: string;
  taxationType: string;
  vat: string;
  paymentObject: string;
  cashierInn: string | null;
  sendToCustomer: boolean;
}

export interface LayoutLine {
  text: string;
  align: 'left' | 'center' | 'right';
  large: boolean;
}

/** Свой текст сверху/снизу чека и QR-код; обязательные реквизиты печатает касса */
export interface ReceiptLayout {
  width: 32 | 42 | 48;
  header: LayoutLine[];
  footer: LayoutLine[];
  qr: string | null;
}

export interface FiscalState extends FiscalSettings {
  layout: ReceiptLayout;
  shiftState: 'opened' | 'closed' | 'expired' | string | null;
  shiftNumber: number | null;
  shiftError: string | null;
  failedReceipts: number;
}

/** Результат пробития чека */
export interface FiscalReceiptResult {
  id?: number;
  kind?: 'sell' | 'sellReturn';
  status: 'processing' | 'done' | 'error';
  error: string | null;
  fiscalDocumentNumber?: number | null;
  shiftNumber?: number | null;
  clientContact?: string | null;
}

export const TAXATION_TYPES: { value: string; label: string }[] = [
  { value: 'usnIncome', label: 'УСН «Доходы»' },
  { value: 'usnIncomeOutcome', label: 'УСН «Доходы минус расходы»' },
  { value: 'patent', label: 'Патент (ПСН)' },
  { value: 'osn', label: 'ОСН' },
  { value: 'esn', label: 'ЕСХН' },
];

export const VAT_TYPES: { value: string; label: string }[] = [
  { value: 'none', label: 'Без НДС' },
  { value: 'vat0', label: 'НДС 0%' },
  { value: 'vat5', label: 'НДС 5%' },
  { value: 'vat7', label: 'НДС 7%' },
  { value: 'vat10', label: 'НДС 10%' },
  { value: 'vat20', label: 'НДС 20%' },
  { value: 'vat22', label: 'НДС 22%' },
];

export const SHIFT_TEXT: Record<string, string> = {
  opened: 'Смена открыта',
  closed: 'Смена закрыта',
  expired: 'Смена больше 24 ч',
};

export const useFiscalState = (withShift = false, enabled = true) =>
  useQuery<FiscalState>({
    queryKey: ['fiscal', 'status', withShift],
    queryFn: () => apiClient.get('/api/fiscal/status', { params: { shift: withShift } }).then(r => r.data),
    enabled,
    refetchInterval: withShift ? 5 * 60_000 : false,
  });

export const useSaveFiscalSettings = () => {
  const qc = useQueryClient();
  return useMutation<FiscalState, Error, FiscalSettings>({
    mutationFn: (data) => apiClient.put('/api/fiscal/settings', data).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['fiscal'] }),
  });
};

export const testFiscalSettings = (data: FiscalSettings) =>
  apiClient.post<{ ok: boolean; message: string }>('/api/fiscal/settings/test', data).then(r => r.data);

export const fiscalShift = (op: 'open' | 'close' | 'x') =>
  apiClient.post<FiscalState>(`/api/fiscal/shift/${op}`).then(r => r.data);

export const fiscalSell = (orderId: number) =>
  apiClient.post<FiscalReceiptResult>(`/api/fiscal/orders/${orderId}/sell`).then(r => r.data);

export const fiscalReturn = (orderId: number) =>
  apiClient.post<FiscalReceiptResult>(`/api/fiscal/orders/${orderId}/return`).then(r => r.data);

export const useSaveReceiptLayout = () => {
  const qc = useQueryClient();
  return useMutation<FiscalState, Error, ReceiptLayout>({
    mutationFn: (data) => apiClient.put('/api/fiscal/layout', data).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['fiscal'] }),
  });
};

export const printReceiptSample = (layout: ReceiptLayout) =>
  apiClient.post('/api/fiscal/layout/sample', layout).then(r => r.data);

/** Подстановки в своём тексте чека */
export const LAYOUT_VARS: { key: string; label: string; sample: string }[] = [
  { key: '{brand}', label: 'название заведения', sample: 'Кафе «Дворик»' },
  { key: '{order}', label: 'номер заказа', sample: '128' },
  { key: '{type}', label: 'в зале / с собой / доставка', sample: 'в зале' },
  { key: '{cashier}', label: 'кассир', sample: 'Иванова Анна' },
  { key: '{date}', label: 'дата', sample: '28.10.2026' },
  { key: '{time}', label: 'время', sample: '21:45' },
];
