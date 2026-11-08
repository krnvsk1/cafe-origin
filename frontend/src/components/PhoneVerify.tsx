import * as React from 'react';
import { Box, Button, TextField, Typography, Alert, CircularProgress } from '@mui/material';
import { sendPhoneCode, verifyPhoneCode } from '../api/phone';
import { apiError } from '../api/inventory';
import { useAuthStore } from '../store/authStore';

/**
 * Подтверждение телефона по SMS перед заказом: номер → «Получить код» → код → телефон сохранён в профиле.
 */
const PhoneVerify: React.FC<{ phone: string; onPhoneChange: (v: string) => void; disabled?: boolean }> = ({ phone, onPhoneChange, disabled }) => {
  const setUser = useAuthStore(s => s.setUser);
  const [sentTo, setSentTo] = React.useState('');
  const [code, setCode] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [err, setErr] = React.useState('');
  const [wait, setWait] = React.useState(0);

  React.useEffect(() => {
    if (wait <= 0) return;
    const t = setTimeout(() => setWait(w => w - 1), 1000);
    return () => clearTimeout(t);
  }, [wait]);

  const send = async () => {
    setBusy(true); setErr('');
    try {
      const r = await sendPhoneCode(phone);
      setSentTo(r.phone); setCode(''); setWait(30);
    } catch (e) {
      setErr(apiError(e, 'Не удалось отправить SMS'));
      const retry = (e as { response?: { data?: { retryAfter?: number } } })?.response?.data?.retryAfter;
      if (retry) setWait(retry);
    } finally { setBusy(false); }
  };

  const verify = async () => {
    setBusy(true); setErr('');
    try {
      const user = await verifyPhoneCode(sentTo, code);
      setUser(user);
      onPhoneChange(user.phone);
    } catch (e) { setErr(apiError(e, 'Неверный код')); }
    finally { setBusy(false); }
  };

  return (
    <Box sx={{ mt: 2, p: 2, borderRadius: 3, bgcolor: 'action.hover' }}>
      <Typography sx={{ fontWeight: 700 }}>Подтвердите телефон</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
        Пришлём SMS с кодом — один раз, дальше номер сохранится в профиле.
      </Typography>
      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'flex-start' }}>
        <TextField size="small" label="Телефон" type="tel" value={phone} onChange={e => { onPhoneChange(e.target.value); setSentTo(''); }}
          disabled={disabled || busy} placeholder="+7 999 123-45-67" sx={{ flex: '1 1 180px' }}
          slotProps={{ htmlInput: { inputMode: 'tel', autoComplete: 'tel' } }} />
        <Button variant="outlined" onClick={send} disabled={disabled || busy || !phone.trim() || wait > 0} sx={{ flex: '0 0 auto', minHeight: 40 }}>
          {wait > 0 ? `Повторно через ${wait} с` : sentTo ? 'Отправить ещё раз' : 'Получить код'}
        </Button>
      </Box>
      {sentTo && (
        <Box sx={{ display: 'flex', gap: 1, mt: 1.5, flexWrap: 'wrap', alignItems: 'flex-start' }}>
          <TextField size="small" label="Код из SMS" value={code} autoFocus onChange={e => setCode(e.target.value.replace(/\D/g, '').slice(0, 4))}
            disabled={busy} sx={{ flex: '1 1 140px' }} slotProps={{ htmlInput: { inputMode: 'numeric', autoComplete: 'one-time-code' } }}
            onKeyDown={e => { if (e.key === 'Enter' && code.length === 4) verify(); }} />
          <Button variant="contained" color="secondary" onClick={verify} disabled={busy || code.length !== 4} sx={{ flex: '0 0 auto', minHeight: 40 }}
            startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>Подтвердить</Button>
        </Box>
      )}
      {err && <Alert severity="error" sx={{ mt: 1.5 }}>{err}</Alert>}
    </Box>
  );
};

export default PhoneVerify;
