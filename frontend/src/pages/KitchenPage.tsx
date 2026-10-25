import * as React from 'react';
import {
  Box, Typography, Paper, Button, Chip, IconButton, Tooltip, Alert, LinearProgress, Divider,
} from '@mui/material';
import {
  Fullscreen as FullscreenIcon,
  FullscreenExit as FullscreenExitIcon,
  VolumeUp as VolumeUpIcon,
  VolumeOff as VolumeOffIcon,
  Schedule as ScheduleIcon,
} from '@mui/icons-material';
import { useKitchenOrders, useSetOrderStatus } from '../api/staff';
import type { KitchenOrder } from '../api/staff';
import { useEventStream, useInvalidateOnOrderEvent } from '../hooks/useOrderEvents';
import { playChime, unlockAudio, isAudioBlocked, subscribeAudioState } from '../utils/sound';
import { formatScheduled } from '../utils/schedule';
import type { OrderStatus } from '../types';

const SOUND_KEY = 'kitchen-sound';

// Минуты с момента создания (или до времени для заказов ко времени)
const useNow = (intervalMs = 30000) => {
  const [now, setNow] = React.useState(Date.now());
  React.useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(t);
  }, [intervalMs]);
  return now;
};

// Пока есть принятые, но не начатые заказы, кухня повторяет сигнал с этим интервалом
const ACCEPTED_ALARM_INTERVAL_MS = 10_000;
const ACCEPTED_COLOR = '#C77700';

const COLUMNS: { status: OrderStatus; title: string; color: string }[] = [
  { status: 'Новый', title: 'Новые', color: '#1976d2' },
  { status: 'Принят', title: 'Принятые', color: ACCEPTED_COLOR },
  { status: 'Готовится', title: 'Готовятся', color: '#f57c00' },
  { status: 'Готов', title: 'Готовы', color: '#2e7d32' },
];

const KitchenCard: React.FC<{ order: KitchenOrder; now: number }> = ({ order, now }) => {
  const setStatus = useSetOrderStatus();
  // now обновляется раз в 30 с — заказ, пришедший между тиками, «моложе» now; без ограничения было «-1 мин»
  const minutes = Math.max(0, Math.floor((now - new Date(order.createdAt).getTime()) / 60000));
  const late = order.status !== 'Готов' && !order.scheduledFor && minutes >= 20;
  const isPickup = order.deliveryType !== 'Доставка';
  const accepted = order.status === 'Принят';

  const next: { label: string; status: OrderStatus } | null =
    order.status === 'Новый' ? { label: 'Принять', status: 'Принят' }
      : order.status === 'Принят' ? { label: 'Начать готовить', status: 'Готовится' }
      : order.status === 'Готовится' ? { label: 'Готово', status: 'Готов' }
      // Самовывоз/в зале кухня выдаёт сама; доставку забирает курьер
      : isPickup ? { label: 'Выдан', status: 'Выполнен' }
      : null;

  return (
    <Paper
      sx={{
        p: 1.5, mb: 1.5,
        border: '2px solid',
        borderColor: late ? 'error.main' : accepted ? ACCEPTED_COLOR : 'divider',
        // Принятый заказ мигает, пока его не начнут готовить — вместе со звуковым сигналом
        animation: accepted ? 'kitchen-accepted-pulse 1.6s ease-in-out infinite' : 'none',
        '@keyframes kitchen-accepted-pulse': {
          '0%, 100%': { boxShadow: '0 0 0 0 rgba(199, 119, 0, 0)' },
          '50%': { boxShadow: '0 0 0 5px rgba(199, 119, 0, 0.35)' },
        },
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
        <Typography variant="h5" fontWeight={800}>#{order.id}</Typography>
        <Chip size="small" label={order.deliveryType} variant="outlined" />
        <Box sx={{ flex: 1 }} />
        {order.scheduledFor ? (
          <Chip size="small" color="warning" icon={<ScheduleIcon />} label={formatScheduled(order.scheduledFor)} />
        ) : (
          <Typography variant="body2" fontWeight={700} color={late ? 'error.main' : 'text.secondary'}>
            {minutes} мин
          </Typography>
        )}
      </Box>

      {order.items.map((item, idx) => (
        <Box key={idx} sx={{ mb: 0.75 }}>
          <Typography fontWeight={700} sx={{ fontSize: '1.05rem' }}>
            {item.quantity} × {item.name}
          </Typography>
          {item.addons.length > 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ pl: 2 }}>
              + {item.addons.map(a => (a.quantity > 1 ? `${a.addonName} ×${a.quantity}` : a.addonName)).join(', ')}
            </Typography>
          )}
        </Box>
      ))}

      {order.notes && (
        <Alert severity="warning" icon={false} sx={{ mt: 1, py: 0 }}>{order.notes}</Alert>
      )}

      <Divider sx={{ my: 1 }} />
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }} noWrap>{order.customerName}</Typography>
        {(order.status === 'Новый' || accepted) && (
          <Button size="small" color="error" onClick={() => {
            if (window.confirm(`Отменить заказ #${order.id}?`)) setStatus.mutate({ id: order.id, status: 'Отменён' });
          }}>
            Отменить
          </Button>
        )}
        {next && (
          <Button
            variant="contained"
            size="large"
            disabled={setStatus.isPending}
            onClick={() => setStatus.mutate({ id: order.id, status: next.status })}
          >
            {next.label}
          </Button>
        )}
        {!next && <Chip size="small" label="Ждёт курьера" />}
      </Box>
    </Paper>
  );
};

