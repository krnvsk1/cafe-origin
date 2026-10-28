import * as React from 'react';
import {
  Box, Typography, Button, TextField, MenuItem, Chip, CircularProgress, Alert, IconButton, Menu, FormControlLabel, Switch,
  Dialog, DialogTitle, DialogContent, DialogActions, useTheme,
} from '@mui/material';
import {
  Add as AddIcon, MoreVert as MoreIcon, PrintOutlined as PrintIcon, FileDownloadOutlined as ExcelIcon,
  WarningAmberRounded as WarnIcon, History as HistoryIcon,
} from '@mui/icons-material';
import {
  useHaccpEntries, annulHaccpEntry, fetchHaccpHistory, downloadHaccpExcel, displayValue, fmtMsk, rangeText, monthStartMsk, todayMsk,
  useInvalidateHaccp, type HaccpMeta, type HaccpJournal, type HaccpEntry, type HaccpRevision,
} from '../../api/haccp';
import { apiError } from '../../api/inventory';
import { tokens } from '../../theme';
import { Panel, EmptyState } from '../inventory/ui';
import EntryDialog from './EntryDialog';
import { HealthBatchDialog, PerishableReceiptDialog } from './BatchDialogs';
import { printJournal } from './print';

// ============ История записи ============

const HistoryDialog: React.FC<{ entry: HaccpEntry | null; journal: HaccpJournal; onClose: () => void }> = ({ entry, journal, onClose }) => {
  const [list, setList] = React.useState<HaccpRevision[] | null>(null);
  React.useEffect(() => {
    setList(null);
    if (entry) fetchHaccpHistory(entry.id).then(setList).catch(() => setList([]));
  }, [entry]);
  return (
    <Dialog open={!!entry} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>История записи</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        {entry && (
          <Typography variant="body2" color="text.secondary">
            Внесена {fmtMsk(entry.createdAt)} — {entry.createdByName}. Текущая редакция: {entry.version}.
          </Typography>
        )}
        {list === null ? <CircularProgress sx={{ alignSelf: 'center' }} /> : list.length === 0
          ? <Typography>Запись не исправлялась</Typography>
          : list.map(r => (
            <Box key={r.version} sx={{ bgcolor: 'action.hover', borderRadius: '12px', p: 1.5 }}>
              <Typography sx={{ fontWeight: 700 }}>
                {r.action === 'annul' ? 'Аннулирована' : `Исправлена редакция ${r.version}`} · {fmtMsk(r.changedAt)} · {r.changedByName}
              </Typography>
              <Typography variant="body2">Причина: {r.reason}</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                Было: {fmtMsk(r.occurredAt)}; {journal.fields.map(f => ({ f, v: displayValue(f, r.values[f.key]) })).filter(x => x.v).map(x => `${x.f.label}: ${x.v}`).join('; ')}
                {r.correctiveAction ? `; корректирующее действие: ${r.correctiveAction}` : ''}
              </Typography>
            </Box>
          ))}
      </DialogContent>
      <DialogActions><Button onClick={onClose}>Закрыть</Button></DialogActions>
    </Dialog>
  );
};

// ============ Аннулирование ============

