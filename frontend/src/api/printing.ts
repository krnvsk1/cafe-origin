import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';

/** Принтер счетов для гостя (пречек) и программа печати на кассовом ПК */
export interface PrintingState {
  enabled: boolean;
  /** agent — USB к кассовому ПК; agent-lan — принтер в сети, печатает программа на ПК; network — принтер в сети, печатает сервер */
  mode: 'agent' | 'agent-lan' | 'network';
  /** Касса АТОЛ и УТМ ЕГАИС — через программу на кассовом ПК (сервер в интернете) */
  relayDevices: boolean;
  /** Касса через драйвер ККТ 10 на кассовом ПК (без АТОЛ Web Server) */
  atolViaDriver: boolean;
  host: string | null;
  port: number;
  printerName: string;
  width: number;
  codeTable: number;
  encoding: 866 | 1251;
  footer: string | null;
  /** Вид счёта для гостя */
  layout: PrecheckLayout;
  agentSeenAt: string | null;
  agentHost: string | null;
  recent: { id: number; kind: string; title: string; status: 'pending' | 'taken' | 'done' | 'error'; error: string | null; createdAt: string; doneAt: string | null }[];
}

export type PrintingInput = Pick<PrintingState, 'enabled' | 'mode' | 'relayDevices' | 'atolViaDriver' | 'host' | 'port' | 'printerName' | 'width' | 'codeTable' | 'encoding' | 'footer'>;

const KEY = ['printing'];

export const usePrintingState = () =>
  useQuery<PrintingState>({
    queryKey: KEY,
    queryFn: () => apiClient.get('/api/admin/printing').then(r => r.data),
    refetchInterval: 10_000,
  });

export const useSavePrinting = () => {
  const qc = useQueryClient();
  return useMutation<PrintingState, Error, PrintingInput>({
    mutationFn: body => apiClient.put('/api/admin/printing', body).then(r => r.data),
    onSuccess: data => qc.setQueryData(KEY, data),
  });
};

/** Строка счёта для предпросмотра: align 0 — слева, 1 — по центру, 2 — справа */
export interface PreviewLine { text: string; align: number; bold: boolean; big: boolean }
/** Строка шапки или подвала счёта */
export interface PrecheckLayoutLine { text: string; align: 'left' | 'center' | 'right'; bold: boolean; large: boolean }
/** Вид счёта для гостя: шапка, поля, подписи, подвал. «» печатается всегда */
export interface PrecheckLayout {
  header: PrecheckLayoutLine[];
  showDate: boolean;
  showGuests: boolean;
  guestsLabel: string;
  showWaiter: boolean;
  waiterLabel: string;
  showPrices: boolean;
  subtotalLabel: string;
  totalLabel: string;
  dueLabel: string;
  footer: PrecheckLayoutLine[];
}
export const PRECHECK_VARS = [
  { key: '{brand}', label: 'название заведения' },
  { key: '{title}', label: 'заголовок (СЧЁТ / БАНКЕТ)' },
  { key: '{table}', label: 'стол' },
  { key: '{check}', label: 'номер счёта' },
  { key: '{waiter}', label: 'официант' },
  { key: '{guests}', label: 'гостей' },
  { key: '{date}', label: 'дата' },
  { key: '{time}', label: 'время' },
];

export const usePrecheckPreview = (width: number, layout: PrecheckLayout | null, enabled: boolean) =>
  useQuery<{ width: number; lines: PreviewLine[] }>({
    queryKey: ['printing-preview', width, layout],
    queryFn: () => apiClient.post('/api/admin/printing/preview', { width, layout }).then(r => r.data),
    enabled,
    placeholderData: prev => prev,
    staleTime: 60_000,
  });

export const useSavePrecheckLayout = () => {
  const qc = useQueryClient();
  return useMutation<PrintingState, Error, PrecheckLayout>({
    mutationFn: layout => apiClient.put('/api/admin/printing/layout', layout).then(r => r.data),
    onSuccess: data => qc.setQueryData(KEY, data),
  });
};

export const printPrecheckSample = (layout: PrecheckLayout) =>
  apiClient.post('/api/admin/printing/layout/sample', layout).then(r => r.data);

export const useTestPrint = () => {
  const qc = useQueryClient();
  return useMutation<{ id: number }, Error, void>({
    mutationFn: () => apiClient.post('/api/admin/printing/test').then(r => r.data),
    onSettled: () => qc.invalidateQueries({ queryKey: KEY }),
  });
};

export const useNewPrintToken = () =>
  useMutation<void, Error, void>({ mutationFn: () => apiClient.post('/api/admin/printing/token').then(() => undefined) });

/** Скачать программу печати (PowerShell) с адресом этого сайта и ключом */
export const downloadPrintAgent = async () => {
  const res = await apiClient.get('/api/admin/printing/agent', { params: { base: window.location.origin }, responseType: 'blob' });
  const url = URL.createObjectURL(res.data as Blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'cafe-print.ps1';
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
};

/** Программа печати на связи: заходила за заданиями меньше минуты назад */
export const agentOnline = (s?: PrintingState) => !!s?.agentSeenAt && Date.now() - new Date(s.agentSeenAt.endsWith('Z') ? s.agentSeenAt : `${s.agentSeenAt}Z`).getTime() < 60_000;
