import * as React from 'react';
import {
  Box, Typography, Paper, Button, Chip, IconButton, Tooltip, Alert, LinearProgress, Divider,
  Menu, MenuItem, ListItemIcon, ListItemText, ListSubheader,
} from '@mui/material';
import {
  VolumeUp as VolumeUpIcon,
  VolumeOff as VolumeOffIcon,
  Schedule as ScheduleIcon,
  MusicNote as MusicNoteIcon,
  MenuBookOutlined as RecipeIcon,
  Check as CheckIcon,
} from '@mui/icons-material';
import { useKitchenOrders, useSetOrderStatus, useKitchenStations, useSetStationStatus, useKitchenItem, useResolveVoid } from '../api/staff';
import type { KitchenOrder, KitchenOrderItem } from '../api/staff';
import { useEventStream, useInvalidateOnOrderEvent } from '../hooks/useOrderEvents';
import {
  playChime, unlockAudio, isAudioBlocked, subscribeAudioState, MELODIES, getMelody, setMelody, type MelodyId,
} from '../utils/sound';
import { formatScheduled } from '../utils/schedule';
import FullscreenButton from '../components/FullscreenButton';
import KitchenTechCardDialog from '../components/KitchenTechCardDialog';
import type { OrderStatus } from '../types';

const SOUND_KEY = 'kitchen-sound';
// Какую кухню показывает этот экран (у каждого планшета своя)
const STATION_KEY = 'kitchen-station';

// Минуты с момента создания (или до времени для заказов ко времени)
const useNow = (intervalMs = 30000) => {
  const [now, setNow] = React.useState(Date.now());
  React.useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(t);
  }, [intervalMs]);
  return now;
};

// Пока в «Новых» есть заказ, кухня повторяет сигнал с этим интервалом
const NEW_ORDER_ALARM_INTERVAL_MS = 10_000;
const NEW_COLOR = '#1976d2';
const ACCEPTED_COLOR = '#C77700';

const COLUMNS: { status: OrderStatus; title: string; color: string }[] = [
  { status: 'Новый', title: 'Новые', color: NEW_COLOR },
  { status: 'Принят', title: 'Принятые', color: ACCEPTED_COLOR },
  { status: 'Готовится', title: 'Готовятся', color: '#f57c00' },
  { status: 'Готов', title: 'Готовы', color: '#2e7d32' },
];

/** Статус заказа для колонки: на экране кухни — статус её части, на общем — заказа */
const statusFor = (order: KitchenOrder, stationId: number | null): OrderStatus =>
  stationId == null ? order.status : (order.stations.find(s => s.id === stationId)?.status ?? order.status);