const AnnulDialog: React.FC<{ entry: HaccpEntry | null; onClose: () => void }> = ({ entry, onClose }) => {
  const invalidate = useInvalidateHaccp();
  const [reason, setReason] = React.useState('');
  const [error, setError] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  React.useEffect(() => { setReason(''); setError(''); }, [entry]);
  const submit = async () => {
    if (!entry) return;
    setBusy(true);
    try {
      await annulHaccpEntry(entry.id, reason);
      await invalidate();
      onClose();
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };
  return (
    <Dialog open={!!entry} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>Аннулировать запись?</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        <Typography variant="body2" color="text.secondary">Запись останется в журнале зачёркнутой, с причиной и вашим именем.</Typography>
        <TextField label="Причина *" value={reason} onChange={e => setReason(e.target.value)} autoFocus placeholder="Например: внесено по ошибке не в тот холодильник" />
        {error && <Alert severity="error">{error}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose}>Отмена</Button>
        <Button color="error" variant="contained" disabled={busy || !reason.trim()} onClick={submit}>Аннулировать</Button>
      </DialogActions>
    </Dialog>
  );
};

// ============ Строка журнала ============

const EntryRow: React.FC<{
  entry: HaccpEntry;
  journal: HaccpJournal;
  meta: HaccpMeta;
  onEdit: () => void;
  onAnnul: () => void;
  onHistory: () => void;
}> = ({ entry: e, journal, meta, onEdit, onAnnul, onHistory }) => {
  const [anchor, setAnchor] = React.useState<HTMLElement | null>(null);
  const obj = e.objectId ? meta.objects.find(o => o.id === e.objectId) : null;
  const staff = e.staffId ? meta.staff.find(s => s.id === e.staffId) : null;
  const head = obj?.name ?? staff?.fullName ?? null;
  const main = journal.fields
    .map(f => ({ f, v: displayValue(f, e.values[f.key]) }))
    .filter(x => x.v && x.f.key !== 'note');

  return (
    <Box sx={{
      display: 'flex', gap: 1.5, py: 1.5, px: 2, borderTop: '1px solid', borderColor: 'divider',
      bgcolor: e.hasDeviation && !e.isAnnulled ? 'rgba(226,70,44,0.06)' : undefined,
      opacity: e.isAnnulled ? 0.55 : 1,
    }}>
      <Box sx={{ width: 4, borderRadius: 2, flexShrink: 0, bgcolor: e.isAnnulled ? 'divider' : e.hasDeviation ? tokens.chili : tokens.herb }} />
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Box sx={{ display: 'flex', gap: 1, alignItems: 'baseline', flexWrap: 'wrap' }}>
          <Typography sx={{ fontWeight: 700, textDecoration: e.isAnnulled ? 'line-through' : 'none' }}>{fmtMsk(e.occurredAt)}</Typography>
          {head && <Typography sx={{ fontWeight: 600 }}>{head}</Typography>}
          {e.late && <Chip size="small" label="внесено позже" />}
          {e.version > 1 && <Chip size="small" label={`исправлено (${e.version - 1})`} onClick={onHistory} />}
          {e.isAnnulled && <Chip size="small" label="аннулировано" />}
        </Box>
        <Typography variant="body2" sx={{ mt: 0.25, textDecoration: e.isAnnulled ? 'line-through' : 'none' }}>
          {main.map(x => (x.f.type === 'bool' ? `${x.f.label.length > 40 ? x.f.label.slice(0, 40) + '…' : x.f.label}: ${x.v}` : x.v)).join(' · ')}
        </Typography>
        {e.values.note && <Typography variant="body2" color="text.secondary">{String(e.values.note)}</Typography>}
        {e.hasDeviation && (
          <Typography variant="body2" sx={{ color: tokens.chili, fontWeight: 600, mt: 0.5 }}>
            <WarnIcon sx={{ fontSize: 16, verticalAlign: 'text-bottom', mr: 0.5 }} />{e.deviation}
          </Typography>
        )}
        {e.correctiveAction && <Typography variant="body2">Действие: {e.correctiveAction}</Typography>}
        {e.isAnnulled && <Typography variant="body2">Причина аннулирования: {e.annulReason}</Typography>}
        <Typography variant="caption" color="text.secondary">Внёс {e.createdByName} · {fmtMsk(e.createdAt)}</Typography>
      </Box>
      <IconButton aria-label="Действия" onClick={ev => setAnchor(ev.currentTarget)} sx={{ alignSelf: 'flex-start' }}><MoreIcon /></IconButton>
      <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}>
        <MenuItem disabled={!e.canEdit} onClick={() => { setAnchor(null); onEdit(); }}>Исправить</MenuItem>
        <MenuItem disabled={!e.canEdit} onClick={() => { setAnchor(null); onAnnul(); }}>Аннулировать</MenuItem>
        <MenuItem onClick={() => { setAnchor(null); onHistory(); }}><HistoryIcon fontSize="small" sx={{ mr: 1 }} />История</MenuItem>
      </Menu>
    </Box>
  );
};

// ============ Журнал ============

