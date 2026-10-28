import * as React from 'react';
import {
  Box, Typography, Button, TextField, IconButton, Tooltip, ToggleButtonGroup, ToggleButton, Alert, CircularProgress,
  Dialog, DialogTitle, DialogContent, DialogActions, MenuItem, useMediaQuery, useTheme, Chip,
} from '@mui/material';
import {
  Add as AddIcon, DeleteOutline as DeleteIcon, ArrowUpward as UpIcon, ArrowDownward as DownIcon,
  FormatAlignLeft as LeftIcon, FormatAlignCenter as CenterIcon, FormatAlignRight as RightIcon, FormatSize as LargeIcon,
} from '@mui/icons-material';
import {
  useSaveReceiptLayout, printReceiptSample, LAYOUT_VARS, TAXATION_TYPES, VAT_TYPES,
  type FiscalState, type LayoutLine, type ReceiptLayout,
} from '../../api/fiscal';
import { apiError } from '../../api/inventory';

const fill = (text: string) => LAYOUT_VARS.reduce((t, v) => t.split(v.key).join(v.sample), text);

const SAMPLE = [
  { name: 'Шаурма классическая + Сыр', qty: 2, price: 350 },
  { name: 'Морс клюквенный', qty: 1, price: 120 },
];

const money = (v: number) => v.toFixed(2);

/** Строка «слева … справа» на всю ширину ленты */
const spread = (left: string, right: string, width: number) =>
  left.length + right.length + 1 > width ? `${left}\n${right.padStart(width)}` : left + right.padStart(width - left.length);

const wrap = (text: string, width: number) => {
  const out: string[] = [];
  for (let i = 0; i < text.length; i += width) out.push(text.slice(i, i + width));
  return out.length ? out : [''];
};

/** Узор «как QR» для предпросмотра (настоящий QR печатает касса) */
const FakeQr: React.FC<{ seed: string; size?: number }> = ({ seed, size = 84 }) => {
  const n = 21;
  let h = 0;
  for (const c of seed) h = (h * 31 + c.charCodeAt(0)) >>> 0;
  const cells: boolean[] = [];
  for (let i = 0; i < n * n; i++) { h = (h * 1103515245 + 12345) >>> 0; cells.push(((h >>> 16) & 1) === 1); }
  const finder = (x: number, y: number) => {
    for (const [fx, fy] of [[0, 0], [n - 7, 0], [0, n - 7]]) {
      const dx = x - fx, dy = y - fy;
      if (dx >= 0 && dx < 7 && dy >= 0 && dy < 7) return dx === 0 || dy === 0 || dx === 6 || dy === 6 || (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4) ? 1 : 0;
    }
    return -1;
  };
  return (
    <svg width={size} height={size} viewBox={`0 0 ${n} ${n}`} shapeRendering="crispEdges" aria-label="QR-код">
      {cells.map((on, i) => {
        const x = i % n, y = Math.floor(i / n);
        const f = finder(x, y);
        return (f === 1 || (f === -1 && on)) ? <rect key={i} x={x} y={y} width={1} height={1} fill="#111" /> : null;
      })}
    </svg>
  );
};

/** Предпросмотр бумажного чека: свой текст + то, что касса печатает сама (серым) */
export const ReceiptPreview: React.FC<{ layout: ReceiptLayout; state: FiscalState }> = ({ layout, state }) => {
  const w = layout.width;
  const total = SAMPLE.reduce((s, i) => s + i.qty * i.price, 0);
  const taxation = TAXATION_TYPES.find(t => t.value === state.taxationType)?.label ?? state.taxationType;
  const vat = VAT_TYPES.find(t => t.value === state.vat)?.label ?? state.vat;

  const Own: React.FC<{ line: LayoutLine }> = ({ line }) => (
    <Box sx={{
      textAlign: line.align, whiteSpace: 'pre-wrap', wordBreak: 'break-all',
      fontSize: line.large ? '2em' : '1em', lineHeight: 1.25, fontWeight: line.large ? 700 : 400,
      bgcolor: 'rgba(255,201,51,0.18)',
    }}>
      {fill(line.text) || ' '}
    </Box>
  );

  const fiscalPart = [
    'КАССОВЫЙ ЧЕК',
    'ПРИХОД',
    ...SAMPLE.flatMap(i => [
      ...wrap(i.name, w),
      spread(` ${i.qty} x ${money(i.price)}`, `=${money(i.qty * i.price)}`, w),
      ` ${vat === 'Без НДС' ? 'НДС не облагается' : vat}`,
    ]),
    '-'.repeat(w),
    spread('ИТОГ', `=${money(total)}`, w),
    spread('НАЛИЧНЫМИ', `=${money(total)}`, w),
    `СНО: ${taxation}`,
    `Кассир: Иванова Анна`,
    spread('ФН: 7380440800000000', 'ФД: 1234', w),
    spread('ФП: 1234567890', '28.10.26 21:45', w),
    'Сайт ФНС: www.nalog.gov.ru',
  ];

  return (
    <Box sx={{
      bgcolor: '#fffdf7', color: '#111', borderRadius: '4px', boxShadow: '0 6px 24px rgba(0,0,0,0.12)',
      px: 1.5, py: 2, fontFamily: '"Roboto Mono", "Courier New", monospace', fontSize: { xs: '10px', sm: '11px' },
      width: `calc(${w}ch + 24px)`, maxWidth: '100%', mx: 'auto', overflow: 'hidden',
    }}>
      <Box sx={{ color: '#8a857c', textAlign: 'center', whiteSpace: 'pre-wrap' }}>
        {'ПРОДАВЕЦ, ИНН, АДРЕС\n(из регистрации кассы)'}
      </Box>
      <Box sx={{ my: 0.5 }}>{layout.header.map((l, i) => <Own key={`h${i}`} line={l} />)}</Box>
      <Box sx={{ color: '#8a857c', whiteSpace: 'pre', overflow: 'hidden' }}>{fiscalPart.join('\n')}</Box>
      <Box sx={{ display: 'flex', justifyContent: 'center', my: 1, opacity: 0.45 }}><FakeQr seed="fns" size={72} /></Box>
      <Box>{layout.footer.map((l, i) => <Own key={`f${i}`} line={l} />)}</Box>
      {layout.qr && (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 1, bgcolor: 'rgba(255,201,51,0.18)', py: 0.5 }}>
          <FakeQr seed={layout.qr} />
        </Box>
      )}
    </Box>
  );
};

