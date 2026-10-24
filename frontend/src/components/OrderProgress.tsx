import * as React from 'react';
import { Stepper, Step, StepLabel, Alert, Box, Typography } from '@mui/material';
import type { OrderStatus } from '../types';

/**
 * Прогресс заказа: Принят → Готовится → Готов → В пути → Доставлен.
 * Для самовывоза/в зале шаг «В пути» не показывается.
 */
const OrderProgress: React.FC<{ status: OrderStatus; deliveryType?: string }> = ({ status, deliveryType }) => {
  const isDelivery = !deliveryType || deliveryType === 'Доставка';
  const steps: { key: OrderStatus[]; label: string }[] = isDelivery
    ? [
        { key: ['Новый'], label: 'Принят' },
        { key: ['Готовится'], label: 'Готовится' },
        { key: ['Готов'], label: 'Готов' },
        { key: ['В пути'], label: 'В пути' },
        { key: ['Доставлен', 'Выполнен'], label: 'Доставлен' },
      ]
    : [
        { key: ['Новый'], label: 'Принят' },
        { key: ['Готовится'], label: 'Готовится' },
        { key: ['Готов'], label: 'Можно забирать' },
        { key: ['Доставлен', 'Выполнен', 'В пути'], label: 'Выдан' },
      ];

  if (status === 'Отменён') {
    return <Alert severity="error" sx={{ mb: 2 }}>Заказ отменён</Alert>;
  }

  const activeIndex = Math.max(0, steps.findIndex(s => s.key.includes(status)));
  const finished = status === 'Доставлен' || status === 'Выполнен';

  return (
    <Box sx={{ mb: 2 }}>
      <Stepper activeStep={finished ? steps.length : activeIndex} alternativeLabel>
        {steps.map(s => (
          <Step key={s.label}>
            <StepLabel>
              <Typography variant="caption">{s.label}</Typography>
            </StepLabel>
          </Step>
        ))}
      </Stepper>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', textAlign: 'center', mt: 1 }}>
        Статус обновляется автоматически
      </Typography>
    </Box>
  );
};

export default OrderProgress;
