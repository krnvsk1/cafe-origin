import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';

export type HaccpFieldType = 'text' | 'textarea' | 'number' | 'temperature' | 'humidity' | 'bool' | 'select' | 'datetime' | 'date' | 'dish';

export interface HaccpField {
  key: string;
  label: string;
  type: HaccpFieldType;
  required: boolean;
  options: string[] | null;
  unit: string | null;
  min: number | null;
  max: number | null;
  badOptions: string[] | null;
  badIf: boolean | null;
  default: string | null;
  hint: string | null;
}

export type HaccpObjectKind = 'fridge' | 'storage' | 'fryer' | 'ccp';

export interface HaccpJournal {
  key: string;
  title: string;
  short: string;
  basis: string;
  frequency: 'per_object' | 'per_staff' | 'daily' | 'per_receipt';
  objectKind: HaccpObjectKind | null;
  usesStaff: boolean;
  fields: HaccpField[];
  description: string | null;
}

export interface HaccpObject {
  id: number;
  kind: HaccpObjectKind;
  name: string;
  location: string | null;
  minValue: number | null;
  maxValue: number | null;
  minHumidity: number | null;
  maxHumidity: number | null;
  checksPerDay: number;
  note: string | null;
  isActive: boolean;
  sortOrder: number;
}

export interface HaccpStaff {
  id: number;
  fullName: string;
  position: string | null;
  medicalBookNumber: string | null;
  medicalExamUntil: string | null;
  hygieneTrainingUntil: string | null;
  isActive: boolean;
  note: string | null;
}

export interface HaccpMeta {
  canManage: boolean;
  journals: HaccpJournal[];
  objects: HaccpObject[];
  staff: HaccpStaff[];
  dishes: string[];
  editWindowHours: number;
}

export type Values = Record<string, string | number | boolean | null>;

export interface HaccpEntry {
  id: number;
  journal: string;
  objectId: number | null;
  staffId: number | null;
  stockDocumentId: number | null;
  occurredAt: string;
  values: Values;
  hasDeviation: boolean;
  deviation: string | null;
  correctiveAction: string | null;
  createdByName: string | null;
  createdAt: string;
  updatedAt: string | null;
  version: number;
  isAnnulled: boolean;
  annulReason: string | null;
  late: boolean;
  canEdit: boolean;
}

export interface HaccpRevision {
  version: number;
  action: 'edit' | 'annul';
  occurredAt: string;
  values: Values;
  correctiveAction: string | null;
  reason: string;
  changedByName: string | null;
  changedAt: string;
}

export interface TodayTask {
  journal: string;
  title: string;
  objectId: number | null;
  objectName: string | null;
  expected: number;
  done: number;
  status: 'done' | 'partial' | 'todo';
  hint: string | null;
}

export interface HaccpToday {
  date: string;
  tasks: TodayTask[];
  staff: { staffId: number; fullName: string; what: string; until: string; expired: boolean }[];
  receipts: { stockDocumentId: number; number: string | null; date: string; supplier: string | null; lines: number }[];
  deviations: number;
}

export interface HaccpReceipt {
  id: number;
  number: string | null;
  date: string;
  supplier: string | null;
  lines: { name: string; unit: string; quantity: number }[];
  isChecked: boolean;
}

export interface EntryInput {
  journal: string;
  objectId?: number | null;
  staffId?: number | null;
  stockDocumentId?: number | null;
  occurredAt?: string | null;
  values: Values;
  correctiveAction?: string | null;
  reason?: string | null;
}

const KEY = 'haccp';

export const useHaccpMeta = () =>
  useQuery<HaccpMeta>({ queryKey: [KEY, 'meta'], queryFn: () => apiClient.get('/api/haccp/meta').then(r => r.data) });

export const useHaccpToday = (enabled = true) =>
  useQuery<HaccpToday>({
    queryKey: [KEY, 'today'],
    queryFn: () => apiClient.get('/api/haccp/today').then(r => r.data),
    refetchInterval: 60_000,
    refetchOnWindowFocus: true,
    enabled,
  });

