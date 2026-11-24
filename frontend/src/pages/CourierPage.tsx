import * as React from 'react';
import {
  Box, Typography, Paper, Button, Chip, Tabs, Tab, Alert, LinearProgress, Stack, Snackbar,
} from '@mui/material';
import {
  Phone as PhoneIcon,
  Map as MapIcon,
  Schedule as ScheduleIcon,
} from '@mui/icons-material';
import { useCourierOrders, useCourierAction } from '../api/staff';
import type { CourierOrder, CourierAction } from '../api/staff';
import { useEventStream, useInvalidateOnOrderEvent } from '../hooks/useOrderEvents';
import { formatScheduled } from '../utils/schedule';
import { apiError } from '../api/inventory';

const mapUrl = (address: string) => `https://yandex.ru/maps/?text=${encodeURIComponent(address)}`;

const CourierOrderCard: React.FC<{
  order: CourierOrder;
  mine: boolean;
  onAction: (id: number, action: CourierAction) => void;
  busy: boolean;
}> = ({ order, mine, onAction, busy }) => (
  <Paper sx={{ p: 2, mb: 1.5 }}>
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1, flexWrap: 'wrap' }}>
      <Typography variant="h6" fontWeight={800}>#{order.id}</Typography>
      <Chip size="small" label={order.status} color={order.status === 'В пути' ? 'secondary' : order.status === 'Готов' ? 'success' : 'default'} />
      {order.scheduledFor && <Chip size="small" color="warning" icon={<ScheduleIcon />} label={formatScheduled(order.scheduledFor)} />}
      <Box sx={{ flex: 1 }} />
      {order.amountToCollect > 0
        ? <Typography fontWeight={700}>Получить {order.amountToCollect} ₽ {order.paymentMethod === 'card' ? '(карта)' : '(наличные)'}</Typography>
        : <Chip size="small" color="success" label="Оплачен" />}
    </Box>

    <Typography fontWeight={600}>{order.address}</Typography>
    <Typography variant="body2" color="text.secondary">
      {order.customerName} · {order.itemsCount} поз.
    </Typography>
    <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{order.items.join(', ')}</Typography>
    {order.notes && <Alert severity="info" icon={false} sx={{ mt: 1, py: 0 }}>{order.notes}</Alert>}

    <Stack direction="row" spacing={1} sx={{ mt: 1.5, flexWrap: 'wrap', gap: 1 }}>
      {mine && (
        <>
          <Button component="a" size="small" variant="outlined" startIcon={<PhoneIcon />} href={`tel:${order.phone}`}>Позвонить</Button>
          <Button component="a" size="small" variant="outlined" startIcon={<MapIcon />} href={mapUrl(order.address)} target="_blank" rel="noopener">Маршрут</Button>
        </>
      )}
      <Box sx={{ flex: 1 }} />
      {!mine && (
        <Button variant="contained" disabled={busy} onClick={() => onAction(order.id, 'take')}>Взять заказ</Button>
      )}
      {mine && order.status !== 'В пути' && (
        <>
          <Button size="small" color="inherit" disabled={busy} onClick={() => onAction(order.id, 'release')}>Отказаться</Button>
          <Button variant="contained" disabled={busy || order.status !== 'Готов'} onClick={() => onAction(order.id, 'pickup')}>
            {order.status === 'Готов' ? 'Забрал, выезжаю' : 'Ещё готовится'}
          </Button>
        </>
      )}
      {mine && order.status === 'В пути' && (
        <Button variant="contained" color="success" disabled={busy} onClick={() => onAction(order.id, 'delivered')}>Доставлен</Button>
      )}
    </Stack>
  </Paper>
);

const CourierPage: React.FC = () => {
  const { data, isLoading, error } = useCourierOrders();
  const action = useCourierAction();
  const invalidate = useInvalidateOnOrderEvent();
  const [tab, setTab] = React.useState<'mine' | 'available'>('mine');
  const [toast, setToast] = React.useState('');

  useEventStream('/api/events/orders', invalidate);

  const mine = data?.mine ?? [];
  const available = data?.available ?? [];

  React.useEffect(() => {
    // Если своих заказов нет — сразу показываем свободные
    if (data && mine.length === 0 && available.length > 0) setTab('available');
  }, [data]); // eslint-disable-line react-hooks/exhaustive-deps

  const handleAction = async (id: number, act: CourierAction) => {
    try {
      await action.mutateAsync({ id, action: act });
      if (act === 'take') { setTab('mine'); setToast(`Заказ #${id} ваш`); }
      if (act === 'delivered') setToast(`Заказ #${id} доставлен 👍`);
    } catch (e: unknown) {
      setToast(apiError(e, 'Не удалось выполнить действие'));
    }
  };

  const list = tab === 'mine' ? mine : available;

  return (
    <Box sx={{ maxWidth: 720, mx: 'auto' }}>
      <Typography variant="h4" fontWeight={700} sx={{ mb: 2 }}>Доставка</Typography>
      <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="fullWidth" sx={{ mb: 2 }}>
        <Tab value="mine" label={`Мои (${mine.length})`} />
        <Tab value="available" label={`Свободные (${available.length})`} />
      </Tabs>

      {isLoading && <LinearProgress />}
      {error && <Alert severity="error">Не удалось загрузить заказы</Alert>}
      {!isLoading && list.length === 0 && (
        <Typography color="text.secondary" sx={{ textAlign: 'center', py: 4 }}>
          {tab === 'mine' ? 'У вас нет активных заказов' : 'Свободных заказов нет'}
        </Typography>
      )}

      {list.map(o => (
        <CourierOrderCard key={o.id} order={o} mine={tab === 'mine'} onAction={handleAction} busy={action.isPending} />
      ))}

      <Snackbar open={!!toast} autoHideDuration={4000} onClose={() => setToast('')} message={toast} />
    </Box>
  );
};

export default CourierPage;