const KitchenCard: React.FC<{ order: KitchenOrder; now: number; stationId: number | null; onRecipe: (item: KitchenOrderItem) => void }> = ({ order, now, stationId, onRecipe }) => {
  const setStatus = useSetOrderStatus();
  const setStation = useSetStationStatus();
  const itemMark = useKitchenItem();
  const resolveVoid = useResolveVoid();
  // Просьбы убрать блюда этой кухни (на общем экране — все)
  const voidRequests = (order.voidRequests ?? []).filter(v => stationId == null
    || order.items.some(i => i.id === v.orderItemId && i.stationId === stationId));
  const busy = setStatus.isPending || setStation.isPending || itemMark.isPending;
  const status = statusFor(order, stationId);
  // now обновляется раз в 30 с — заказ, пришедший между тиками, «моложе» now; без ограничения было «-1 мин»
  const minutes = Math.max(0, Math.floor((now - new Date(order.createdAt).getTime()) / 60000));
  const late = status !== 'Готов' && !order.scheduledFor && minutes >= 20;
  const isPickup = order.deliveryType !== 'Доставка';
  // Заказ официанта: блюда гостям отдаёт сам официант со своего экрана
  const byWaiter = !!order.tableName;
  const accepted = status === 'Принят';
  // Новый заказ (не «ко времени») звенит и мигает, пока его не примут
  const ringing = status === 'Новый' && !order.isLater;
  const multi = order.stations.length > 1;
  const others = stationId == null ? [] : order.stations.filter(s => s.id !== stationId);
  const waiting = order.stations.filter(s => s.status !== 'Готов' && s.id !== stationId);

  // Своя часть на экране кухни; заказ целиком — на общем экране
  const advance = (next: OrderStatus) => {
    if (stationId != null && next !== 'Выполнен') setStation.mutate({ orderId: order.id, stationId, status: next });
    else setStatus.mutate({ id: order.id, status: next });
  };

  const next: { label: string; status: OrderStatus } | null =
    status === 'Новый' ? { label: 'Принять', status: 'Принят' }
      : status === 'Принят' ? { label: 'Начать готовить', status: 'Готовится' }
      : status === 'Готовится' ? { label: 'Готово', status: 'Готов' }
      // Выдать можно, когда готов весь заказ; самовывоз/в зале кухня выдаёт сама, доставку забирает курьер
      : order.status === 'Готов' && isPickup && !byWaiter ? { label: 'Выдан', status: 'Выполнен' }
      : null;

  const items = stationId == null ? order.items : order.items.filter(i => i.stationId === stationId);
  const otherItems = stationId == null ? [] : order.items.filter(i => i.stationId !== stationId);

  // Отметки по блюдам — после того как заказ приняли
  const canMark = status !== 'Новый';
  // Часть кухни (или заказ) уже «Готов» — отмечать готовность больше нечего
  const partReady = status === 'Готов' || status === 'Выполнен';
  const renderItem = (item: KitchenOrderItem, idx: number) => {
    const ready = partReady ? item.quantity : item.readyQuantity;
    const served = item.servedQuantity;
    const toServe = ready - served;
    const done = served >= item.quantity;
    const allReady = ready >= item.quantity;
    // Для кухни блюдо закончено, когда оно готово: зачёркиваем
    const crossed = done || allReady;
    return (
      <Box key={idx} sx={{ mb: 0.75, opacity: crossed ? 0.45 : 1 }}>
        <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 0.5 }}>
          <Typography fontWeight={700} sx={{ fontSize: '1.05rem', flex: 1, textDecoration: crossed ? 'line-through' : 'none' }}>
            {item.quantity} × {item.name}
          </Typography>
          {/* Техкарта — по запросу повара, чтобы не загромождать карточку */}
          <Tooltip title="Техкарта">
            <IconButton size="small" onClick={() => onRecipe(item)} aria-label={`Техкарта: ${item.name}`} sx={{ mt: -0.25, color: 'text.secondary' }}>
              <RecipeIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Box>
        {item.addons.length > 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ pl: 2 }}>
            + {item.addons.map(a => (a.quantity > 1 ? `${a.addonName} ×${a.quantity}` : a.addonName)).join(', ')}
          </Typography>
        )}
        {canMark && !done && (
          <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap', alignItems: 'center', mt: 0.5, pl: 2 }}>
            {ready > 0 && partReady && (
              <Chip size="small" color="success" variant="outlined" icon={<CheckIcon />} label="готово" />
            )}
            {ready > 0 && !partReady && (
              <Tooltip title="Нажмите, чтобы снять одну отметку «готово»">
                <Chip size="small" color="success" variant="outlined" icon={<CheckIcon />}
                  label={item.quantity > 1 ? `готово ${ready}/${item.quantity}` : 'готово'}
                  onClick={() => ready > served && itemMark.mutate({ itemId: item.id, action: 'ready', delta: -1 })} />
              </Tooltip>
            )}
            {served > 0 && <Chip size="small" label={`отдано ${served}`} />}
            {!allReady && (
              <Button size="small" variant="outlined" color="success" disabled={busy}
                onClick={() => itemMark.mutate({ itemId: item.id, action: 'ready', delta: item.quantity > 1 ? 1 : null })}>
                {item.quantity > 1 ? '+1 готово' : 'Готово'}
              </Button>
            )}
            {!allReady && item.quantity - ready > 1 && (
              <Button size="small" color="success" disabled={busy} onClick={() => itemMark.mutate({ itemId: item.id, action: 'ready', delta: null })}>
                все готовы
              </Button>
            )}
            {toServe > 0 && isPickup && !byWaiter && (
              <Button size="small" variant="contained" color="success" disabled={busy}
                onClick={() => itemMark.mutate({ itemId: item.id, action: 'served', delta: null })}>
                Отдать{toServe > 1 ? ` ${toServe}` : ''}
              </Button>
            )}
          </Box>
        )}
      </Box>
    );
  };

  return (
    <Paper
      sx={{
        p: 1.5, mb: 1.5,
        border: '2px solid',
        borderColor: late ? 'error.main' : ringing ? NEW_COLOR : accepted ? ACCEPTED_COLOR : 'divider',
        animation: ringing ? 'kitchen-new-pulse 1.6s ease-in-out infinite' : 'none',
        '@keyframes kitchen-new-pulse': {
          '0%, 100%': { boxShadow: '0 0 0 0 rgba(25, 118, 210, 0)' },
          '50%': { boxShadow: '0 0 0 5px rgba(25, 118, 210, 0.35)' },
        },
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
        <Typography variant="h5" fontWeight={800}>#{order.id}</Typography>
        {order.tableName
          ? <Chip size="small" color="secondary" label={`Стол ${order.tableName}`} sx={{ fontWeight: 800 }} />
          : <Chip size="small" label={order.deliveryType} variant="outlined" />}
        {order.waiterName && <Typography variant="body2" color="text.secondary" noWrap sx={{ minWidth: 0 }}>{order.waiterName}</Typography>}
        <Box sx={{ flex: 1 }} />
        {order.scheduledFor ? (
          <Chip size="small" color="warning" icon={<ScheduleIcon />} label={formatScheduled(order.scheduledFor)} />
        ) : (
          <Typography variant="body2" fontWeight={700} color={late ? 'error.main' : 'text.secondary'}>
            {minutes} мин
          </Typography>
        )}
      </Box>

      {stationId == null && multi
        ? order.stations.map(st => (
          <Box key={st.id} sx={{ mb: 0.5 }}>
            <Typography variant="caption" sx={{ fontWeight: 800, textTransform: 'uppercase', color: 'text.secondary' }}>
              {st.name} · {st.status.toLowerCase()}
            </Typography>
            {order.items.filter(i => i.stationId === st.id).map(renderItem)}
          </Box>
        ))
        : items.map(renderItem)}

      {otherItems.length > 0 && (
        <Box sx={{ mt: 0.5, px: 1, py: 0.5, borderRadius: 1, bgcolor: 'action.hover' }}>
          {others.map(st => (
            <Typography key={st.id} variant="body2" color="text.secondary">
              <b>{st.name}</b> ({st.status.toLowerCase()}): {otherItems.filter(i => i.stationId === st.id).map(i => `${i.quantity}× ${i.name}`).join(', ')}
            </Typography>
          ))}
        </Box>
      )}

      {order.notes && (
        <Alert severity="warning" icon={false} sx={{ mt: 1, py: 0 }}>{order.notes}</Alert>
      )}

      {voidRequests.map(v => (
        <Alert key={v.id} severity="error" icon={false} sx={{ mt: 1, '& .MuiAlert-message': { width: '100%' } }}>
          <Typography sx={{ fontWeight: 800 }}>Официант просит убрать: {v.quantity} × {v.name}</Typography>
          <Typography variant="body2">Причина: {v.reason}</Typography>
          <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap', mt: 1 }}>
            <Button size="small" variant="contained" color="error" disabled={resolveVoid.isPending}
              onClick={() => resolveVoid.mutate({ id: v.id, decision: 'writeoff' })}>Убрать, списать продукты</Button>
            <Button size="small" variant="outlined" color="error" disabled={resolveVoid.isPending}
              onClick={() => resolveVoid.mutate({ id: v.id, decision: 'keep' })}>Убрать без списания</Button>
          </Box>
        </Alert>
      ))}

      <Divider sx={{ my: 1 }} />
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }} noWrap>{order.customerName}</Typography>
        {next && (
          <Button variant="contained" size="large" disabled={busy} onClick={() => advance(next.status)}>
            {next.label}
          </Button>
        )}
        {!next && status === 'Готов' && waiting.length > 0 && (
          <Chip size="small" color="warning" label={`Ждёт: ${waiting.map(w => w.name).join(', ')}`} />
        )}
        {!next && status === 'Готов' && waiting.length === 0 && !isPickup && <Chip size="small" label="Ждёт курьера" />}
      </Box>
    </Paper>
  );
};