export const useHaccpEntries = (journal: string | null, from: string, to: string, objectId: number | null, deviationsOnly: boolean) =>
  useQuery<HaccpEntry[]>({
    queryKey: [KEY, 'entries', journal, from, to, objectId, deviationsOnly],
    queryFn: () => apiClient.get('/api/haccp/entries', { params: { journal, from, to, objectId: objectId ?? undefined, deviationsOnly } }).then(r => r.data),
    enabled: !!journal,
  });

export const fetchHaccpHistory = (id: number) => apiClient.get<HaccpRevision[]>(`/api/haccp/entries/${id}/history`).then(r => r.data);

export const fetchHaccpReceipts = (days = 14) => apiClient.get<HaccpReceipt[]>('/api/haccp/receipts', { params: { days } }).then(r => r.data);

export const createHaccpEntries = (entries: EntryInput[]) => apiClient.post<HaccpEntry[]>('/api/haccp/entries', entries).then(r => r.data);
export const updateHaccpEntry = (id: number, input: EntryInput) => apiClient.put<HaccpEntry>(`/api/haccp/entries/${id}`, input).then(r => r.data);
export const annulHaccpEntry = (id: number, reason: string) => apiClient.post<HaccpEntry>(`/api/haccp/entries/${id}/annul`, { reason }).then(r => r.data);

export const saveHaccpObject = (o: Partial<HaccpObject> & { kind: HaccpObjectKind; name: string }) =>
  (o.id ? apiClient.put<HaccpObject>(`/api/haccp/objects/${o.id}`, o) : apiClient.post<HaccpObject>('/api/haccp/objects', o)).then(r => r.data);
export const deleteHaccpObject = (id: number) => apiClient.delete(`/api/haccp/objects/${id}`);
export const saveHaccpStaff = (s: Partial<HaccpStaff> & { fullName: string }) =>
  (s.id ? apiClient.put<HaccpStaff>(`/api/haccp/staff/${s.id}`, s) : apiClient.post<HaccpStaff>('/api/haccp/staff', s)).then(r => r.data);
export const deleteHaccpStaff = (id: number) => apiClient.delete(`/api/haccp/staff/${id}`);

/** Скачать журнал за период в Excel */
export const downloadHaccpExcel = async (journal: string, from: string, to: string) => {
  const r = await apiClient.get('/api/haccp/export', { params: { journal, from, to }, responseType: 'blob' });
  const disposition = String(r.headers['content-disposition'] ?? '');
  const m = /filename\*=UTF-8''([^;]+)/i.exec(disposition) ?? /filename="?([^";]+)"?/i.exec(disposition);
  const name = m ? decodeURIComponent(m[1]) : `${journal}.xlsx`;
  const url = URL.createObjectURL(r.data as Blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
};

export const useInvalidateHaccp = () => {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: [KEY] });
};

export const OBJECT_KINDS: Record<HaccpObjectKind, { title: string; one: string; defaults: Partial<HaccpObject> }> = {
  fridge: { title: 'Холодильное оборудование', one: 'Холодильник', defaults: { minValue: 2, maxValue: 6, checksPerDay: 2 } },
  storage: { title: 'Склады', one: 'Склад', defaults: { minValue: 2, maxValue: 25, minHumidity: 0, maxHumidity: 75, checksPerDay: 1 } },
  fryer: { title: 'Фритюрницы', one: 'Фритюрница', defaults: { checksPerDay: 1 } },
  ccp: { title: 'Контрольные точки ХАССП', one: 'Контрольная точка', defaults: { minValue: 75, checksPerDay: 1 } },
};

// ============ Даты (по Москве) ============

