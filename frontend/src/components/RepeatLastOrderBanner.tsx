import * as React from 'react';
import { Paper, Box, Typography, Button, Snackbar, Alert, CircularProgress } from '@mui/material';
import { Replay as ReplayIcon } from '@mui/icons-material';
import { useMyOrders } from '../api/hooks';
import { useAuthStore } from '../store/authStore';
import { useRepeatOrder, describeRepeatResult } from '../api/reorder';

/**
 * Плашка «Повторить прошлый заказ» для авторизованных клиентов.
 * Показывается, если у пользователя есть хотя бы один не отменённый заказ.
 */
const RepeatLastOrderBanner: React.FC = () => {
  const { isAuthenticated, hasRole } = useAuthStore();
  const isStaff = isAuthenticated && (hasRole('admin') || hasRole('manager') || hasRole('courier'));
  const { data: orders } = useMyOrders(isAuthenticated && !isStaff);
  const repeatOrder = useRepeatOrder();
  const [toast, setToast] = React.useState<{ message: string; severity: 'success' | 'warning' | 'error' } | null>(null);

  const lastOrder = (orders ?? []).find(o => o.status !== 'Отменён' && (o.orderItems?.length ?? 0) > 0);
  if (!isAuthenticated || isStaff || !lastOrder) return null;

  const summary = (lastOrder.orderItems ?? []).map(i => i.name).join(', ');

  const handleRepeat = async () => {
    try {
      const result = await repeatOrder.mutateAsync(lastOrder.id);
      setToast(describeRepeatResult(lastOrder.id, result));
    } catch {
      setToast({ message: 'Не удалось повторить заказ', severity: 'error' });
    }
  };

  return (
    <>
      <Paper
        elevation={0}
        sx={{
          p: 2, mb: 3, borderRadius: 3,
          border: (theme) => `1px solid ${theme.palette.divider}`,
          display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap',
        }}
      >
        <Box sx={{ flex: 1, minWidth: 200 }}>
          <Typography fontWeight={700}>Повторить прошлый заказ?</Typography>
          <Typography variant="body2" color="text.secondary" noWrap title={summary}>
            #{lastOrder.id} · {summary}
          </Typography>
        </Box>
        <Button
          variant="contained"
          startIcon={repeatOrder.isPending ? <CircularProgress size={16} color="inherit" /> : <ReplayIcon />}
          onClick={handleRepeat}
          disabled={repeatOrder.isPending}
          sx={{ borderRadius: '9999px' }}
        >
          В корзину
        </Button>
      </Paper>
      <Snackbar
        open={!!toast}
        autoHideDuration={5000}
        onClose={() => setToast(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity={toast?.severity ?? 'success'} onClose={() => setToast(null)} sx={{ width: '100%' }}>
          {toast?.message}
        </Alert>
      </Snackbar>
    </>
  );
};

export default RepeatLastOrderBanner;