const KitchenPage: React.FC = () => {
  const { data: orders, isLoading, error } = useKitchenOrders();
  const invalidate = useInvalidateOnOrderEvent();
  const now = useNow();
  const [soundOn, setSoundOn] = React.useState(() => {
    try { return localStorage.getItem(SOUND_KEY) !== 'off'; } catch { return true; }
  });
  const [melody, setMelodyState] = React.useState<MelodyId>(getMelody);
  const [recipeItem, setRecipeItem] = React.useState<KitchenOrderItem | null>(null);
  const { data: stations = [] } = useKitchenStations();
  const [stationId, setStationIdState] = React.useState<number | null>(() => {
    try { const v = localStorage.getItem(STATION_KEY); return v ? Number(v) : null; } catch { return null; }
  });
  // Кухню удалили или их одна — показываем всё
  const station = stations.length > 1 && stationId != null && stations.some(s => s.id === stationId) ? stationId : null;
  const chooseStation = (id: number | null) => {
    setStationIdState(id);
    try { if (id == null) localStorage.removeItem(STATION_KEY); else localStorage.setItem(STATION_KEY, String(id)); } catch { /* ignore */ }
  };
  // На экране кухни — только заказы, где есть её блюда
  // На экране своей кухни заказ уходит из «Готовых», когда все её блюда готовы и отданы; на «Все кухни» — остаётся
  const stationDone = (o: KitchenOrder) => {
    const mine = o.items.filter(i => i.stationId === station);
    return mine.length > 0 && mine.every(i => i.servedQuantity >= i.quantity);
  };
  const visible = (orders ?? []).filter(o => station == null || (o.stations.some(s => s.id === station) && !stationDone(o)));

  // Живые события только обновляют список; звук зависит от того, что в «Новых» (так он есть, даже если заказ пришёл опросом)
  useEventStream('/api/events/orders', invalidate);

  // Сигнал звучит сразу и повторяется, пока в «Новых» есть заказ; приняли последний — звук пропадает
  const ringing = soundOn && visible.some(o => (statusFor(o, station) === 'Новый' && !o.isLater) || (o.voidRequests ?? []).length > 0);
  React.useEffect(() => {
    if (!ringing) return;
    playChime(melody);
    const timer = setInterval(() => playChime(melody), NEW_ORDER_ALARM_INTERVAL_MS);
    return () => clearInterval(timer);
  }, [ringing, melody]);

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

  const [melodyMenu, setMelodyMenu] = React.useState<HTMLElement | null>(null);
  // Выбор сразу проигрывает мелодию; меню не закрываем, чтобы можно было послушать все
  const chooseMelody = (id: MelodyId) => {
    setMelody(id);
    setMelodyState(id);
    playChime(id);
  };

  const toggleSound = () => {
    const next = !soundOn;
    setSoundOn(next);
    if (next) playChime(melody);
    try { localStorage.setItem(SOUND_KEY, next ? 'on' : 'off'); } catch { /* ignore */ }
  };

  const later = visible.filter(o => o.isLater);
  const current = visible.filter(o => !o.isLater);

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 2 }}>
        <Typography variant="h4" fontWeight={700}>{station == null ? 'Кухня' : stations.find(s => s.id === station)?.name}</Typography>
        <Box sx={{ flex: 1, display: 'flex', gap: 1, flexWrap: 'wrap', ml: 1 }}>
          {stations.length > 1 && (
            <>
              <Chip label="Все кухни" color={station == null ? 'primary' : 'default'} onClick={() => chooseStation(null)} />
              {stations.map(st => (
                <Chip key={st.id} label={st.name} color={station === st.id ? 'primary' : 'default'} onClick={() => chooseStation(st.id)} />
              ))}
            </>
          )}
        </Box>
        <Tooltip title={`Мелодия сигнала: ${MELODIES[melody].label}`}>
          <IconButton onClick={e => setMelodyMenu(e.currentTarget)} aria-label="Выбрать мелодию сигнала">
            <MusicNoteIcon />
          </IconButton>
        </Tooltip>
        <Menu anchorEl={melodyMenu} open={!!melodyMenu} onClose={() => setMelodyMenu(null)}>
          <ListSubheader sx={{ lineHeight: '36px' }}>Новый заказ — повтор каждые 10 с, пока не примут</ListSubheader>
          {(Object.keys(MELODIES) as MelodyId[]).map(id => (
            <MenuItem key={id} selected={id === melody} onClick={() => chooseMelody(id)}>
              <ListItemIcon>{id === melody && <CheckIcon fontSize="small" />}</ListItemIcon>
              <ListItemText>{MELODIES[id].label}</ListItemText>
            </MenuItem>
          ))}
        </Menu>
        <Tooltip title={soundOn ? 'Выключить звук новых заказов' : 'Включить звук новых заказов'}>
          <IconButton onClick={toggleSound}>{soundOn ? <VolumeUpIcon /> : <VolumeOffIcon />}</IconButton>
        </Tooltip>
        <FullscreenButton />
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
          const list = current.filter(o => statusFor(o, station) === col.status);
          return (
            <Box key={col.status}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5, borderBottom: `3px solid ${col.color}`, pb: 0.5 }}>
                <Typography variant="h6" fontWeight={700}>{col.title}</Typography>
                <Chip size="small" label={list.length} />
              </Box>
              {list.map(o => <KitchenCard key={o.id} order={o} now={now} stationId={station} onRecipe={setRecipeItem} />)}
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
            {later.map(o => <KitchenCard key={o.id} order={o} now={now} stationId={station} onRecipe={setRecipeItem} />)}
          </Box>
        )}
      </Box>
      <KitchenTechCardDialog item={recipeItem} onClose={() => setRecipeItem(null)} />
    </Box>
  );
};

export default KitchenPage;