const KitchenPage: React.FC = () => {
  const { data: orders, isLoading, error } = useKitchenOrders();
  const invalidate = useInvalidateOnOrderEvent();
  const now = useNow();
  const containerRef = React.useRef<HTMLDivElement>(null);
  const [isFullscreen, setIsFullscreen] = React.useState(false);
  const [soundOn, setSoundOn] = React.useState(() => {
    try { return localStorage.getItem(SOUND_KEY) !== 'off'; } catch { return true; }
  });

  useEventStream('/api/events/orders', (evt) => {
    invalidate(evt);
    if (evt.type === 'created' && soundOn) playChime();
  });

  // Сигнал повторяется, пока есть принятые заказы; «Начать готовить» у последнего — и звук пропадает
  const ringing = soundOn && (orders ?? []).some(o => o.status === 'Принят');
  React.useEffect(() => {
    if (!ringing) return;
    playChime();
    const timer = setInterval(playChime, ACCEPTED_ALARM_INTERVAL_MS);
    return () => clearInterval(timer);
  }, [ringing]);

  // Браузер включает звук только после действия пользователя — разблокируем по любому касанию или клавише на экране кухни
  React.useEffect(() => {
    document.addEventListener('pointerdown', unlockAudio);
    document.addEventListener('keydown', unlockAudio);
    return () => {
      document.removeEventListener('pointerdown', unlockAudio);
      document.removeEventListener('keydown', unlockAudio);
    };
  }, []);
  const audioBlocked = React.useSyncExternalStore(subscribeAudioState, isAudioBlocked);

  React.useEffect(() => {
    const onChange = () => setIsFullscreen(!!document.fullscreenElement);
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, []);

  const toggleSound = () => {
    const next = !soundOn;
    setSoundOn(next);
    if (next) playChime();
    try { localStorage.setItem(SOUND_KEY, next ? 'on' : 'off'); } catch { /* ignore */ }
  };

  const toggleFullscreen = () => {
    if (document.fullscreenElement) void document.exitFullscreen();
    else void containerRef.current?.requestFullscreen?.();
  };

  const later = (orders ?? []).filter(o => o.isLater);
  const current = (orders ?? []).filter(o => !o.isLater);

  return (
    <Box ref={containerRef} sx={{ bgcolor: 'background.default', p: isFullscreen ? 2 : 0, minHeight: isFullscreen ? '100vh' : undefined, overflow: 'auto' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 2 }}>
        <Typography variant="h4" fontWeight={700} sx={{ flex: 1 }}>Кухня</Typography>
        <Tooltip title={soundOn ? 'Выключить звук новых заказов' : 'Включить звук новых заказов'}>
          <IconButton onClick={toggleSound}>{soundOn ? <VolumeUpIcon /> : <VolumeOffIcon />}</IconButton>
        </Tooltip>
        <Tooltip title={isFullscreen ? 'Выйти из полноэкранного режима' : 'На весь экран'}>
          <IconButton onClick={toggleFullscreen}>{isFullscreen ? <FullscreenExitIcon /> : <FullscreenIcon />}</IconButton>
        </Tooltip>
      </Box>

      {soundOn && audioBlocked && (
        <Alert severity="warning" icon={<VolumeOffIcon />} sx={{ mb: 2, cursor: 'pointer' }}>
          Браузер заблокировал звук — коснитесь экрана, чтобы слышать новые заказы
        </Alert>
      )}

      {isLoading && <LinearProgress />}
      {error && <Alert severity="error">Не удалось загрузить заказы</Alert>}

      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: `repeat(${COLUMNS.length + (later.length ? 1 : 0)}, minmax(0, 1fr))` },
          gap: 2,
          alignItems: 'start',
        }}
      >
        {COLUMNS.map(col => {
          const list = current.filter(o => o.status === col.status);
          return (
            <Box key={col.status}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5, borderBottom: `3px solid ${col.color}`, pb: 0.5 }}>
                <Typography variant="h6" fontWeight={700}>{col.title}</Typography>
                <Chip size="small" label={list.length} />
              </Box>
              {list.map(o => <KitchenCard key={o.id} order={o} now={now} />)}
              {list.length === 0 && (
                <Typography variant="body2" color="text.secondary" sx={{ textAlign: 'center', py: 3 }}>Пусто</Typography>
              )}
            </Box>
          );
        })}
        {later.length > 0 && (
          <Box>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5, borderBottom: '3px solid #9e9e9e', pb: 0.5 }}>
              <Typography variant="h6" fontWeight={700}>Ко времени (позже)</Typography>
              <Chip size="small" label={later.length} />
            </Box>
            {later.map(o => <KitchenCard key={o.id} order={o} now={now} />)}
          </Box>
        )}
      </Box>
    </Box>
  );
};

export default KitchenPage;
