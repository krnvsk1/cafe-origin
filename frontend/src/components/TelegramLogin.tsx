import * as React from 'react';
import { Box, Button, CircularProgress, Divider, Typography } from '@mui/material';
import { Telegram as TelegramIcon } from '@mui/icons-material';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../api/client';

interface AuthOk {
  success: boolean;
  message?: string;
  accessToken?: string;
  refreshToken?: string;
  user?: unknown;
}

const POLL_MS = 2000;

/**
 * Вход через Telegram: сайт получает ссылку на бота, гость нажимает «Запустить» в Telegram,
 * а сайт ждёт подтверждения и входит. Аккаунт создаётся при первом входе.
 */
const TelegramLogin: React.FC<{ onSuccess: (r: AuthOk) => void; onError: (text: string) => void; divider?: boolean }> = ({ onSuccess, onError, divider }) => {
  const { data: info } = useQuery<{ enabled: boolean }>({
    queryKey: ['telegram-web-login'],
    queryFn: () => apiClient.get('/api/telegram/web-login').then(r => r.data),
    staleTime: 5 * 60 * 1000,
  });
  const [session, setSession] = React.useState<{ code: string; url: string } | null>(null);
  const [starting, setStarting] = React.useState(false);

  // Ждём, пока гость подтвердит вход в боте
  React.useEffect(() => {
    if (!session) return;
    let stop = false;
    const tick = async () => {
      try {
        const r = await apiClient.get<AuthOk & { pending?: boolean }>(`/api/telegram/web-login/${session.code}`);
        if (stop) return;
        if (r.data.pending) { timer = setTimeout(tick, POLL_MS); return; }
        if (r.data.success && r.data.accessToken) { setSession(null); onSuccess(r.data); return; }
        setSession(null);
        onError(r.data.message ?? 'Не удалось войти через Telegram');
      } catch (e) {
        if (stop) return;
        const status = (e as { response?: { status?: number; data?: { message?: string } } }).response;
        setSession(null);
        onError(status?.data?.message ?? 'Не удалось войти через Telegram — попробуйте ещё раз');
      }
    };
    let timer = setTimeout(tick, POLL_MS);
    return () => { stop = true; clearTimeout(timer); };
  }, [session]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!info?.enabled) return null;

  const start = async () => {
    setStarting(true);
    try {
      const r = await apiClient.post<{ code: string; url: string }>('/api/telegram/web-login');
      setSession(r.data);
    } catch {
      onError('Вход через Telegram сейчас недоступен');
    } finally { setStarting(false); }
  };

  const sep = divider ? <Divider sx={{ my: 2, color: 'text.secondary', fontSize: '0.85rem' }}>или</Divider> : null;

  if (session) {
    return (
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, alignItems: 'stretch', textAlign: 'center' }}>
        {sep}
        <Typography variant="body2" color="text.secondary">
          Откройте бота и нажмите «Запустить» (или «Start») — вход на сайте выполнится сам.
        </Typography>
        <Button
          component="a" href={session.url} target="_blank" rel="noopener noreferrer"
          variant="contained" size="large" startIcon={<TelegramIcon />}
          sx={{ borderRadius: '9999px', py: 1.5, textTransform: 'none', fontWeight: 600, bgcolor: '#229ED9', '&:hover': { bgcolor: '#1c8dc2' } }}
        >
          Открыть Telegram
        </Button>
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1, color: 'text.secondary' }}>
          <CircularProgress size={16} color="inherit" />
          <Typography variant="body2">Ждём подтверждения…</Typography>
        </Box>
        <Button size="small" onClick={() => setSession(null)} sx={{ color: 'text.secondary', textTransform: 'none' }}>Отмена</Button>
      </Box>
    );
  }

  return (
    <>
    {sep}
    <Button
      fullWidth variant="outlined" size="large" onClick={start} disabled={starting}
      startIcon={starting ? <CircularProgress size={18} /> : <TelegramIcon sx={{ color: '#229ED9' }} />}
      sx={{ borderRadius: '9999px', py: 1.5, textTransform: 'none', fontWeight: 600 }}
    >
      Войти через Telegram
    </Button>
    </>
  );
};

export default TelegramLogin;
