import * as React from 'react';
import { Snackbar, Alert, Button } from '@mui/material';
import { useNavigate } from 'react-router-dom';
import { useAuthStore } from '../store/authStore';
import { useEventStream, useInvalidateOnOrderEvent } from '../hooks/useOrderEvents';
import type { OrderEvent } from '../types';

const CUSTOMER_MESSAGES: Partial<Record<OrderEvent['status'], string>> = {
  'Готовится': 'начали готовить',
  'Готов': 'готов',
  'В пути': 'передан курьеру и уже в пути',
  'Доставлен': 'доставлен. Приятного аппетита!',
  'Выполнен': 'выполнен. Приятного аппетита!',
  'Отменён': 'отменён',
};

/**
 * Глобальный слушатель событий заказов для авторизованных пользователей:
 * держит данные свежими и показывает уведомления (персоналу — о новых заказах,
 * клиенту — о смене статуса его заказа).
 */
const LiveOrdersListener: React.FC = () => {
  const { isAuthenticated, hasRole } = useAuthStore();
  const isStaff = isAuthenticated && (hasRole('admin') || hasRole('manager') || hasRole('courier'));
  const invalidate = useInvalidateOnOrderEvent();
  const navigate = useNavigate();
  const [toast, setToast] = React.useState<{ text: string; orderId: number } | null>(null);

  useEventStream(isAuthenticated ? '/api/events/orders' : null, (evt) => {
    invalidate(evt);
    if (isStaff && evt.type === 'created') {
      setToast({ text: `Новый заказ #${evt.orderId}`, orderId: evt.orderId });
    } else if (!isStaff && evt.type === 'status' && CUSTOMER_MESSAGES[evt.status]) {
      setToast({ text: `Заказ #${evt.orderId} ${CUSTOMER_MESSAGES[evt.status]}`, orderId: evt.orderId });
    }
  });

  return (
    <Snackbar
      open={!!toast}
      autoHideDuration={6000}
      onClose={() => setToast(null)}
      anchorOrigin={{ vertical: 'top', horizontal: 'center' }}
    >
      <Alert
        severity="info"
        variant="filled"
        onClose={() => setToast(null)}
        action={
          <Button
            color="inherit"
            size="small"
            onClick={() => {
              if (toast) navigate(isStaff ? '/orders' : `/order/${toast.orderId}/success`);
              setToast(null);
            }}
          >
            Открыть
          </Button>
        }
      >
        {toast?.text}
      </Alert>
    </Snackbar>
  );
};

export default LiveOrdersListener;
