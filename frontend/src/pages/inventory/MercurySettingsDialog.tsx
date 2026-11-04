import * as React from 'react';
import { FieldHint, TitleWithHint } from '../../components/Hint';
import {
  Box, Button, TextField, Switch, FormControlLabel, Alert, CircularProgress,
  Dialog, DialogTitle, DialogContent, DialogActions, InputAdornment, useMediaQuery, useTheme,
} from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { saveMercurySettings, testMercurySettings, type MercuryState, type MercurySettingsInput } from '../../api/mercury';
import { apiError } from '../../api/inventory';

/** Подключение к Меркурию через Ветис.API: данные из письма о доступе + логин ВетИС и GUID площадки */
const MercurySettingsDialog: React.FC<{ open: boolean; state: MercuryState; onClose: () => void }> = ({ open, state, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const qc = useQueryClient();
  const [f, setF] = React.useState({
    enabled: true, sandbox: false, apiKey: '', login: '', password: '', issuerId: '', initiatorLogin: '', enterpriseGuid: '',
    minutes: '30', days: '14',
  });
  const [busy, setBusy] = React.useState(false);
  const [result, setResult] = React.useState<{ ok: boolean; text: string } | null>(null);

  React.useEffect(() => {
    if (!open) return;
    setResult(null);
    setF({
      enabled: state.enabled || !state.login, sandbox: state.sandbox, apiKey: '', login: state.login, password: '',
      issuerId: state.issuerId, initiatorLogin: state.initiatorLogin, enterpriseGuid: state.enterpriseGuid,
      minutes: String(state.autoSyncMinutes), days: String(state.initialDays),
    });
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const input = (): MercurySettingsInput => ({
    enabled: f.enabled, sandbox: f.sandbox, apiKey: f.apiKey.trim() || null, login: f.login.trim(), password: f.password || null,
    issuerId: f.issuerId.trim(), initiatorLogin: f.initiatorLogin.trim(), enterpriseGuid: f.enterpriseGuid.trim(),
    autoSyncMinutes: Number(f.minutes) || 0, initialDays: Number(f.days) || 14,
  });

  const test = async () => {
    setBusy(true);
    setResult(null);
    try {
      const r = await testMercurySettings(input());
      setResult({ ok: r.ok, text: r.message });
    } catch (e) {
      setResult({ ok: false, text: apiError(e) });
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    setBusy(true);
    setResult(null);
    try {
      await saveMercurySettings(input());
      await qc.invalidateQueries({ queryKey: ['mercury'] });
      onClose();
    } catch (e) {
      setResult({ ok: false, text: apiError(e) });
    } finally {
      setBusy(false);
    }
  };

  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.value });

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="sm" fullWidth>
      <DialogTitle><TitleWithHint hint="Доступ к Ветис.API выдаёт Россельхознадзор по заявке: в письме приходят API-ключ, логин и пароль и issuerId. Логин ВетИС — пользователь Меркурия, от имени которого гасятся ВСД. GUID площадки — в карточке площадки в Цербере.">Подключение к Меркурию</TitleWithHint></DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
        <FormControlLabel control={<Switch checked={f.enabled} onChange={e => setF({ ...f, enabled: e.target.checked })} />} label="Работать с Меркурием" />
        <TextField label="API-ключ" value={f.apiKey} onChange={set('apiKey')} autoComplete="off"
          placeholder={state.hasApiKey ? 'Сохранён — введите, чтобы заменить' : ''} slotProps={{ inputLabel: { shrink: state.hasApiKey || !!f.apiKey } }} />
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0,1fr)', sm: '1fr 1fr' }, gap: 1.5 }}>
          <TextField label="Логин API" value={f.login} onChange={set('login')} autoComplete="off" />
          <TextField label="Пароль API" type="password" value={f.password} onChange={set('password')} autoComplete="new-password"
            placeholder={state.hasPassword ? 'Сохранён' : ''} slotProps={{ inputLabel: { shrink: state.hasPassword || !!f.password } }} />
        </Box>
        <TextField label="issuerId" value={f.issuerId} onChange={set('issuerId')} helperText={<FieldHint text="Идентификатор хозяйствующего субъекта из письма о доступе" />} />
        <TextField label="Логин пользователя ВетИС" value={f.initiatorLogin} onChange={set('initiatorLogin')} placeholder="ivanov_ii_210101" />
        <TextField label="GUID площадки" value={f.enterpriseGuid} onChange={set('enterpriseGuid')} placeholder="7a0c9a4b-…" />
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5 }}>
          <TextField label="Проверять каждые" type="number" value={f.minutes} onChange={set('minutes')} helperText={<FieldHint text="0 — только вручную" />}
            slotProps={{ input: { endAdornment: <InputAdornment position="end">мин</InputAdornment> }, htmlInput: { min: 0, max: 1440 } }} />
          <TextField label="ВСД за последние" type="number" value={f.days} onChange={set('days')} helperText={<FieldHint text="Не больше 31 дня" />}
            slotProps={{ input: { endAdornment: <InputAdornment position="end">дн</InputAdornment> }, htmlInput: { min: 1, max: 31 } }} />
        </Box>
        <FormControlLabel control={<Switch checked={f.sandbox} onChange={e => setF({ ...f, sandbox: e.target.checked })} />}
          label="Тестовый контур (api2.vetrf.ru)" />
        {result && <Alert severity={result.ok ? 'success' : 'error'}>{result.text}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        <Button onClick={test} disabled={busy} sx={{ mr: 'auto' }} startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Проверить доступ
        </Button>
        <Button onClick={onClose} disabled={busy} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={save} disabled={busy}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

export default MercurySettingsDialog;
