import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, TextField, ToggleButtonGroup, ToggleButton,
  InputAdornment, Alert, Typography, Box, Chip, CircularProgress,
} from '@mui/material';
import { apiError } from '../api/inventory';

export type DiscountType = 'percent' | 'amount';

/** Сумма скидки — так же, как на сервере (не больше суммы, до копейки) */
export const discountOf = (type: DiscountType | null | undefined, value: number | null | undefined, sum: number) => {
  if (!value || value <= 0 || sum <= 0) return 0;
  const d = type === 'percent' ? sum * Math.min(value, 100) / 100 : type === 'amount' ? value : 0;
  return Math.round(Math.min(d, sum) * 100) / 100;
};

const rub = (v: number) => `${v.toLocaleString('ru-RU', { maximumFractionDigits: 2 })} ₽`;

/** Скидка процентом или суммой; «Убрать» — без скидки */
const DiscountDialog: React.FC<{
  open: boolean;
  /** От какой суммы скидка */
  base: number;
  type?: DiscountType | null;
  value?: number | null;
  onClose: () => void;
  onSave: (type: DiscountType | null, value: number | null) => Promise<unknown> | void;
}> = ({ open, base, type: type0, value: value0, onClose, onSave }) => {
  const [type, setType] = React.useState<DiscountType>(type0 ?? 'percent');
  const [value, setValue] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [err, setErr] = React.useState('');
  React.useEffect(() => {
    if (!open) return;
    setType(type0 ?? 'percent');
    setValue(value0 ? String(value0) : '');
    setErr('');
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const num = Number(value.replace(',', '.')) || 0;
  const d = discountOf(type, num, base);
  const bad = num < 0 || (type === 'percent' && num > 100);
  const save = async (t: DiscountType | null, v: number | null) => {
    setBusy(true); setErr('');
    try { await onSave(t, v); onClose(); }
    catch (e) { setErr(apiError(e)); }
    finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} maxWidth="xs" fullWidth>
      <DialogTitle>Скидка</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <ToggleButtonGroup exclusive fullWidth value={type} onChange={(_, v) => v && setType(v)}>
          <ToggleButton value="percent">Процент</ToggleButton>
          <ToggleButton value="amount">Сумма</ToggleButton>
        </ToggleButtonGroup>
        {type === 'percent' && (
          <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap' }}>
            {[5, 10, 15, 20, 50].map(p => <Chip key={p} label={`${p}%`} color={num === p ? 'primary' : 'default'} onClick={() => setValue(String(p))} />)}
          </Box>
        )}
        <TextField autoFocus label={type === 'percent' ? 'Процент' : 'Сумма скидки'} value={value} onChange={e => setValue(e.target.value)} inputMode="decimal"
          error={bad} helperText={bad ? 'Процент — от 0 до 100' : base > 0 ? `Скидка ${rub(d)} · к оплате ${rub(base - d)}` : undefined}
          slotProps={{ input: { endAdornment: <InputAdornment position="end">{type === 'percent' ? '%' : '₽'}</InputAdornment> } }} />
        <Typography variant="caption" color="text.secondary">Скидка распределится по позициям кассового чека.</Typography>
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        {(value0 ?? 0) > 0 && <Button color="error" onClick={() => save(null, null)} disabled={busy} sx={{ mr: 'auto' }}>Убрать скидку</Button>}
        <Button onClick={onClose} disabled={busy} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" disabled={busy || bad || num <= 0} onClick={() => save(type, num)}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>Применить</Button>
      </DialogActions>
    </Dialog>
  );
};

export default DiscountDialog;
