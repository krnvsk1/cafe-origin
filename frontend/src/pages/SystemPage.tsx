import * as React from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Box, Typography, Paper, Chip, Alert, CircularProgress, ToggleButtonGroup, ToggleButton, Collapse, ButtonBase, TextField, Button, Tabs, Tab,
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

interface AlertsInfo { telegramChatId: number | null; telegramEnabled: boolean; ordersChatId: number | null }

/** Куда приходят тревоги: отдельный Telegram-чат, не чат заказов */
const AlertsSettings: React.FC = () => {
  const qc = useQueryClient();
  const { data } = useQuery<AlertsInfo>({ queryKey: ['system-alerts'], queryFn: () => apiClient.get('/api/admin/system/alerts').then(r => r.data) });
  const [chat, setChat] = React.useState('');
  const [msg, setMsg] = React.useState<{ ok: boolean; text: string } | null>(null);
  React.useEffect(() => { if (data) setChat(data.telegramChatId?.toString() ?? ''); }, [data]);
  const save = useMutation({
    mutationFn: (id: number | null) => apiClient.put('/api/admin/system/alerts', { telegramChatId: id }).then(r => r.data),
    onSuccess: d => { qc.setQueryData(['system-alerts'], d); setMsg({ ok: true, text: 'Сохранено' }); },
    onError: e => setMsg({ ok: false, text: apiError(e) }),
  });
  const test = useMutation({
    mutationFn: () => apiClient.post('/api/admin/system/alerts/test'),
    onSuccess: () => setMsg({ ok: true, text: 'Проверочное сообщение отправлено — посмотрите в чате' }),
    onError: e => setMsg({ ok: false, text: apiError(e) }),
  });
  const trimmed = chat.trim();
  const valid = trimmed === '' || /^-?\d+$/.test(trimmed);
  const sameAsOrders = valid && trimmed !== '' && data?.ordersChatId != null && Number(trimmed) === data.ordersChatId;
  const changed = trimmed !== (data?.telegramChatId?.toString() ?? '');

  return (
    <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      <Typography sx={{ fontWeight: 700 }}>Тревоги в Telegram</Typography>
      <Typography variant="body2" color="text.secondary">
        Сбои сервера, недоступная база, мало места, падающие синхронизации. Нужен отдельный чат — не тот, куда приходят заказы:
        создайте группу, добавьте в неё бота, напишите в ней /id и вставьте сюда полученный номер.
      </Typography>
      {data && !data.telegramEnabled && <Alert severity="warning">Telegram-бот не настроен — тревоги отправлять некуда</Alert>}
      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'flex-start' }}>
        <TextField size="small" label="ID чата для тревог" placeholder="-1001234567890" value={chat}
          onChange={e => { setChat(e.target.value); setMsg(null); }} sx={{ minWidth: 220 }}
          error={!valid || sameAsOrders}
          helperText={!valid ? 'Только цифры, у групп — с минусом' : sameAsOrders ? 'Это чат заказов — нужен другой' : 'Пусто — тревоги выключены'} />
        <Button variant="contained" disabled={!valid || sameAsOrders || !changed || save.isPending}
          onClick={() => save.mutate(trimmed === '' ? null : Number(trimmed))}>Сохранить</Button>
        <Button disabled={!data?.telegramChatId || changed || test.isPending} onClick={() => test.mutate()}>Проверить</Button>
      </Box>
      {msg && <Alert severity={msg.ok ? 'success' : 'error'} onClose={() => setMsg(null)}>{msg.text}</Alert>}
    </Paper>
  );
};

interface EndpointRow {
  method: string; route: string; count: number; errors5xx: number; errors4xx: number; errorRate: number;
  p50Ms: number; p95Ms: number; maxMs: number; lastError: string | null; lastErrorStatus: number | null; lastErrorRequestId: string | null;
}
interface EndpointsInfo {
  minutes: number; since: string;
  alertRule: { minRequests: number; maxErrorRate: number; windowMinutes: number };
  items: EndpointRow[];
}

