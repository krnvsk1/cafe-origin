import * as React from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Box, Typography, Paper, Chip, Alert, CircularProgress, ToggleButtonGroup, ToggleButton, Collapse, ButtonBase,
} from '@mui/material';
import { apiClient } from '../api/client';
import { apiError } from '../api/inventory';
import { parseUtc } from '../api/waiter';

interface SystemInfo {
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  version: string;
  environment: string;
  startedAt: string;
  uptimeMinutes: number;
  memoryMb: number;
  dbSizeMb: number | null;
  checks: { name: string; title: string; status: string; description: string | null; durationMs: number }[];
  jobs: { name: string; lastSuccess: string | null; lastFailure: string | null; lastError: string | null; failuresInRow: number }[];
  logs: { at: string; level: string; message: string; exception: string | null; source: string | null; requestId: string | null }[];
}

const STATUS: Record<string, { text: string; color: 'success' | 'warning' | 'error' }> = {
  Healthy: { text: 'в порядке', color: 'success' },
  Degraded: { text: 'есть проблемы', color: 'warning' },
  Unhealthy: { text: 'не работает', color: 'error' },
};

const when = (iso: string | null) => (iso ? parseUtc(iso).toLocaleString('ru-RU', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : '—');
const uptime = (m: number) => (m < 60 ? `${m} мин` : m < 1440 ? `${Math.floor(m / 60)} ч ${m % 60} мин` : `${Math.floor(m / 1440)} дн ${Math.floor((m % 1440) / 60)} ч`);
const shortSource = (s: string | null) => (s ? s.split('.').pop() : '');

/** Состояние системы: сервер, база, фоновые задачи, последние ошибки — обновляется раз в 30 секунд */
const SystemPage: React.FC = () => {
  const [level, setLevel] = React.useState<'all' | 'Error'>('all');
  const [openLog, setOpenLog] = React.useState<number | null>(null);
  const { data, isLoading, error } = useQuery<SystemInfo>({
    queryKey: ['system'],
    queryFn: () => apiClient.get('/api/admin/system', { params: { take: 200 } }).then(r => r.data),
    refetchInterval: 30_000,
  });

  if (isLoading) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;
  if (error || !data) return <Alert severity="error" sx={{ m: 2 }}>{apiError(error, 'Не удалось получить состояние системы')}</Alert>;

  const st = STATUS[data.status] ?? STATUS.Degraded;
  const logs = data.logs.filter(l => level === 'all' || l.level === 'Error' || l.level === 'Fatal');

  return (
    <Box sx={{ maxWidth: 1100, mx: 'auto', p: { xs: 2, md: 3 }, display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
        <Typography variant="h4" sx={{ fontWeight: 800, flex: 1 }}>Состояние системы</Typography>
        <Chip color={st.color} label={st.text} sx={{ fontWeight: 700 }} />
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: 1.5 }}>
        {[
          ['Работает', uptime(data.uptimeMinutes)],
          ['Память', `${data.memoryMb} МБ`],
          ['База данных', data.dbSizeMb != null ? `${data.dbSizeMb} МБ` : '—'],
          ['Версия', `${data.version} · ${data.environment}`],
        ].map(([label, value]) => (
          <Paper key={label} variant="outlined" sx={{ p: 2, borderRadius: '16px' }}>
            <Typography variant="body2" color="text.secondary">{label}</Typography>
            <Typography sx={{ fontWeight: 800 }} noWrap>{value}</Typography>
          </Paper>
        ))}
      </Box>

      <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2 }}>
        <Typography sx={{ fontWeight: 700, mb: 1 }}>Проверки</Typography>
        {data.checks.map(c => {
          const s = STATUS[c.status] ?? STATUS.Degraded;
          return (
            <Box key={c.name} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, py: 0.75, borderTop: '1px solid', borderColor: 'divider', '&:first-of-type': { borderTop: 0 } }}>
              <Chip size="small" color={s.color} label={s.text} sx={{ minWidth: 110 }} />
              <Typography sx={{ fontWeight: 600, minWidth: 140 }}>{c.title}</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }}>{c.description}</Typography>
            </Box>
          );
        })}
      </Paper>

      {data.jobs.length > 0 && (
        <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2 }}>
          <Typography sx={{ fontWeight: 700, mb: 1 }}>Фоновые задачи</Typography>
          {data.jobs.map(j => (
            <Box key={j.name} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, py: 0.75, flexWrap: 'wrap', borderTop: '1px solid', borderColor: 'divider', '&:first-of-type': { borderTop: 0 } }}>
              <Chip size="small" color={j.failuresInRow >= 3 ? 'error' : j.failuresInRow > 0 ? 'warning' : 'success'}
                label={j.failuresInRow > 0 ? `ошибок подряд: ${j.failuresInRow}` : 'работает'} sx={{ minWidth: 110 }} />
              <Typography sx={{ fontWeight: 600, minWidth: 140 }}>{j.name}</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }}>
                успешно: {when(j.lastSuccess)}{j.lastError ? ` · последняя ошибка ${when(j.lastFailure)}: ${j.lastError}` : ''}
              </Typography>
            </Box>
          ))}
        </Paper>
      )}

      <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
          <Typography sx={{ fontWeight: 700, flex: 1 }}>Предупреждения и ошибки с момента запуска</Typography>
          <ToggleButtonGroup size="small" exclusive value={level} onChange={(_, v) => v && setLevel(v)}>
            <ToggleButton value="all">Все</ToggleButton>
            <ToggleButton value="Error">Только ошибки</ToggleButton>
          </ToggleButtonGroup>
        </Box>
        {logs.length === 0 && <Typography color="text.secondary" sx={{ py: 2 }}>Пусто — всё спокойно</Typography>}
        {logs.map((l, i) => {
          const error = l.level === 'Error' || l.level === 'Fatal';
          return (
            <Box key={i} sx={{ borderTop: '1px solid', borderColor: 'divider', '&:first-of-type': { borderTop: 0 } }}>
              <ButtonBase onClick={() => setOpenLog(openLog === i ? null : i)} disabled={!l.exception}
                sx={{ display: 'flex', alignItems: 'flex-start', gap: 1.5, py: 0.75, width: '100%', textAlign: 'left' }}>
                <Chip size="small" color={error ? 'error' : 'warning'} label={error ? 'ошибка' : 'внимание'} sx={{ minWidth: 80 }} />
                <Typography variant="body2" color="text.secondary" sx={{ minWidth: 90 }}>{when(l.at)}</Typography>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{l.message}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {shortSource(l.source)}{l.requestId ? ` · запрос ${l.requestId}` : ''}{l.exception ? ' · нажмите, чтобы увидеть подробности' : ''}
                  </Typography>
                </Box>
              </ButtonBase>
              <Collapse in={openLog === i} unmountOnExit>
                <Box component="pre" sx={{ m: 0, mb: 1, p: 1.5, bgcolor: 'action.hover', borderRadius: '10px', fontSize: '0.75rem', overflowX: 'auto', whiteSpace: 'pre-wrap' }}>
                  {l.exception}
                </Box>
              </Collapse>
            </Box>
          );
        })}
      </Paper>
      <Typography variant="body2" color="text.secondary">
        Полный журнал — в папке logs на сервере (файл за каждый день). Тревоги в Telegram включаются настройкой Monitoring:TelegramChatId.
      </Typography>
    </Box>
  );
};

export default SystemPage;
