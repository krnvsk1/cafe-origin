import * as React from 'react';
import { TitleWithHint } from '../../components/Hint';
import {
  Box, Typography, Button, TextField, IconButton, Tooltip, ToggleButtonGroup, ToggleButton, Alert, CircularProgress,
  Dialog, DialogTitle, DialogContent, DialogActions, useMediaQuery, useTheme, Chip, FormControlLabel, Switch, MenuItem,
} from '@mui/material';
import {
  Add as AddIcon, DeleteOutline as DeleteIcon, ArrowUpward as UpIcon, ArrowDownward as DownIcon,
  FormatAlignLeft as LeftIcon, FormatAlignCenter as CenterIcon, FormatAlignRight as RightIcon, FormatSize as LargeIcon, FormatBold as BoldIcon,
} from '@mui/icons-material';
import {
  usePrecheckPreview, useSavePrecheckLayout, printPrecheckSample, PRECHECK_VARS,
  type PrintingState, type PrecheckLayout, type PrecheckLayoutLine,
} from '../../api/printing';
import { apiError } from '../../api/inventory';

/** Как счёт выйдет на ленте — строит сервер тем же кодом, что печатает */
const Preview: React.FC<{ width: number; layout: PrecheckLayout }> = ({ width, layout }) => {
  // Перестраиваем с задержкой, чтобы не дёргать сервер на каждую букву
  const [debounced, setDebounced] = React.useState(layout);
  React.useEffect(() => { const t = setTimeout(() => setDebounced(layout), 350); return () => clearTimeout(t); }, [layout]);
  const { data, isLoading, error } = usePrecheckPreview(width, debounced, true);
  return (
    <Box sx={{
      bgcolor: '#fffdf7', color: '#111', borderRadius: '4px', boxShadow: '0 6px 24px rgba(0,0,0,0.12)',
      px: 1.5, py: 2, fontFamily: '"Roboto Mono", "Courier New", monospace', fontSize: { xs: '10px', sm: '11px' }, lineHeight: 1.4,
      width: `calc(${data?.width ?? width}ch + 24px)`, maxWidth: '100%', mx: 'auto', overflowX: 'auto', minHeight: 160,
    }}>
      {error ? <Typography variant="body2" color="error">{apiError(error)}</Typography>
        : isLoading && !data ? <Typography variant="body2" color="text.secondary">Загрузка…</Typography>
        : (data?.lines ?? []).map((l, i) => (
          <Box key={i} sx={{
            whiteSpace: 'pre', minHeight: '1.4em',
            textAlign: l.align === 1 ? 'center' : l.align === 2 ? 'right' : 'left',
            fontWeight: l.bold ? 700 : 400, fontSize: l.big ? '2em' : undefined, lineHeight: l.big ? 1.15 : undefined,
            color: l.text === '' ? '#8a857c' : undefined,
          }}>{l.text}</Box>
        ))}
    </Box>
  );
};