const JournalView: React.FC<{ meta: HaccpMeta; journal: HaccpJournal }> = ({ meta, journal }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const [from, setFrom] = React.useState(monthStartMsk());
  const [to, setTo] = React.useState(todayMsk());
  const [objectId, setObjectId] = React.useState<number | ''>('');
  const [deviationsOnly, setDeviationsOnly] = React.useState(false);
  const { data: entries = [], isLoading } = useHaccpEntries(journal.key, from, to, objectId || null, deviationsOnly);

  const [adding, setAdding] = React.useState(false);
  const [editing, setEditing] = React.useState<HaccpEntry | null>(null);
  const [annulling, setAnnulling] = React.useState<HaccpEntry | null>(null);
  const [history, setHistory] = React.useState<HaccpEntry | null>(null);
  const [healthOpen, setHealthOpen] = React.useState(false);
  const [receiptOpen, setReceiptOpen] = React.useState(false);
  const [error, setError] = React.useState('');

  React.useEffect(() => { setObjectId(''); setDeviationsOnly(false); }, [journal.key]);

  const objects = meta.objects.filter(o => o.kind === journal.objectKind);
  const todayStr = fmtMsk(new Date().toISOString(), false);
  const todayIds = entries.filter(e => fmtMsk(e.occurredAt, false) === todayStr && !e.isAnnulled && e.staffId).map(e => e.staffId!);

  const exportExcel = async () => {
    setError('');
    try { await downloadHaccpExcel(journal.key, from, to); } catch (e) { setError(apiError(e)); }
  };
  const print = () => {
    if (!printJournal(meta, journal, entries.filter(e => !deviationsOnly || e.hasDeviation), from, to))
      setError('Браузер заблокировал окно печати — разрешите всплывающие окна для сайта');
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Panel>
        <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <Box sx={{ flex: '1 1 260px', minWidth: 0 }}>
            <Typography sx={{ fontWeight: 800, fontSize: '1.15rem' }}>{journal.title}</Typography>
            <Typography variant="body2" color="text.secondary">{journal.basis}</Typography>
            {journal.description && <Typography variant="body2" sx={{ mt: 0.5 }}>{journal.description}</Typography>}
          </Box>
          <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
            {journal.key === 'health' && (
              <Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={() => setHealthOpen(true)}>Осмотр смены</Button>
            )}
            {journal.key === 'perishable' && (
              <Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={() => setReceiptOpen(true)}>Из прихода</Button>
            )}
            <Button variant={journal.key === 'health' || journal.key === 'perishable' ? 'outlined' : 'contained'} color="secondary"
              startIcon={<AddIcon />} onClick={() => setAdding(true)}>
              {journal.key === 'health' ? 'Один сотрудник' : 'Запись'}
            </Button>
          </Box>
        </Box>
      </Panel>

      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
        <TextField size="small" type="date" label="С" value={from} onChange={e => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} sx={{ width: 160 }} />
        <TextField size="small" type="date" label="По" value={to} onChange={e => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} sx={{ width: 160 }} />
        {objects.length > 1 && (
          <TextField size="small" select label="Оборудование" value={objectId} onChange={e => setObjectId(e.target.value === '' ? '' : Number(e.target.value))} sx={{ minWidth: 180 }}>
            <MenuItem value="">Все</MenuItem>
            {objects.map(o => <MenuItem key={o.id} value={o.id}>{o.name}{o.isActive ? '' : ' (выключено)'}</MenuItem>)}
          </TextField>
        )}
        <FormControlLabel control={<Switch checked={deviationsOnly} onChange={e => setDeviationsOnly(e.target.checked)} />} label="Только отклонения" />
        <Box sx={{ flex: 1 }} />
        <Button startIcon={<PrintIcon />} onClick={print} sx={{ bgcolor: t.surfaceMuted }}>Печать / PDF</Button>
        <Button startIcon={<ExcelIcon />} onClick={exportExcel} sx={{ bgcolor: t.surfaceMuted }}>Excel</Button>
      </Box>
      {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}

      <Panel padding={false}>
        {isLoading ? <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>
          : entries.length === 0 ? <EmptyState title="За период записей нет" text={objects.length === 0 && journal.objectKind ? 'Добавьте оборудование во вкладке «Настройка»' : undefined} />
          : (
            <>
              <Box sx={{ px: 2, py: 1.25, display: 'flex', gap: 2, flexWrap: 'wrap' }}>
                <Typography variant="body2" color="text.secondary">Записей: {entries.length}</Typography>
                {entries.some(e => e.hasDeviation) && (
                  <Typography variant="body2" sx={{ color: tokens.chili }}>с отклонением: {entries.filter(e => e.hasDeviation && !e.isAnnulled).length}</Typography>
                )}
                {objects.length > 0 && objectId !== '' && (() => {
                  const o = objects.find(x => x.id === objectId);
                  return o && (o.minValue != null || o.maxValue != null) ? <Typography variant="body2" color="text.secondary">норма {rangeText(o.minValue, o.maxValue)}</Typography> : null;
                })()}
              </Box>
              {entries.map(e => (
                <EntryRow key={e.id} entry={e} journal={journal} meta={meta}
                  onEdit={() => setEditing(e)} onAnnul={() => setAnnulling(e)} onHistory={() => setHistory(e)} />
              ))}
            </>
          )}
      </Panel>

      <EntryDialog open={adding || !!editing} meta={meta} journal={journal} entry={editing}
        presetObjectId={objectId || null} onClose={() => { setAdding(false); setEditing(null); }} />
      <AnnulDialog entry={annulling} onClose={() => setAnnulling(null)} />
      <HistoryDialog entry={history} journal={journal} onClose={() => setHistory(null)} />
      {journal.key === 'health' && <HealthBatchDialog open={healthOpen} meta={meta} alreadyToday={todayIds} onClose={() => setHealthOpen(false)} />}
      {journal.key === 'perishable' && <PerishableReceiptDialog open={receiptOpen} onClose={() => setReceiptOpen(false)} />}
    </Box>
  );
};

export default JournalView;
