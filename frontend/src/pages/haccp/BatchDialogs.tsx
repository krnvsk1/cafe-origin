import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, TextField, Box, Typography, IconButton, Alert, Checkbox,
  CircularProgress, ToggleButtonGroup, ToggleButton, MenuItem, Chip, useMediaQuery, useTheme,
} from '@mui/material';
import { Close as CloseIcon } from '@mui/icons-material';
import {
  createHaccpEntries, fetchHaccpReceipts, todayMsk, fmtMsk, useInvalidateHaccp, predictDeviations, rangeText,
  type HaccpMeta, type HaccpReceipt, type EntryInput, type HaccpJournal, type Values,
} from '../../api/haccp';
import { FieldInput } from './EntryDialog';
import { apiError, fmtQty, UNIT_LABELS, type StockUnit } from '../../api/inventory';
import { tokens } from '../../theme';

const DialogHead: React.FC<{ title: string; subtitle: string; onClose: () => void; busy: boolean }> = ({ title, subtitle, onClose, busy }) => (
  <DialogTitle sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, pr: 1.5 }}>
    <Box sx={{ flex: 1, minWidth: 0 }}>
      <Typography variant="body2" color="text.secondary">{subtitle}</Typography>
      {title}
    </Box>
    <IconButton aria-label="Закрыть" onClick={onClose} disabled={busy}><CloseIcon /></IconButton>
  </DialogTitle>
);

// ============ Гигиенический журнал: вся смена разом ============

interface HealthRow { onShift: boolean; healthy: boolean; skin: boolean; corrective: string }

