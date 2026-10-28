import * as React from 'react';
import {
  Typography, Button, TextField, Switch, FormControlLabel, Alert, CircularProgress, MenuItem,
  Dialog, DialogTitle, DialogContent, DialogActions, Box,
} from '@mui/material';
import {
  useSaveFiscalSettings, testFiscalSettings, TAXATION_TYPES, VAT_TYPES, type FiscalState, type FiscalSettings,
} from '../../api/fiscal';
import { apiError } from '../../api/inventory';

/** Подключение онлайн-кассы АТОЛ через АТОЛ Web Server (драйвер ДТО 10) */
const FiscalSettingsDialog: React.FC<{ open: boolean; state: FiscalState; onClose: () => void }> = ({ open, state, onClose }) => {
  const save = useSaveFiscalSettings();
  const [form, setForm] = React.useState<FiscalSettings>(state);
  const [busy, setBusy] = React.useState(false);
  const [result, setResult] = React.useState<{ ok: boolean; text: string } | null>(null);

  React.useEffect(() => {
    if (!open) return;
    setForm({ ...state, enabled: state.enabled || !state.url || state.url === 'http://127.0.0.1:16732' });
    setResult(null);
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const set = <K extends keyof FiscalSettings>(k: K, v: FiscalSettings[K]) => setForm(f => ({ ...f, [k]: v }));
  const input = (): FiscalSettings => ({ ...form, url: form.url.trim(), cashierInn: form.cashierInn?.trim() || null });

  const test = async () => {
    setBusy(true);
    setResult(null);
    try {
      const r = await testFiscalSettings(input());
      setResult({ ok: r.ok, text: r.message });
    } catch (e) {
      setResult({ ok: false, text: apiError(e) });
    } finally {
      setBusy(false);
    }
  };

  const submit = async () => {
    setResult(null);
    try {
      await save.mutateAsync(input());
      onClose();
    } catch (e) {
      setResult({ ok: false, text: apiError(e) });
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Онлайн-касса АТОЛ</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
        <Typography variant="body2" color="text.secondary">
          На компьютере, к которому подключена касса, должен работать АТОЛ Web Server (входит в драйвер ДТО 10,
          порт 16732). Сервис отправляет туда чек сразу после оплаты на кассе — чек печатается и уходит в ФН и ОФД.
          Если касса занята iiko, отключите в iiko печать чеков, иначе чек пробьётся дважды.
        </Typography>
        <FormControlLabel control={<Switch checked={form.enabled} onChange={e => set('enabled', e.target.checked)} />} label="Пробивать чеки на кассе" />
        <TextField label="Адрес АТОЛ Web Server" value={form.url} onChange={e => set('url', e.target.value)} placeholder="http://192.168.1.86:16732"
          helperText="IP компьютера с кассой в локальной сети и порт 16732" />
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 1.5 }}>
          <TextField select label="Система налогообложения" value={form.taxationType} onChange={e => set('taxationType', e.target.value)}>
            {TAXATION_TYPES.map(t => <MenuItem key={t.value} value={t.value}>{t.label}</MenuItem>)}
          </TextField>
          <TextField select label="НДС" value={form.vat} onChange={e => set('vat', e.target.value)}>
            {VAT_TYPES.map(t => <MenuItem key={t.value} value={t.value}>{t.label}</MenuItem>)}
          </TextField>
          <TextField select label="Блюда в чеке" value={form.paymentObject} onChange={e => set('paymentObject', e.target.value)}>
            <MenuItem value="commodity">Товар</MenuItem>
            <MenuItem value="service">Услуга</MenuItem>
          </TextField>
          <TextField label="ИНН кассира (необязательно)" value={form.cashierInn ?? ''} onChange={e => set('cashierInn', e.target.value)}
            slotProps={{ htmlInput: { inputMode: 'numeric', maxLength: 12 } }} />
        </Box>
        <FormControlLabel
          control={<Switch checked={form.sendToCustomer} onChange={e => set('sendToCustomer', e.target.checked)} />}
          label="Отправлять электронный чек на телефон гостя, если он указан"
        />
        <Typography variant="caption" color="text.secondary">
          Систему налогообложения и ставку НДС уточните у бухгалтера: они должны совпадать с регистрацией кассы, иначе касса откажет.
        </Typography>
        {result && <Alert severity={result.ok ? 'success' : 'error'}>{result.text}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        <Button onClick={test} disabled={busy || !form.url.trim()} sx={{ mr: 'auto' }}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Проверить связь
        </Button>
        <Button onClick={onClose} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={submit} disabled={save.isPending}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

export default FiscalSettingsDialog;