const LinesEditor: React.FC<{ title: string; lines: PrecheckLayoutLine[]; onChange: (l: PrecheckLayoutLine[]) => void }> = ({ title, lines, onChange }) => {
  const update = (i: number, patch: Partial<PrecheckLayoutLine>) => onChange(lines.map((l, k) => (k === i ? { ...l, ...patch } : l)));
  const move = (i: number, d: number) => {
    const j = i + d;
    if (j < 0 || j >= lines.length) return;
    const next = [...lines];
    [next[i], next[j]] = [next[j], next[i]];
    onChange(next);
  };
  return (
    <Box>
      <Typography sx={{ fontWeight: 700, mb: 1 }}>{title}</Typography>
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
        {lines.map((l, i) => (
          <Box key={i} sx={{ display: 'flex', gap: 0.5, alignItems: 'center', flexWrap: { xs: 'wrap', sm: 'nowrap' } }}>
            <TextField size="small" value={l.text} onChange={e => update(i, { text: e.target.value })} placeholder="Текст строки"
              sx={{ flex: '1 1 200px' }} slotProps={{ htmlInput: { maxLength: 64 } }} />
            <ToggleButtonGroup size="small" exclusive value={l.align} onChange={(_, v) => v && update(i, { align: v })}>
              <ToggleButton value="left" aria-label="Слева"><LeftIcon fontSize="small" /></ToggleButton>
              <ToggleButton value="center" aria-label="По центру"><CenterIcon fontSize="small" /></ToggleButton>
              <ToggleButton value="right" aria-label="Справа"><RightIcon fontSize="small" /></ToggleButton>
            </ToggleButtonGroup>
            <Tooltip title="Жирный">
              <ToggleButton size="small" value="bold" selected={l.bold} onChange={() => update(i, { bold: !l.bold })} aria-label="Жирный">
                <BoldIcon fontSize="small" />
              </ToggleButton>
            </Tooltip>
            <Tooltip title="Крупно">
              <ToggleButton size="small" value="large" selected={l.large} onChange={() => update(i, { large: !l.large })} aria-label="Крупно">
                <LargeIcon fontSize="small" />
              </ToggleButton>
            </Tooltip>
            <IconButton size="small" onClick={() => move(i, -1)} disabled={i === 0} aria-label="Выше"><UpIcon fontSize="small" /></IconButton>
            <IconButton size="small" onClick={() => move(i, 1)} disabled={i === lines.length - 1} aria-label="Ниже"><DownIcon fontSize="small" /></IconButton>
            <IconButton size="small" onClick={() => onChange(lines.filter((_, k) => k !== i))} aria-label="Удалить"><DeleteIcon fontSize="small" /></IconButton>
          </Box>
        ))}
        {lines.length < 12 && (
          <Button size="small" startIcon={<AddIcon />} onClick={() => onChange([...lines, { text: '', align: 'center', bold: false, large: false }])} sx={{ alignSelf: 'flex-start' }}>
            Добавить строку
          </Button>
        )}
      </Box>
    </Box>
  );
};

/** Поле счёта: показывать или нет и как подписать */
const FieldRow: React.FC<{ label: string; checked: boolean; onCheck: (v: boolean) => void; caption?: string; onCaption?: (v: string) => void; hint?: string }> =
  ({ label, checked, onCheck, caption, onCaption, hint }) => (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
      <FormControlLabel sx={{ minWidth: 190, mr: 0 }} control={<Switch checked={checked} onChange={e => onCheck(e.target.checked)} />}
        label={hint ? <TitleWithHint hint={hint}>{label}</TitleWithHint> : label} />
      {onCaption && (
        <TextField size="small" label="Подпись" value={caption ?? ''} disabled={!checked} onChange={e => onCaption(e.target.value)}
          sx={{ flex: '1 1 160px', maxWidth: 240 }} slotProps={{ htmlInput: { maxLength: 24 } }} />
      )}
    </Box>
  );

