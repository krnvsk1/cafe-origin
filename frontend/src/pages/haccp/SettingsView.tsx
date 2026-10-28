import * as React from 'react';
import {
  Box, Typography, Button, TextField, Dialog, DialogTitle, DialogContent, DialogActions, Alert, Switch, FormControlLabel,
  InputAdornment, Chip, useMediaQuery, useTheme,
} from '@mui/material';
import { Add as AddIcon, ChevronRight as ChevronRightIcon } from '@mui/icons-material';
import {
  saveHaccpObject, deleteHaccpObject, saveHaccpStaff, deleteHaccpStaff, rangeText, todayMsk, fmtMsk, useInvalidateHaccp, OBJECT_KINDS,
  type HaccpMeta, type HaccpObject, type HaccpObjectKind, type HaccpStaff,
} from '../../api/haccp';
import { apiError } from '../../api/inventory';
import { tokens } from '../../theme';
import { Panel, ListRow } from '../inventory/ui';

const num = (s: string) => (s.trim() === '' ? null : Number(s.replace(',', '.')));
const str = (v: number | null | undefined) => (v === null || v === undefined ? '' : String(v));

// ============ Оборудование / ККТ ============

const ObjectDialog: React.FC<{ value: Partial<HaccpObject> & { kind: HaccpObjectKind } | null; onClose: () => void }> = ({ value, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const invalidate = useInvalidateHaccp();
  const [f, setF] = React.useState({ name: '', location: '', min: '', max: '', minH: '', maxH: '', checks: '1', note: '', active: true });
  const [error, setError] = React.useState('');
  const [busy, setBusy] = React.useState(false);

  React.useEffect(() => {
    if (!value) return;
    setError('');
    setF({
      name: value.name ?? '', location: value.location ?? '', min: str(value.minValue), max: str(value.maxValue),
      minH: str(value.minHumidity), maxH: str(value.maxHumidity), checks: String(value.checksPerDay ?? 1), note: value.note ?? '',
      active: value.isActive ?? true,
    });
  }, [value]);

  if (!value) return null;
  const kind = value.kind;
  const ccp = kind === 'ccp';

  const save = async () => {
    setBusy(true);
    setError('');
    try {
      await saveHaccpObject({
        id: value.id, kind, name: f.name, location: f.location || null, minValue: num(f.min), maxValue: num(f.max),
        minHumidity: num(f.minH), maxHumidity: num(f.maxH), checksPerDay: Number(f.checks) || 1, note: f.note || null, isActive: f.active,
        sortOrder: value.sortOrder ?? 0,
      });
      await invalidate();
      onClose();
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!value.id) return;
    setBusy(true);
    try {
      await deleteHaccpObject(value.id);
      await invalidate();
      onClose();
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open onClose={onClose} fullScreen={fullScreen} maxWidth="sm" fullWidth>
      <DialogTitle>{value.id ? 'Изменить' : 'Добавить'}: {OBJECT_KINDS[kind].one.toLowerCase()}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
        <TextField label={ccp ? 'Контрольная точка' : 'Название'} value={f.name} onChange={e => setF({ ...f, name: e.target.value })} autoFocus
          placeholder={ccp ? 'Жарка курицы для шаурмы — температура в толще' : kind === 'fridge' ? 'Холодильник для мяса' : ''} />
        {!ccp && <TextField label="Помещение" value={f.location} onChange={e => setF({ ...f, location: e.target.value })} placeholder="Горячий цех, склад…" />}
        {kind !== 'fryer' && (
          <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5 }}>
            <TextField label={ccp ? 'Критический предел: не ниже' : 'Температура от'} type="number" value={f.min} onChange={e => setF({ ...f, min: e.target.value })}
              slotProps={{ input: { endAdornment: <InputAdornment position="end">°C</InputAdornment> }, htmlInput: { step: 'any' } }} />
            <TextField label={ccp ? 'не выше' : 'до'} type="number" value={f.max} onChange={e => setF({ ...f, max: e.target.value })}
              slotProps={{ input: { endAdornment: <InputAdornment position="end">°C</InputAdornment> }, htmlInput: { step: 'any' } }} />
          </Box>
        )}
        {kind === 'storage' && (
          <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5 }}>
            <TextField label="Влажность от" type="number" value={f.minH} onChange={e => setF({ ...f, minH: e.target.value })}
              slotProps={{ input: { endAdornment: <InputAdornment position="end">%</InputAdornment> } }} />
            <TextField label="до" type="number" value={f.maxH} onChange={e => setF({ ...f, maxH: e.target.value })}
              slotProps={{ input: { endAdornment: <InputAdornment position="end">%</InputAdornment> } }} />
          </Box>
        )}
        <Typography variant="body2" color="text.secondary">Когда проверять — задаётся во вкладке «График».</Typography>
        {ccp && (
          <TextField label="Что делать при отклонении" value={f.note} onChange={e => setF({ ...f, note: e.target.value })} multiline minRows={2}
            placeholder="Продолжить жарку до достижения 75 °C, повторный замер" />
        )}
        {value.id && <FormControlLabel control={<Switch checked={f.active} onChange={e => setF({ ...f, active: e.target.checked })} />} label="Используется" />}
        {error && <Alert severity="error">{error}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        {value.id && <Button onClick={remove} disabled={busy} sx={{ color: tokens.chili, mr: 'auto' }}>Удалить</Button>}
        <Button onClick={onClose}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={save} disabled={busy || !f.name.trim()}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

// ============ Сотрудник ============

const StaffDialog: React.FC<{ value: Partial<HaccpStaff> | null; onClose: () => void }> = ({ value, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const invalidate = useInvalidateHaccp();
  const [f, setF] = React.useState({ fullName: '', position: '', book: '', exam: '', training: '', active: true, note: '' });
  const [error, setError] = React.useState('');
  const [busy, setBusy] = React.useState(false);

  React.useEffect(() => {
    if (!value) return;
    setError('');
    setF({
      fullName: value.fullName ?? '', position: value.position ?? '', book: value.medicalBookNumber ?? '',
      exam: value.medicalExamUntil?.slice(0, 10) ?? '', training: value.hygieneTrainingUntil?.slice(0, 10) ?? '',
      active: value.isActive ?? true, note: value.note ?? '',
    });
  }, [value]);

  if (!value) return null;

  const save = async () => {
    setBusy(true);
    setError('');
    try {
      await saveHaccpStaff({
        id: value.id, fullName: f.fullName, position: f.position || null, medicalBookNumber: f.book || null,
        medicalExamUntil: f.exam || null, hygieneTrainingUntil: f.training || null, isActive: f.active, note: f.note || null,
      });
      await invalidate();
      onClose();
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!value.id) return;
    setBusy(true);
    try {
      await deleteHaccpStaff(value.id);
      await invalidate();
      onClose();
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open onClose={onClose} fullScreen={fullScreen} maxWidth="sm" fullWidth>
      <DialogTitle>{value.id ? 'Сотрудник' : 'Новый сотрудник'}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
        <TextField label="ФИО" value={f.fullName} onChange={e => setF({ ...f, fullName: e.target.value })} autoFocus />
        <TextField label="Должность" value={f.position} onChange={e => setF({ ...f, position: e.target.value })} placeholder="Повар-кассир" />
        <TextField label="Номер медицинской книжки" value={f.book} onChange={e => setF({ ...f, book: e.target.value })} />
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0,1fr)', sm: '1fr 1fr' }, gap: 1.5 }}>
          <TextField label="Медосмотр действует до" type="date" value={f.exam} onChange={e => setF({ ...f, exam: e.target.value })}
            slotProps={{ inputLabel: { shrink: true } }} error={!!f.exam && f.exam < todayMsk()} />
          <TextField label="Гигиеническая аттестация до" type="date" value={f.training} onChange={e => setF({ ...f, training: e.target.value })}
            slotProps={{ inputLabel: { shrink: true } }} error={!!f.training && f.training < todayMsk()} />
        </Box>
        <TextField label="Примечание" value={f.note} onChange={e => setF({ ...f, note: e.target.value })} />
        {value.id && <FormControlLabel control={<Switch checked={f.active} onChange={e => setF({ ...f, active: e.target.checked })} />} label="Работает" />}
        {error && <Alert severity="error">{error}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        {value.id && <Button onClick={remove} disabled={busy} sx={{ color: tokens.chili, mr: 'auto' }}>Удалить</Button>}
        <Button onClick={onClose}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={save} disabled={busy || !f.fullName.trim()}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

// ============ Вкладка ============

const until = (iso: string | null) => {
  if (!iso) return { text: 'не указан', bad: true };
  const d = iso.slice(0, 10);
  const soon = new Date(`${d}T00:00:00Z`).getTime() - Date.now() < 30 * 86400_000;
  return { text: fmtMsk(iso, false), bad: d < todayMsk(), soon };
};

const SettingsView: React.FC<{ meta: HaccpMeta }> = ({ meta }) => {
  const [object, setObject] = React.useState<(Partial<HaccpObject> & { kind: HaccpObjectKind }) | null>(null);
  const [staff, setStaff] = React.useState<Partial<HaccpStaff> | null>(null);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {(Object.keys(OBJECT_KINDS) as HaccpObjectKind[]).map(kind => {
        const list = meta.objects.filter(o => o.kind === kind);
        return (
          <Panel key={kind} padding={false}>
            <Box sx={{ display: 'flex', alignItems: 'center', px: 2, py: 1.5, gap: 1 }}>
              <Typography sx={{ fontWeight: 700, flex: 1 }}>{OBJECT_KINDS[kind].title}</Typography>
              <Button size="small" startIcon={<AddIcon />} onClick={() => setObject({ kind, ...OBJECT_KINDS[kind].defaults })}>Добавить</Button>
            </Box>
            {list.length === 0 && <Typography variant="body2" color="text.secondary" sx={{ px: 2, pb: 1.5 }}>
              {kind === 'ccp' ? 'Например: «Жарка курицы — в толще не ниже 75 °C», «Охлаждение соусов — до +6 °C за 2 часа»' : 'Пока ничего'}
            </Typography>}
            {list.map(o => (
              <ListRow key={o.id} onClick={() => setObject(o)} muted={!o.isActive}>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography sx={{ fontWeight: 600 }}>{o.name}{o.isActive ? '' : ' · выключено'}</Typography>
                  <Typography variant="body2" color="text.secondary">
                    {[o.location, rangeText(o.minValue, o.maxValue), kind === 'storage' ? rangeText(o.minHumidity, o.maxHumidity, '%') : ''].filter(Boolean).join(' · ')}
                  </Typography>
                </Box>
                <ChevronRightIcon sx={{ color: 'text.secondary' }} />
              </ListRow>
            ))}
          </Panel>
        );
      })}

      <Panel padding={false}>
        <Box sx={{ display: 'flex', alignItems: 'center', px: 2, py: 1.5, gap: 1 }}>
          <Typography sx={{ fontWeight: 700, flex: 1 }}>Сотрудники: медкнижки и аттестация</Typography>
          <Button size="small" startIcon={<AddIcon />} onClick={() => setStaff({})}>Добавить</Button>
        </Box>
        {meta.staff.map(s => {
          const exam = until(s.medicalExamUntil);
          const training = until(s.hygieneTrainingUntil);
          return (
            <ListRow key={s.id} onClick={() => setStaff(s)} muted={!s.isActive}>
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography sx={{ fontWeight: 600 }}>{s.fullName}{s.isActive ? '' : ' · не работает'}</Typography>
                <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap', mt: 0.25 }}>
                  {s.position && <Typography variant="body2" color="text.secondary">{s.position}</Typography>}
                  <Chip size="small" label={`медосмотр: ${exam.text}`} color={exam.bad ? 'error' : exam.soon ? 'warning' : 'default'} />
                  <Chip size="small" label={`аттестация: ${training.text}`} color={training.bad && s.hygieneTrainingUntil ? 'error' : training.soon ? 'warning' : 'default'} />
                </Box>
              </Box>
              <ChevronRightIcon sx={{ color: 'text.secondary' }} />
            </ListRow>
          );
        })}
      </Panel>

      <ObjectDialog value={object} onClose={() => setObject(null)} />
      <StaffDialog value={staff} onClose={() => setStaff(null)} />
    </Box>
  );
};

export default SettingsView;
