import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';

/** Принтер счетов для гостя (пречек) и программа печати на кассовом ПК */
export interface PrintingState {
  enabled: boolean;
  /** agent — USB к кассовому ПК; agent-lan — принтер в сети, печатает программа на ПК; network — принтер в сети, печатает сервер */
  mode: 'agent' | 'agent-lan' | 'network';
  /** Касса АТОЛ и УТМ ЕГАИС — через программу на кассовом ПК (сервер в интернете) */
  relayDevices: boolean;
  host: string | null;
  port: number;
  printerName: string;
  width: number;
  codeTable: number;
  encoding: 866 | 1251;
  footer: string | null;
  agentSeenAt: string | null;
  agentHost: string | null;
  recent: { id: number; kind: string; title: string; status: 'pending' | 'taken' | 'done' | 'error'; error: string | null; createdAt: string; doneAt: string | null }[];
}

export type PrintingInput = Pick<PrintingState, 'enabled' | 'mode' | 'relayDevices' | 'host' | 'port' | 'printerName' | 'width' | 'codeTable' | 'encoding' | 'footer'>;

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
