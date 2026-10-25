import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';

/** Куда попадает платёж: просрочен, в ближайшие 7 дней, позже, без срока, оплачен */
export type PaymentBucket = 'overdue' | 'week' | 'later' | 'nodue' | 'paid';
export type PaymentFilter = 'unpaid' | 'paid' | 'all';

export interface PaymentRow {
  id: number;
  number: string | null;
  date: string | null;
  supplierInn: string | null;
  supplierName: string | null;
  total: number;
  vatTotal: number | null;
  withoutVat: boolean;
  dueDate: string | null;
  /** Срок взят из самого УПД (иначе — из отсрочки поставщика) */
  dueFromUpd: boolean;
  daysLeft: number | null;
  bucket: PaymentBucket;
  paidAt: string | null;
  edoStatus: string;
  stockDocumentId: number | null;
}

export interface BucketSum { count: number; total: number }

export interface PaymentsResponse {
  today: string;
  summary: { overdue: BucketSum; week: BucketSum; later: BucketSum; noDue: BucketSum; unpaidTotal: number };
  items: PaymentRow[];
}

export interface PaymentDetail extends PaymentRow {
  supplierKpp: string | null;
  updDelayDays: number | null;
  supplierDelayDays: number | null;
  bank: { account: string | null; bik: string | null; corrAccount: string | null; bankName: string | null } | null;
  /** Реквизиты не из этого УПД, а запомненные у поставщика из прошлых */
  bankFromSupplier: boolean;
  basis: { name: string | null; number: string | null; date: string | null } | null;
  vatPhrase: string | null;
  paymentPurpose: string | null;
  paidBy: string | null;
  error: string | null;
}

const KEY = 'finance';

export const useFinancePayments = (filter: PaymentFilter) =>
  useQuery<PaymentsResponse>({
    queryKey: [KEY, 'payments', filter],
    queryFn: () => apiClient.get('/api/finance/payments', { params: { filter } }).then(r => r.data),
  });

export const useFinancePayment = (id: number | null) =>
  useQuery<PaymentDetail>({
    queryKey: [KEY, 'payment', id],
    queryFn: () => apiClient.get(`/api/finance/payments/${id}`).then(r => r.data),
    enabled: id !== null,
  });

export const useMarkPaid = () => {
  const qc = useQueryClient();
  return useMutation<unknown, Error, { id: number; paid: boolean }>({
    mutationFn: ({ id, paid }) => apiClient.post(`/api/finance/payments/${id}/paid`, { paid }).then(r => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

/** Отсрочка поставщика — срок для накладных, где поставщик его не указал */
export const useSaveSupplierTerms = () => {
  const qc = useQueryClient();
  return useMutation<unknown, Error, { inn: string; name: string | null; paymentDelayDays: number | null }>({
    mutationFn: body => apiClient.put('/api/finance/suppliers/terms', body).then(r => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

// ==================== Поставщики ====================

export interface SupplierRow {
  inn: string;
  supplierId: number | null;
  name: string;
  paymentDelayDays: number | null;
  hasBank: boolean;
  invoices: number;
  unpaidCount: number;
  unpaidTotal: number;
  overdueTotal: number;
  /** Сколько неоплаченных накладных без срока */
  noDueCount: number;
  nextDueDate: string | null;
}

export interface SupplierInvoice {
  id: number;
  number: string | null;
  date: string | null;
  total: number;
  dueDate: string | null;
  daysLeft: number | null;
  bucket: PaymentBucket;
  paidAt: string | null;
}

export interface SupplierBank {
  account: string | null;
  bik: string | null;
  corrAccount: string | null;
  bankName: string | null;
}

export interface SupplierDetail {
  inn: string;
  supplierId: number | null;
  name: string;
  kpp: string | null;
  paymentDelayDays: number | null;
  bank: SupplierBank;
  unpaidTotal: number;
  invoices: SupplierInvoice[];
}

export const useFinanceSuppliers = () =>
  useQuery<SupplierRow[]>({
    queryKey: [KEY, 'suppliers'],
    queryFn: () => apiClient.get('/api/finance/suppliers').then(r => r.data),
  });

export const useFinanceSupplier = (inn: string | null) =>
  useQuery<SupplierDetail>({
    queryKey: [KEY, 'supplier', inn],
    queryFn: () => apiClient.get(`/api/finance/suppliers/${encodeURIComponent(inn!)}`).then(r => r.data),
    enabled: !!inn,
  });

/** Реквизиты поставщика вручную; пустое поле — очистить */
export const useSaveSupplierRequisites = () => {
  const qc = useQueryClient();
  return useMutation<unknown, Error, { inn: string; name: string | null } & SupplierBank>({
    mutationFn: ({ inn, name, account, bik, corrAccount, bankName }) =>
      apiClient.put('/api/finance/suppliers/requisites', { inn, name, bankAccount: account, bik, corrAccount, bankName }).then(r => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};
