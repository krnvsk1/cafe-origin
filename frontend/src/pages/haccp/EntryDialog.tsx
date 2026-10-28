import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, TextField, MenuItem, Box, Typography, IconButton, Alert,
  CircularProgress, ToggleButtonGroup, ToggleButton, Autocomplete, InputAdornment, useMediaQuery, useTheme,
} from '@mui/material';
import { Close as CloseIcon, WarningAmberRounded as WarnIcon } from '@mui/icons-material';
import {
  createHaccpEntries, updateHaccpEntry, predictDeviations, defaultValues, rangeText, toLocalInput, fromLocalInput, useInvalidateHaccp,
  type HaccpMeta, type HaccpJournal, type HaccpEntry, type HaccpField, type Values,
} from '../../api/haccp';
import { apiError } from '../../api/inventory';

interface Props {
  open: boolean;
  meta: HaccpMeta;
  journal: HaccpJournal;
  /** Исправление существующей записи */
  entry?: HaccpEntry | null;
  presetObjectId?: number | null;
  presetStaffId?: number | null;
  onClose: () => void;
}

/** Значения записи → значения формы (datetime в UTC → поле по Москве) */
const toForm = (journal: HaccpJournal, values: Values): Values =>
  Object.fromEntries(journal.fields.map(f => {
    const v = values[f.key];
    if (v === undefined || v === null) return [f.key, null];
    if (f.type === 'datetime') return [f.key, toLocalInput(String(v))];
    if (f.type === 'date') return [f.key, String(v).slice(0, 10)];
    return [f.key, v];
  }));

/** Значения формы → для сервера */
const fromForm = (journal: HaccpJournal, values: Values): Values =>
  Object.fromEntries(journal.fields.map(f => {
    const v = values[f.key];
    if (v === undefined || v === null || v === '') return [f.key, null];
    if (f.type === 'datetime') return [f.key, fromLocalInput(String(v))];
    return [f.key, v];
  }));

export const FieldInput: React.FC<{
  field: HaccpField;
  value: Values[string] | undefined;
  onChange: (v: Values[string]) => void;
  dishes: string[];
  norm?: string;
  bad?: boolean;
  compact?: boolean;
}> = ({ field: f, value, onChange, dishes, norm, bad, compact }) => {
  const label = `${f.label}${f.required ? ' *' : ''}`;
  const size = compact ? 'small' : 'medium';
  switch (f.type) {
    case 'bool':
      return (
        <Box>
          <Typography variant="body2" sx={{ mb: 0.75, color: bad ? 'error.main' : 'text.primary' }}>{label}</Typography>
          <ToggleButtonGroup
            exclusive
            value={value === true || value === 'true' ? 'yes' : value === false || value === 'false' ? 'no' : null}
            onChange={(_, v) => v && onChange(v === 'yes')}
            sx={{ '& .MuiToggleButton-root': { px: 3, py: compact ? 0.5 : 1 } }}
          >
            <ToggleButton value="yes" color="success">Да</ToggleButton>
            <ToggleButton value="no" color="error">Нет</ToggleButton>
          </ToggleButtonGroup>
        </Box>
      );
    case 'select':
      if ((f.options?.length ?? 0) <= 4)
        return (
          <Box>
            <Typography variant="body2" sx={{ mb: 0.75, color: bad ? 'error.main' : 'text.primary' }}>{label}</Typography>
            <ToggleButtonGroup
              exclusive
              value={value ?? null}
              onChange={(_, v) => v && onChange(v)}
              sx={{ flexWrap: 'wrap', '& .MuiToggleButton-root': { py: compact ? 0.5 : 1, px: 2 } }}
            >
              {f.options!.map(o => (
                <ToggleButton key={o} value={o} color={f.badOptions?.includes(o) ? 'error' : 'primary'}>{o}</ToggleButton>
              ))}
            </ToggleButtonGroup>
          </Box>
        );
      return (
        <TextField select size={size} label={label} value={value ?? ''} onChange={e => onChange(e.target.value)} error={bad} fullWidth>
          {f.options!.map(o => <MenuItem key={o} value={o}>{o}</MenuItem>)}
        </TextField>
      );
    case 'temperature':
    case 'humidity':
    case 'number':
      return (
        <TextField
          size={size}
          label={label}
          type="number"
          value={value ?? ''}
          onChange={e => onChange(e.target.value)}
          error={bad}
          helperText={norm ? `Норма ${norm}` : f.hint ?? undefined}
          fullWidth
          slotProps={{
            input: {
              endAdornment: f.unit ? <InputAdornment position="end">{f.unit}</InputAdornment> : undefined,
              sx: f.type === 'temperature' && !compact ? { fontSize: '1.6rem', fontWeight: 700 } : undefined,
            },
            htmlInput: { step: 'any', inputMode: 'decimal' },
          }}
        />
      );
    case 'datetime':
      return (
        <TextField size={size} label={label} type="datetime-local" value={value ?? ''} onChange={e => onChange(e.target.value)} fullWidth
          slotProps={{ inputLabel: { shrink: true } }} />
      );
    case 'date':
      return (
        <TextField size={size} label={label} type="date" value={value ?? ''} onChange={e => onChange(e.target.value)} error={bad} fullWidth
          slotProps={{ inputLabel: { shrink: true } }} helperText={f.hint ?? undefined} />
      );
    case 'dish':
      return (
        <Autocomplete
          freeSolo
          options={dishes}
          value={(value as string) ?? ''}
          onInputChange={(_, v) => onChange(v)}
          renderInput={params => <TextField {...params} size={size} label={label} />}
        />
      );
    case 'textarea':
      return <TextField size={size} label={label} value={value ?? ''} onChange={e => onChange(e.target.value)} multiline minRows={2} fullWidth />;
    default:
      return <TextField size={size} label={label} value={value ?? ''} onChange={e => onChange(e.target.value)} helperText={f.hint ?? undefined} fullWidth />;
  }
};