/** Вид счёта для гостя: шапка, поля, подписи итогов, подвал; предпросмотр и печать примера */
const PrecheckLayoutDialog: React.FC<{ open: boolean; state: PrintingState; onClose: () => void }> = ({ open, state, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('md'));
  const save = useSavePrecheckLayout();
  const [layout, setLayout] = React.useState<PrecheckLayout>(state.layout);
  const [width, setWidth] = React.useState(state.width);
  const [busy, setBusy] = React.useState(false);
  const [result, setResult] = React.useState<{ ok: boolean; text: string } | null>(null);
  const set = (patch: Partial<PrecheckLayout>) => setLayout(l => ({ ...l, ...patch }));

  React.useEffect(() => {
    if (open) { setLayout(state.layout); setWidth(state.width); setResult(null); }
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const sample = async () => {
    setBusy(true); setResult(null);
    try { await printPrecheckSample(layout); setResult({ ok: true, text: 'Пример счёта отправлен на принтер' }); }
    catch (e) { setResult({ ok: false, text: apiError(e) }); }
    finally { setBusy(false); }
  };

  const submit = async () => {
    setResult(null);
    try { await save.mutateAsync(layout); onClose(); }
    catch (e) { setResult({ ok: false, text: apiError(e) }); }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg" fullWidth fullScreen={fullScreen}>
      <DialogTitle>
        <TitleWithHint hint="Счёт, который официант печатает гостю перед оплатой. Блюда, суммы и строка «» печатаются всегда; остальное — как настроите здесь. Ширина ленты — в настройках принтера.">
          Вид счёта
        </TitleWithHint>
      </DialogTitle>
      <DialogContent sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(0, 1fr) auto' }, gap: 3, alignItems: 'start' }}>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5, pt: 1, minWidth: 0 }}>
          <LinesEditor title="Шапка" lines={layout.header} onChange={header => set({ header })} />

          <Box>
            <Typography sx={{ fontWeight: 700, mb: 0.5 }}>Поля</Typography>
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
              <FieldRow label="Дата и время" checked={layout.showDate} onCheck={v => set({ showDate: v })} />
              <FieldRow label="Число гостей" checked={layout.showGuests} onCheck={v => set({ showGuests: v })}
                caption={layout.guestsLabel} onCaption={v => set({ guestsLabel: v })} />
              <FieldRow label="Официант" checked={layout.showWaiter} onCheck={v => set({ showWaiter: v })}
                caption={layout.waiterLabel} onCaption={v => set({ waiterLabel: v })} />
              <FieldRow label="Цена за штуку" checked={layout.showPrices} onCheck={v => set({ showPrices: v })}
                hint="Включено — под блюдом строка «2 x 350,00 … 700,00»; выключено — блюдо и сумма в одну строку" />
            </Box>
          </Box>

          <Box>
            <Typography sx={{ fontWeight: 700, mb: 1 }}>Подписи итогов</Typography>
            <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap' }}>
              <TextField size="small" label="Итог" value={layout.totalLabel} onChange={e => set({ totalLabel: e.target.value })}
                sx={{ flex: '1 1 140px' }} slotProps={{ htmlInput: { maxLength: 24 } }} />
              <TextField size="small" label="До скидки" value={layout.subtotalLabel} onChange={e => set({ subtotalLabel: e.target.value })}
                sx={{ flex: '1 1 140px' }} slotProps={{ htmlInput: { maxLength: 24 } }} />
              <TextField size="small" label="После предоплаты" value={layout.dueLabel} onChange={e => set({ dueLabel: e.target.value })}
                sx={{ flex: '1 1 140px' }} slotProps={{ htmlInput: { maxLength: 24 } }} />
            </Box>
          </Box>

          <LinesEditor title="Внизу счёта" lines={layout.footer} onChange={footer => set({ footer })} />

          <Box>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 0.75 }}>Можно вставить в текст:</Typography>
            <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap' }}>
              {PRECHECK_VARS.map(v => <Chip key={v.key} size="small" label={`${v.key} — ${v.label}`} />)}
            </Box>
          </Box>
          {result && <Alert severity={result.ok ? 'success' : 'error'}>{result.text}</Alert>}
        </Box>
        <Box sx={{ position: { md: 'sticky' }, top: 0, pt: 1 }}>
          <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 1, mb: 1 }}>
            <Typography variant="body2" color="text.secondary">Так будет выглядеть счёт ·</Typography>
            <TextField select size="small" variant="standard" value={width} onChange={e => setWidth(Number(e.target.value))}>
              <MenuItem value={48}>80 мм</MenuItem>
              <MenuItem value={42}>80 мм, мелкий</MenuItem>
              <MenuItem value={32}>58 мм</MenuItem>
            </TextField>
          </Box>
          <Preview width={width} layout={layout} />
        </Box>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1, flexWrap: 'wrap' }}>
        <Button onClick={sample} disabled={busy || !state.enabled} sx={{ mr: 'auto' }}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Напечатать пример
        </Button>
        <Button onClick={onClose} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={submit} disabled={save.isPending}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

export default PrecheckLayoutDialog;
