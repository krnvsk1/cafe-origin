import * as React from 'react';
import { Box, Button, Chip, Typography, CircularProgress, Alert } from '@mui/material';
import { CreditCard as CardIcon } from '@mui/icons-material';
import { useQueryClient } from '@tanstack/react-query';
import { usePaymentStatus, useStartPayment } from '../api/hooks';
import type { Order } from '../types';

const LABELS: Record<string, { text: string; color: 'default' | 'success' | 'warning' | 'error' | 'info' }> = {
  unpaid: { text: 'Не оплачен', color: 'warning' },
  pending: { text: 'Оплата обрабатывается', color: 'info' },
  paid: { text: 'Оплачен онлайн', color: 'success' },
  canceled: { text: 'Оплата не прошла', color: 'error' },
  refunded: { text: 'Деньги возвращены', color: 'default' },
};

/**
 * Блок оплаты на странице заказа: статус, кнопка «Оплатить» для неоплаченного онлайн-заказа.
 */
const OrderPaymentBlock: React.FC<{ order: Order }> = ({ order }) => {
  const queryClient = useQueryClient();
  const isOnline = order.paymentMethod === 'online';
  const { data: payment } = usePaymentStatus(order.id, isOnline);
  const startPayment = useStartPayment();
  const status = payment?.paymentStatus ?? order.paymentStatus ?? 'unpaid';

  // Как только платёж прошёл — обновляем сам заказ
  React.useEffect(() => {
    if (status === 'paid') queryClient.invalidateQueries({ queryKey: ['order', order.id] });
  }, [status, order.id, queryClient]);

  if (!isOnline) {
    return (
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
        Оплата: {order.paymentMethod === 'card' ? 'картой при получении' : 'наличными при получении'}
      </Typography>
    );
  }

  const label = LABELS[status] ?? LABELS.unpaid;
  const canPay = (status === 'unpaid' || status === 'canceled') && order.status !== 'Отменён';

  return (
    <Box sx={{ mb: 1.5 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
        <Typography variant="body2" color="text.secondary">Оплата:</Typography>
        <Chip size="small" color={label.color} label={label.text} icon={status === 'pending' ? <CircularProgress size={12} /> : undefined} />
      </Box>
      {canPay && (
        <>
          <Alert severity="warning" sx={{ mt: 1 }}>
            Заказ начнут готовить после оплаты. Неоплаченный заказ отменится автоматически.
          </Alert>
          <Button
            fullWidth
            variant="contained"
            startIcon={<CardIcon />}
            sx={{ mt: 1, borderRadius: '9999px' }}
            disabled={startPayment.isPending}
            onClick={() => startPayment.mutate(order.id)}
          >
            Оплатить {order.total} ₽
          </Button>
          {startPayment.isError && <Alert severity="error" sx={{ mt: 1 }}>Не удалось открыть оплату, попробуйте ещё раз</Alert>}
        </>
      )}
    </Box>
  );
};

export default OrderPaymentBlock;