export const HealthBatchDialog: React.FC<{ open: boolean; meta: HaccpMeta; alreadyToday: number[]; onClose: () => void }> = ({ open, meta, alreadyToday, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const invalidate = useInvalidateHaccp();
  const staff = meta.staff.filter(s => s.isActive);
  const [rows, setRows] = React.useState<Record<number, HealthRow>>({});
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState('');
  const today = todayMsk();

  React.useEffect(() => {
    if (!open) return;
    setError('');
    setRows(Object.fromEntries(staff.map(s => [s.id, { onShift: false, healthy: true, skin: true, corrective: '' }])));
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const set = (id: number, patch: Partial<HealthRow>) => setRows(r => ({ ...r, [id]: { ...r[id], ...patch } }));
  const chosen = staff.filter(s => rows[s.id]?.onShift);

  const submit = async () => {
    setError('');
    if (chosen.length === 0) { setError('Отметьте, кто вышел на смену'); return; }
    const entries: EntryInput[] = chosen.map(s => {
      const r = rows[s.id];
      const expired = (s.medicalExamUntil && s.medicalExamUntil.slice(0, 10) < today) || (s.hygieneTrainingUntil && s.hygieneTrainingUntil.slice(0, 10) < today);
      const ok = r.healthy && r.skin && !expired;
      return {
        journal: 'health',
        staffId: s.id,
        values: { noInfection: r.healthy, noSkin: r.skin, result: ok ? 'Допущен' : 'Отстранён' },
        correctiveAction: ok ? null : (r.corrective.trim() || 'Отстранён от работы до выздоровления / продления медкнижки'),
      };
    });
    setBusy(true);
    try {
      await createHaccpEntries(entries);
      await invalidate();
      onClose();
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="sm" fullWidth>
      <DialogHead title="Осмотр перед сменой" subtitle="Гигиенический журнал" onClose={onClose} busy={busy} />
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
        {staff.length === 0 && <Alert severity="warning">Добавьте сотрудников во вкладке «Настройка»</Alert>}
        <Typography variant="body2" color="text.secondary">
          Отметьте, кто вышел на смену. Работник подтверждает, что у него и членов семьи нет признаков инфекций, а на коже рук нет гнойничков.
        </Typography>
        {staff.map(s => {
          const r = rows[s.id];
          if (!r) return null;
          const expired = (s.medicalExamUntil && s.medicalExamUntil.slice(0, 10) < today) || (s.hygieneTrainingUntil && s.hygieneTrainingUntil.slice(0, 10) < today);
          const ok = r.healthy && r.skin && !expired;
          return (
            <Box key={s.id} sx={{ borderTop: '1px solid', borderColor: 'divider', py: 1, opacity: r.onShift ? 1 : 0.7 }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Checkbox checked={r.onShift} onChange={e => set(s.id, { onShift: e.target.checked })} />
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography sx={{ fontWeight: 600 }}>{s.fullName}</Typography>
                  <Typography variant="body2" color="text.secondary">{s.position}</Typography>
                </Box>
                {alreadyToday.includes(s.id) && <Chip size="small" label="уже отмечен" />}
                {expired && <Chip size="small" color="error" label="медкнижка просрочена" />}
              </Box>
              {r.onShift && (
                <Box sx={{ pl: 6, display: 'flex', flexDirection: 'column', gap: 1 }}>
                  <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
                    <ToggleButtonGroup size="small" exclusive value={r.healthy ? 'y' : 'n'} onChange={(_, v) => v && set(s.id, { healthy: v === 'y' })}>
                      <ToggleButton value="y" color="success">Нет признаков инфекций</ToggleButton>
                      <ToggleButton value="n" color="error">Есть</ToggleButton>
                    </ToggleButtonGroup>
                    <ToggleButtonGroup size="small" exclusive value={r.skin ? 'y' : 'n'} onChange={(_, v) => v && set(s.id, { skin: v === 'y' })}>
                      <ToggleButton value="y" color="success">Кожа чистая</ToggleButton>
                      <ToggleButton value="n" color="error">Гнойнички</ToggleButton>
                    </ToggleButtonGroup>
                  </Box>
                  <Typography variant="body2" sx={{ fontWeight: 700, color: ok ? tokens.herb : tokens.chili }}>{ok ? 'Допущен' : 'Отстранён'}</Typography>
                  {!ok && (
                    <TextField size="small" label="Что сделали" value={r.corrective} onChange={e => set(s.id, { corrective: e.target.value })}
                      placeholder="Отстранён от работы, направлен к врачу" />
                  )}
                </Box>
              )}
            </Box>
          );
        })}
        {error && <Alert severity="error">{error}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        <Button onClick={() => setRows(r => Object.fromEntries(Object.entries(r).map(([k, v]) => [k, { ...v, onShift: true }])))} sx={{ mr: 'auto' }}>
          Все на смене
        </Button>
        <Button variant="contained" color="secondary" size="large" onClick={submit} disabled={busy || chosen.length === 0}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Записать · {chosen.length}
        </Button>
      </DialogActions>
    </Dialog>
  );
};

// ============ Бракераж сырья по приходу ============

interface PerishableRow { include: boolean; product: string; quantity: string; sellBy: string; ok: boolean; manufacturer: string }

export const PerishableReceiptDialog: React.FC<{ open: boolean; presetReceiptId?: number | null; onClose: () => void }> = ({ open, presetReceiptId, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const invalidate = useInvalidateHaccp();
  const [receipts, setReceipts] = React.useState<HaccpReceipt[] | null>(null);
  const [receiptId, setReceiptId] = React.useState<number | ''>('');
  const [rows, setRows] = React.useState<PerishableRow[]>([]);
  const [safetyDoc, setSafetyDoc] = React.useState('');
  const [storage, setStorage] = React.useState('+2…+6 °C');
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState('');

  React.useEffect(() => {
    if (!open) return;
    setError('');
    setReceipts(null);
    setRows([]);
    setSafetyDoc('');
    fetchHaccpReceipts(14).then(list => {
      setReceipts(list);
      const id = presetReceiptId ?? list.find(r => !r.isChecked)?.id ?? '';
      setReceiptId(id);
    }).catch(e => setError(apiError(e)));
  }, [open, presetReceiptId]);

  const receipt = receipts?.find(r => r.id === receiptId) ?? null;

  React.useEffect(() => {
    if (!receipt) return;
    setRows(receipt.lines.map(l => ({
      include: true,
      product: l.name,
      quantity: `${fmtQty(l.quantity)} ${UNIT_LABELS[l.unit as StockUnit] ?? l.unit}`,
      sellBy: '',
      ok: true,
      manufacturer: '',
    })));
    setSafetyDoc(receipt.number ? `УПД № ${receipt.number}` : '');
  }, [receipt?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const set = (i: number, patch: Partial<PerishableRow>) => setRows(rs => rs.map((r, j) => (j === i ? { ...r, ...patch } : r)));
  const chosen = rows.filter(r => r.include);

  const submit = async () => {
    setError('');
    if (!receipt) return;
    if (!safetyDoc.trim()) { setError('Укажите документ, подтверждающий безопасность (декларация, ветсертификат, УПД)'); return; }
    const noDate = chosen.find(r => !r.sellBy);
    if (noDate) { setError(`Укажите конечный срок реализации: «${noDate.product}»`); return; }
    setBusy(true);
    try {
      await createHaccpEntries(chosen.map(r => ({
        journal: 'perishable',
        stockDocumentId: receipt.id,
        values: {
          product: r.product, supplier: receipt.supplier ?? '—', quantity: r.quantity, safetyDoc: safetyDoc.trim(),
          organoleptic: r.ok ? 'Соответствует' : 'Не соответствует', storage: storage.trim() || null, sellBy: r.sellBy,
          manufacturer: r.manufacturer.trim() || null,
        },
        correctiveAction: r.ok && r.sellBy >= todayMsk() ? null : 'Продукция не принята, возврат поставщику',
      })));
      await invalidate();
      onClose();
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="md" fullWidth>
      <DialogHead title="Бракераж сырья по приходу" subtitle="Журнал бракеража скоропортящейся продукции" onClose={onClose} busy={busy} />
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, pt: '8px !important' }}>
        {receipts === null && !error ? <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box> : (
          <>
            {receipts && receipts.length === 0 && <Alert severity="info">За 2 недели проведённых приходов нет</Alert>}
            {receipts && receipts.length > 0 && (
              <TextField select label="Приход" value={receiptId} onChange={e => setReceiptId(Number(e.target.value))}>
                {receipts.map(r => (
                  <MenuItem key={r.id} value={r.id}>
                    {fmtMsk(r.date, false)} · {r.supplier ?? 'без поставщика'}{r.number ? ` · № ${r.number}` : ''}{r.isChecked ? ' · уже проверен' : ''}
                  </MenuItem>
                ))}
              </TextField>
            )}
            {receipt && (
              <>
                <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0,1fr)', sm: '2fr 1fr' }, gap: 1.5 }}>
                  <TextField label="Документ, подтверждающий безопасность *" value={safetyDoc} onChange={e => setSafetyDoc(e.target.value)}
                    helperText="Декларация, ветсертификат (Меркурий), УПД" />
                  <TextField label="Условия хранения" value={storage} onChange={e => setStorage(e.target.value)} />
                </Box>
                {rows.map((r, i) => (
                  <Box key={i} sx={{ borderTop: '1px solid', borderColor: 'divider', pt: 1, display: 'grid', gap: 1,
                    gridTemplateColumns: { xs: 'auto minmax(0,1fr)', md: 'auto minmax(0,1.6fr) 110px 170px auto' }, alignItems: 'center', opacity: r.include ? 1 : 0.5 }}>
                    <Checkbox checked={r.include} onChange={e => set(i, { include: e.target.checked })} />
                    <Box sx={{ minWidth: 0 }}>
                      <Typography sx={{ fontWeight: 600 }} noWrap>{r.product}</Typography>
                      <TextField size="small" variant="standard" placeholder="Изготовитель" value={r.manufacturer} onChange={e => set(i, { manufacturer: e.target.value })} fullWidth />
                    </Box>
                    <Typography sx={{ gridColumn: { xs: '2', md: 'auto' } }}>{r.quantity}</Typography>
                    <TextField size="small" type="date" label="Годен до *" value={r.sellBy} onChange={e => set(i, { sellBy: e.target.value })}
                      error={!!r.sellBy && r.sellBy < todayMsk()} disabled={!r.include}
                      slotProps={{ inputLabel: { shrink: true } }} sx={{ gridColumn: { xs: '2', md: 'auto' } }} />
                    <ToggleButtonGroup size="small" exclusive value={r.ok ? 'y' : 'n'} onChange={(_, v) => v && set(i, { ok: v === 'y' })} disabled={!r.include}
                      sx={{ gridColumn: { xs: '2', md: 'auto' } }}>
                      <ToggleButton value="y" color="success">Соответствует</ToggleButton>
                      <ToggleButton value="n" color="error">Нет</ToggleButton>
                    </ToggleButtonGroup>
                  </Box>
                ))}
              </>
            )}
          </>
        )}
        {error && <Alert severity="error">{error}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button variant="contained" color="secondary" size="large" onClick={submit} disabled={busy || !receipt || chosen.length === 0}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Записать · {chosen.length}
        </Button>
      </DialogActions>
    </Dialog>
  );
};

// ============ Все холодильники / склады / точки — одним экраном ============

interface ObjRow { values: Values; corrective: string }

/** Замеры по всему оборудованию журнала за один заход: в каждой строке — только сами показания */
export const MultiObjectDialog: React.FC<{ open: boolean; meta: HaccpMeta; journal: HaccpJournal; onClose: () => void }> = ({ open, meta, journal, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const invalidate = useInvalidateHaccp();
  const objects = meta.objects.filter(o => o.kind === journal.objectKind && o.isActive);
  const fields = journal.fields.filter(f => f.required);
  const [rows, setRows] = React.useState<Record<number, ObjRow>>({});
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState('');

  React.useEffect(() => {
    if (!open) return;
    setError('');
    const remembered = meta.lastValues?.[journal.key] ?? {};
    setRows(Object.fromEntries(objects.map(o => [o.id, { values: { ...remembered }, corrective: '' }])));
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const set = (id: number, patch: Partial<ObjRow>) => setRows(r => ({ ...r, [id]: { ...r[id], ...patch } }));
  const filled = objects.filter(o => fields.every(f => {
    const v = rows[o.id]?.values[f.key];
    return v !== undefined && v !== null && v !== '';
  }));

  const submit = async () => {
    setError('');
    if (filled.length === 0) { setError('Внесите показания хотя бы по одному'); return; }
    const missing = filled.find(o => predictDeviations(journal, rows[o.id].values, o, null).length > 0 && !rows[o.id].corrective.trim());
    if (missing) { setError(`«${missing.name}»: отклонение — опишите, что сделали`); return; }
    setBusy(true);
    try {
      await createHaccpEntries(filled.map(o => ({
        journal: journal.key,
        objectId: o.id,
        values: rows[o.id].values,
        correctiveAction: rows[o.id].corrective.trim() || null,
      })));
      await invalidate();
      onClose();
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="sm" fullWidth>
      <DialogHead title={`${journal.short}: все разом`} subtitle={journal.title} onClose={onClose} busy={busy} />
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
        {objects.length === 0 && <Alert severity="warning">Добавьте оборудование во вкладке «Настройка»</Alert>}
        <Typography variant="body2" color="text.secondary">Внесите показания по каждому. Пустые строки не сохраняются.</Typography>
        {objects.map(o => {
          const r = rows[o.id];
          if (!r) return null;
          const dev = predictDeviations(journal, r.values, o, null);
          return (
            <Box key={o.id} sx={{ borderTop: '1px solid', borderColor: 'divider', py: 1.25, display: 'flex', flexDirection: 'column', gap: 1 }}>
              <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1, flexWrap: 'wrap' }}>
                <Typography sx={{ fontWeight: 700 }}>{o.name}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {[o.location, rangeText(o.minValue, o.maxValue) && `норма ${rangeText(o.minValue, o.maxValue)}`].filter(Boolean).join(' · ')}
                </Typography>
              </Box>
              <Box sx={{ display: 'grid', gridTemplateColumns: `repeat(${Math.min(fields.length, 2)}, minmax(0, 1fr))`, gap: 1 }}>
                {fields.map(f => (
                  <FieldInput key={f.key} field={f} value={r.values[f.key]} compact dishes={meta.dishes}
                    onChange={v => set(o.id, { values: { ...r.values, [f.key]: v } })}
                    bad={predictDeviations({ ...journal, key: '', usesStaff: false, fields: [f] }, r.values, o, null).length > 0} />
                ))}
              </Box>
              {dev.length > 0 && (
                <TextField size="small" label="Отклонение — что сделали *" value={r.corrective} onChange={e => set(o.id, { corrective: e.target.value })}
                  error placeholder="Продукты перенесены, вызван мастер, повторный замер через 30 минут" helperText={dev.join('; ')} />
              )}
            </Box>
          );
        })}
        {error && <Alert severity="error">{error}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button variant="contained" color="secondary" size="large" onClick={submit} disabled={busy || filled.length === 0}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Записать · {filled.length}
        </Button>
      </DialogActions>
    </Dialog>
  );
};