const pad = (n: number) => String(n).padStart(2, '0');
/** Москва: UTC+3 без перехода на летнее время */
const msk = (d: Date) => new Date(d.getTime() + 3 * 3600_000);
export const todayMsk = () => { const m = msk(new Date()); return `${m.getUTCFullYear()}-${pad(m.getUTCMonth() + 1)}-${pad(m.getUTCDate())}`; };
export const monthStartMsk = () => todayMsk().slice(0, 8) + '01';
export const fmtMsk = (iso: string | null | undefined, withTime = true) => {
  if (!iso) return '';
  const m = msk(new Date(iso));
  const date = `${pad(m.getUTCDate())}.${pad(m.getUTCMonth() + 1)}.${m.getUTCFullYear()}`;
  return withTime ? `${date} ${pad(m.getUTCHours())}:${pad(m.getUTCMinutes())}` : date;
};
/** ISO (UTC) → значение input type="datetime-local" по Москве */
export const toLocalInput = (iso?: string | null) => {
  const m = msk(iso ? new Date(iso) : new Date());
  return `${m.getUTCFullYear()}-${pad(m.getUTCMonth() + 1)}-${pad(m.getUTCDate())}T${pad(m.getUTCHours())}:${pad(m.getUTCMinutes())}`;
};
/** Значение datetime-local (Москва) → ISO UTC */
export const fromLocalInput = (v: string) => (v ? new Date(`${v}:00+03:00`).toISOString() : null);

/** Норма объекта текстом: «+2…+6 °C» */
export const rangeText = (min: number | null | undefined, max: number | null | undefined, unit = '°C') => {
  const s = (v: number) => (v > 0 ? `+${v}` : `${v}`);
  if (min != null && max != null) return `${s(min)}…${s(max)} ${unit}`;
  if (min != null) return `не ниже ${s(min)} ${unit}`;
  if (max != null) return `не выше ${s(max)} ${unit}`;
  return '';
};

/** Значение графы для показа */
export const displayValue = (f: HaccpField, v: Values[string] | undefined) => {
  if (v === null || v === undefined || v === '') return '';
  if (f.type === 'bool') return v === true || v === 'true' ? 'Да' : 'Нет';
  if (f.type === 'datetime') return fmtMsk(String(v));
  if (f.type === 'date') { const [y, m, d] = String(v).slice(0, 10).split('-'); return d ? `${d}.${m}.${y}` : String(v); }
  if (f.unit && (f.type === 'number' || f.type === 'temperature' || f.type === 'humidity')) return `${String(v).replace('.', ',')} ${f.unit}`;
  return String(v);
};

/** Предварительная проверка в форме (сервер проверяет так же) — чтобы сразу показать отклонение и попросить корректирующее действие */
export const predictDeviations = (journal: HaccpJournal, values: Values, obj: HaccpObject | null | undefined, staff: HaccpStaff | null | undefined) => {
  const out: string[] = [];
  for (const f of journal.fields) {
    const v = values[f.key];
    if (v === null || v === undefined || v === '') continue;
    if (f.type === 'temperature' || f.type === 'humidity' || f.type === 'number') {
      const n = Number(String(v).replace(',', '.'));
      if (!Number.isFinite(n)) continue;
      const min = f.min ?? (f.type === 'temperature' ? obj?.minValue : f.type === 'humidity' ? obj?.minHumidity : null);
      const max = f.max ?? (f.type === 'temperature' ? obj?.maxValue : f.type === 'humidity' ? obj?.maxHumidity : null);
      if ((min != null && n < min) || (max != null && n > max)) out.push(`${f.label}: норма ${rangeText(min, max, f.unit ?? '')}`);
    } else if (f.type === 'bool' && f.badIf !== null && (v === true || v === 'true') === f.badIf) {
      out.push(f.label);
    } else if (f.type === 'select' && f.badOptions?.includes(String(v))) {
      out.push(`${f.label}: ${v}`);
    }
  }
  if (journal.key === 'perishable' && values.sellBy && String(values.sellBy) < todayMsk()) out.push('Срок реализации истёк');
  if (journal.usesStaff && staff) {
    const t = todayMsk();
    if (staff.medicalExamUntil && staff.medicalExamUntil.slice(0, 10) < t) out.push('Медосмотр просрочен');
    if (staff.hygieneTrainingUntil && staff.hygieneTrainingUntil.slice(0, 10) < t) out.push('Гигиеническая аттестация просрочена');
  }
  return out;
};

/** Начальные значения граф по умолчанию */
export const defaultValues = (journal: HaccpJournal): Values =>
  Object.fromEntries(journal.fields.filter(f => f.default !== null).map(f => [f.key, f.type === 'bool' ? f.default === 'true' : f.default]));