const WINDOWS = [{ v: 15, label: '15 мин' }, { v: 60, label: 'Час' }, { v: 1440, label: 'Сутки' }];
const SORTS: { v: 'errors' | 'slow' | 'busy'; label: string }[] = [{ v: 'errors', label: 'Ошибки' }, { v: 'slow', label: 'Медленные' }, { v: 'busy', label: 'Частые' }];
const ms = (v: number) => (v >= 1000 ? `${(v / 1000).toFixed(v >= 10000 ? 0 : 1)} с` : `${v} мс`);

/** Ручки API: сколько запросов, сколько ошибок, как быстро отвечают — за 15 минут, час или сутки */
const EndpointsView: React.FC = () => {
  const [minutes, setMinutes] = React.useState(60);
  const [sort, setSort] = React.useState<'errors' | 'slow' | 'busy'>('errors');
  const [q, setQ] = React.useState('');
  const { data, isLoading, error } = useQuery<EndpointsInfo>({
    queryKey: ['system-endpoints', minutes],
    queryFn: () => apiClient.get('/api/admin/system/endpoints', { params: { minutes } }).then(r => r.data),
    refetchInterval: 30_000,
  });
  if (isLoading) return <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>;
  if (error || !data) return <Alert severity="error">{apiError(error, 'Не удалось получить статистику ручек')}</Alert>;

  const query = q.trim().toLowerCase();
  const rows = data.items
    .filter(r => !query || `${r.method} ${r.route}`.toLowerCase().includes(query))
    .sort((a, b) => sort === 'errors' ? (b.errors5xx - a.errors5xx) || (b.errorRate - a.errorRate) || (b.p95Ms - a.p95Ms)
      : sort === 'slow' ? b.p95Ms - a.p95Ms : b.count - a.count);
  const total = data.items.reduce((x, r) => x + r.count, 0);
  const errors = data.items.reduce((x, r) => x + r.errors5xx, 0);
  const slow = data.items.filter(r => r.p95Ms >= 1000).length;
  const rule = data.alertRule;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
        <ToggleButtonGroup size="small" exclusive value={minutes} onChange={(_, v) => v && setMinutes(v)}>
          {WINDOWS.map(w => <ToggleButton key={w.v} value={w.v}>{w.label}</ToggleButton>)}
        </ToggleButtonGroup>
        <ToggleButtonGroup size="small" exclusive value={sort} onChange={(_, v) => v && setSort(v)}>
          {SORTS.map(s => <ToggleButton key={s.v} value={s.v}>{s.label}</ToggleButton>)}
        </ToggleButtonGroup>
        <TextField size="small" placeholder="Поиск ручки" value={q} onChange={e => setQ(e.target.value)} sx={{ flex: '1 1 180px' }} />
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 1.5 }}>
        {[
          ['Запросов', total.toLocaleString('ru-RU')],
          ['Ошибок сервера', `${errors}${total ? ` · ${(100 * errors / total).toFixed(1)}%` : ''}`],
          ['Медленных ручек', String(slow)],
        ].map(([label, value]) => (
          <Paper key={label} variant="outlined" sx={{ p: 1.5, borderRadius: '16px' }}>
            <Typography variant="body2" color="text.secondary">{label}</Typography>
            <Typography sx={{ fontWeight: 800 }}>{value}</Typography>
          </Paper>
        ))}
      </Box>

      <Paper variant="outlined" sx={{ borderRadius: '16px', overflow: 'hidden' }}>
        <Box sx={{ display: { xs: 'none', md: 'grid' }, gridTemplateColumns: '1fr 80px 120px 90px 90px 150px', gap: 1, px: 2, py: 1,
          bgcolor: 'action.hover', fontSize: '0.8rem', color: 'text.secondary', fontWeight: 600 }}>
          <span>Ручка</span><span>Запросов</span><span>Ошибки 5xx / 4xx</span><span>Обычно</span><span>95%</span><span>Последняя ошибка</span>
        </Box>
        {rows.length === 0 && <Typography color="text.secondary" sx={{ p: 2 }}>За это время запросов не было</Typography>}
        {rows.map(r => {
          const bad = r.errors5xx > 0 && r.errorRate > rule.maxErrorRate;
          const warn = r.errors5xx > 0 || r.p95Ms >= 1000;
          return (
            <Box key={`${r.method} ${r.route}`} sx={{
              display: 'grid', gridTemplateColumns: { xs: '1fr auto', md: '1fr 80px 120px 90px 90px 150px' }, gap: 1, px: 2, py: 1,
              borderTop: '1px solid', borderColor: 'divider', alignItems: 'center',
              bgcolor: bad ? 'rgba(211,47,47,0.08)' : warn ? 'rgba(237,108,2,0.06)' : 'transparent',
            }}>
              <Box sx={{ minWidth: 0 }}>
                <Typography variant="body2" sx={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>
                  <b>{r.method}</b> {r.route}
                </Typography>
                <Typography variant="caption" color="text.secondary" sx={{ display: { md: 'none' } }}>
                  {r.count} запр. · 5xx {r.errors5xx} ({r.errorRate}%) · 4xx {r.errors4xx} · обычно {ms(r.p50Ms)} · 95% {ms(r.p95Ms)}
                </Typography>
              </Box>
              <Typography variant="body2" sx={{ display: { xs: 'none', md: 'block' } }}>{r.count}</Typography>
              <Typography variant="body2" sx={{ display: { xs: 'none', md: 'block' }, color: r.errors5xx ? 'error.main' : 'text.secondary', fontWeight: r.errors5xx ? 700 : 400 }}>
                {r.errors5xx} ({r.errorRate}%) / {r.errors4xx}
              </Typography>
              <Typography variant="body2" sx={{ display: { xs: 'none', md: 'block' } }}>{ms(r.p50Ms)}</Typography>
              <Typography variant="body2" sx={{ display: { xs: 'none', md: 'block' }, color: r.p95Ms >= 1000 ? 'warning.main' : 'text.primary', fontWeight: r.p95Ms >= 1000 ? 700 : 400 }}>
                {ms(r.p95Ms)}
              </Typography>
              <Typography variant="caption" color="text.secondary">
                {r.lastError ? `${when(r.lastError)} · ${r.lastErrorStatus}${r.lastErrorRequestId ? ` · ${r.lastErrorRequestId}` : ''}` : ''}
              </Typography>
            </Box>
          );
        })}
      </Paper>
      <Typography variant="body2" color="text.secondary">
        Считается с запуска сервера ({when(data.since)}); после перезапуска начинается заново. «Обычно» — половина запросов быстрее,
        «95%» — почти все. Тревога в Telegram, если за {rule.windowMinutes} мин у ручки больше {rule.maxErrorRate}% ошибок сервера
        (при {rule.minRequests}+ запросах). Номер запроса из последней ошибки ищется в журнале.
      </Typography>
    </Box>
  );
};

/** Состояние системы: сервер, база, фоновые задачи, последние ошибки — обновляется раз в 30 секунд */
const SystemPage: React.FC = () => {
  const [level, setLevel] = React.useState<'all' | 'Error'>('all');
  const [openLog, setOpenLog] = React.useState<number | null>(null);
  const [tab, setTab] = React.useState<'overview' | 'endpoints'>('overview');
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
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tab value="overview" label="Обзор" />
        <Tab value="endpoints" label="Ручки" />
      </Tabs>
      {tab === 'endpoints' && <EndpointsView />}
      {tab === 'overview' && (<>

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
      <AlertsSettings />
      <Typography variant="body2" color="text.secondary">
        Полный журнал — в папке logs на сервере (файл за каждый день).
      </Typography>
      </>)}
    </Box>
  );
};

export default SystemPage;