/** Добавить или исправить запись журнала; графы — из описания журнала на сервере */
const EntryDialog: React.FC<Props> = ({ open, meta, journal, entry, presetObjectId, presetStaffId, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const invalidate = useInvalidateHaccp();
  const editing = !!entry;

  const objects = meta.objects.filter(o => o.kind === journal.objectKind && (o.isActive || o.id === entry?.objectId));
  const staffList = meta.staff.filter(s => s.isActive || s.id === entry?.staffId);

  const [objectId, setObjectId] = React.useState<number | null>(null);
  const [staffId, setStaffId] = React.useState<number | null>(null);
  const [occurredAt, setOccurredAt] = React.useState('');
  const [values, setValues] = React.useState<Values>({});
  const [corrective, setCorrective] = React.useState('');
  const [reason, setReason] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState('');

  React.useEffect(() => {
    if (!open) return;
    setError('');
    setReason('');
    if (entry) {
      setObjectId(entry.objectId);
      setStaffId(entry.staffId);
      setOccurredAt(toLocalInput(entry.occurredAt));
      setValues(toForm(journal, entry.values));
      setCorrective(entry.correctiveAction ?? '');
    } else {
      setObjectId(presetObjectId ?? (objects.length === 1 ? objects[0].id : null));
      setStaffId(presetStaffId ?? null);
      setOccurredAt(toLocalInput());
      const d = defaultValues(journal);
      for (const f of journal.fields) if (f.type === 'datetime' && f.required && !d[f.key]) d[f.key] = toLocalInput();
      setValues(d);
      setCorrective('');
    }
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const obj = objects.find(o => o.id === objectId) ?? null;
  const staff = staffList.find(s => s.id === staffId) ?? null;
  const deviations = predictDeviations(journal, values, obj, staff);

  const isBad = (f: HaccpField) => predictDeviations({ ...journal, fields: [f] }, values, obj, null).length > 0;

  const submit = async () => {
    setError('');
    if (journal.objectKind && !objectId) { setError('Выберите оборудование'); return; }
    if (journal.usesStaff && !staffId) { setError('Выберите сотрудника'); return; }
    if (deviations.length > 0 && !corrective.trim()) { setError('Есть отклонение — опишите, что сделали (корректирующее действие)'); return; }
    if (editing && !reason.trim()) { setError('Укажите причину исправления'); return; }
    setBusy(true);
    try {
      const input = {
        journal: journal.key,
        objectId,
        staffId,
        occurredAt: fromLocalInput(occurredAt),
        values: fromForm(journal, values),
        correctiveAction: corrective.trim() || null,
        reason: reason.trim() || null,
      };
      if (editing) await updateHaccpEntry(entry!.id, input);
      else await createHaccpEntries([input]);
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
      <DialogTitle sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, pr: 1.5 }}>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant="body2" color="text.secondary">{editing ? 'Исправление записи' : 'Новая запись'}</Typography>
          {journal.short}
        </Box>
        <IconButton aria-label="Закрыть" onClick={onClose} disabled={busy}><CloseIcon /></IconButton>
      </DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
        {journal.objectKind && (
          objects.length === 0
            ? <Alert severity="warning">Сначала добавьте оборудование во вкладке «Настройка»</Alert>
            : (
              <TextField select label="Оборудование / точка контроля *" value={objectId ?? ''} onChange={e => setObjectId(Number(e.target.value))}
                helperText={obj ? [obj.location, obj.minValue != null || obj.maxValue != null ? `норма ${rangeText(obj.minValue, obj.maxValue)}` : null, obj.note].filter(Boolean).join(' · ') : ' '}>
                {objects.map(o => <MenuItem key={o.id} value={o.id}>{o.name}{o.location ? ` · ${o.location}` : ''}</MenuItem>)}
              </TextField>
            )
        )}
        {journal.usesStaff && (
          <TextField select label="Сотрудник *" value={staffId ?? ''} onChange={e => setStaffId(Number(e.target.value))}>
            {staffList.map(s => <MenuItem key={s.id} value={s.id}>{s.fullName}{s.position ? ` · ${s.position}` : ''}</MenuItem>)}
          </TextField>
        )}

        {journal.fields.map(f => (
          <FieldInput
            key={f.key}
            field={f}
            value={values[f.key]}
            onChange={v => setValues(vs => ({ ...vs, [f.key]: v }))}
            dishes={meta.dishes}
            norm={f.type === 'temperature' ? rangeText(f.min ?? obj?.minValue, f.max ?? obj?.maxValue) || undefined
              : f.type === 'humidity' ? rangeText(f.min ?? obj?.minHumidity, f.max ?? obj?.maxHumidity, '%') || undefined : undefined}
            bad={isBad(f)}
          />
        ))}

        {deviations.length > 0 && (
          <Alert severity="error" icon={<WarnIcon />}>
            <Typography sx={{ fontWeight: 700 }}>Отклонение</Typography>
            {deviations.map(d => <div key={d}>{d}</div>)}
          </Alert>
        )}
        {(deviations.length > 0 || corrective) && (
          <TextField
            label={`Корректирующее действие${deviations.length ? ' *' : ''}`}
            value={corrective}
            onChange={e => setCorrective(e.target.value)}
            multiline
            minRows={2}
            placeholder="Например: продукты перенесены в исправный холодильник, вызван мастер, повторный замер через 30 минут"
          />
        )}

        <TextField label="Время замера / осмотра" type="datetime-local" value={occurredAt} onChange={e => setOccurredAt(e.target.value)}
          slotProps={{ inputLabel: { shrink: true } }}
          helperText={meta.canManage ? 'По Москве' : `По Москве. Записи старше ${meta.editWindowHours} ч вносит только ответственный`} />

        {editing && (
          <TextField label="Причина исправления *" value={reason} onChange={e => setReason(e.target.value)}
            helperText="Сохранится в истории записи вместе с прежними значениями" />
        )}
        {error && <Alert severity="error">{error}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        <Button onClick={onClose} disabled={busy} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" size="large" onClick={submit} disabled={busy}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          {editing ? 'Сохранить исправление' : 'Записать'}
        </Button>
      </DialogActions>
    </Dialog>
  );
};

export default EntryDialog;