const LinesEditor: React.FC<{ title: string; lines: LayoutLine[]; onChange: (l: LayoutLine[]) => void }> = ({ title, lines, onChange }) => {
  const update = (i: number, patch: Partial<LayoutLine>) => onChange(lines.map((l, k) => (k === i ? { ...l, ...patch } : l)));
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
          <Button size="small" startIcon={<AddIcon />} onClick={() => onChange([...lines, { text: '', align: 'center', large: false }])} sx={{ alignSelf: 'flex-start' }}>
            Добавить строку
          </Button>
        )}
      </Box>
    </Box>
  );
};

/** Вид чека: свой текст сверху и снизу, QR-код, ширина ленты; предпросмотр и печать образца */
const ReceiptLayoutDialog: React.FC<{ open: boolean; state: FiscalState; onClose: () => void }> = ({ open, state, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('md'));
  const save = useSaveReceiptLayout();
  const [layout, setLayout] = React.useState<ReceiptLayout>(state.layout);
  const [busy, setBusy] = React.useState(false);
  const [result, setResult] = React.useState<{ ok: boolean; text: string } | null>(null);

  React.useEffect(() => {
    if (open) { setLayout(state.layout); setResult(null); }
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const sample = async () => {
    setBusy(true);
    setResult(null);
    try {
      await printReceiptSample(layout);
      setResult({ ok: true, text: 'Образец напечатан — это не чек, в ФН он не попал' });
    } catch (e) {
      setResult({ ok: false, text: apiError(e) });
    } finally {
      setBusy(false);
    }
  };

  const submit = async () => {
    setResult(null);
    try {
      await save.mutateAsync(layout);
      onClose();
    } catch (e) {
      setResult({ ok: false, text: apiError(e) });
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg" fullWidth fullScreen={fullScreen}>
      <DialogTitle>Вид чека</DialogTitle>
      <DialogContent sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(0, 1fr) auto' }, gap: 3, alignItems: 'start' }}>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5, pt: 1, minWidth: 0 }}>
          <Typography variant="body2" color="text.secondary">
            Название продавца, ИНН, позиции, итог, СНО и фискальные реквизиты касса печатает сама — по закону их менять нельзя
            (на образце они серые). Здесь — ваш текст до и после них и QR-код.
          </Typography>
          <LinesEditor title="Сверху чека" lines={layout.header} onChange={header => setLayout({ ...layout, header })} />
          <LinesEditor title="Внизу чека" lines={layout.footer} onChange={footer => setLayout({ ...layout, footer })} />
          <TextField label="QR-код внизу (ссылка)" value={layout.qr ?? ''} onChange={e => setLayout({ ...layout, qr: e.target.value || null })}
            placeholder="https://kumashaurma.ru/review" helperText="Например, на отзыв, меню или Telegram; пусто — без QR" />
          <TextField select label="Ширина ленты" value={layout.width} onChange={e => setLayout({ ...layout, width: Number(e.target.value) as ReceiptLayout['width'] })}
            sx={{ maxWidth: 260 }}>
            <MenuItem value={32}>57 мм (32 символа)</MenuItem>
            <MenuItem value={42}>80 мм, мелкий шрифт (42)</MenuItem>
            <MenuItem value={48}>80 мм (48 символов)</MenuItem>
          </TextField>
          <Box>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 0.75 }}>Можно вставить в текст:</Typography>
            <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap' }}>
              {LAYOUT_VARS.map(v => <Chip key={v.key} size="small" label={`${v.key} — ${v.label}`} />)}
            </Box>
          </Box>
          {result && <Alert severity={result.ok ? 'success' : 'error'}>{result.text}</Alert>}
        </Box>
        <Box sx={{ position: { md: 'sticky' }, top: 0, pt: 1 }}>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1, textAlign: 'center' }}>Так будет выглядеть чек</Typography>
          <ReceiptPreview layout={layout} state={state} />
        </Box>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1, flexWrap: 'wrap' }}>
        <Button onClick={sample} disabled={busy || !state.enabled} sx={{ mr: 'auto' }}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Напечатать образец
        </Button>
        <Button onClick={onClose} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={submit} disabled={save.isPending}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

export default ReceiptLayoutDialog;
