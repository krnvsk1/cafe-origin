import * as React from 'react';
import { FieldHint, TitleWithHint } from '../../components/Hint';
import {
  Box, Button, TextField, Switch, FormControlLabel, Alert, CircularProgress,
  Dialog, DialogTitle, DialogContent, DialogActions, InputAdornment,
} from '@mui/material';
import { useSaveEdoSettings, testEdoSettings, type EdoState } from '../../api/edo';
import { apiError } from '../../api/inventory';

/** Подключение к СБИС (Saby): логин, пароль, интервал проверки */
const EdoSettingsDialog: React.FC<{ open: boolean; state: EdoState; onClose: () => void }> = ({ open, state, onClose }) => {
  const save = useSaveEdoSettings();
  const [enabled, setEnabled] = React.useState(true);
  const [login, setLogin] = React.useState('');
  const [password, setPassword] = React.useState('');
  const [account, setAccount] = React.useState('');
  const [minutes, setMinutes] = React.useState('15');
  const [days, setDays] = React.useState('14');
  const [busy, setBusy] = React.useState(false);
  const [result, setResult] = React.useState<{ ok: boolean; text: string } | null>(null);

  React.useEffect(() => {
    if (!open) return;
    setEnabled(state.enabled || !state.login);
    setLogin(state.login);
    setPassword('');
    setAccount(state.account ?? '');
    setMinutes(String(state.autoSyncMinutes));
    setDays(String(state.initialDays));
    setResult(null);
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const input = () => ({
    enabled, login: login.trim(), password: password || null, account: account.trim() || null,
    autoSyncMinutes: Number(minutes) || 0, initialDays: Number(days) || 14,
  });

  const test = async () => {
    setBusy(true);
    setResult(null);
    try {
      const r = await testEdoSettings(input());
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
      <DialogTitle><TitleWithHint hint="Сервис заходит в СБИС (Saby) от имени пользователя и забирает входящие УПД. Лучше завести отдельного сотрудника с доступом только к входящим документам и без подтверждения входа по SMS.">Подключение к СБИС</TitleWithHint></DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
        <FormControlLabel control={<Switch checked={enabled} onChange={e => setEnabled(e.target.checked)} />} label="Загружать накладные из СБИС" />
        <TextField label="Логин СБИС" value={login} onChange={e => setLogin(e.target.value)} autoComplete="off" />
        <TextField
          label="Пароль"
          type="password"
          value={password}
          onChange={e => setPassword(e.target.value)}
          autoComplete="new-password"
          placeholder={state.hasPassword ? 'Сохранён — введите, чтобы заменить' : ''}
          slotProps={{ inputLabel: { shrink: state.hasPassword || !!password } }}
        />
        <TextField label="Номер аккаунта (если кабинетов несколько)" value={account} onChange={e => setAccount(e.target.value)} />
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5 }}>
          <TextField
            label="Проверять каждые"
            type="number"
            value={minutes}
            onChange={e => setMinutes(e.target.value)}
            helperText={<FieldHint text="0 — только вручную" />}
            slotProps={{ input: { endAdornment: <InputAdornment position="end">мин</InputAdornment> }, htmlInput: { min: 0, max: 1440 } }}
          />
          <TextField
            label="Первая загрузка за"
            type="number"
            value={days}
            onChange={e => setDays(e.target.value)}
            helperText={<FieldHint text="С какой даты забрать накладные" />}
            slotProps={{ input: { endAdornment: <InputAdornment position="end">дн</InputAdornment> }, htmlInput: { min: 1, max: 90 } }}
          />
        </Box>
        {result && <Alert severity={result.ok ? 'success' : 'error'}>{result.text}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        <Button onClick={test} disabled={busy || !login.trim()} sx={{ mr: 'auto' }}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Проверить вход
        </Button>
        <Button onClick={onClose} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={submit} disabled={save.isPending}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

export default EdoSettingsDialog;
