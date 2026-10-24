import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, Typography, Alert,
  FormControlLabel, Switch, Box, CircularProgress,
} from '@mui/material';
import { Telegram as TelegramIcon } from '@mui/icons-material';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '../api/client';
import { openTelegramLink } from '../utils/telegram';

interface TelegramStatus {
  enabled: boolean;
  botUsername: string | null;
  linked: boolean;
  notifications: boolean;
}

const TelegramConnectDialog: React.FC<{ open: boolean; onClose: () => void }> = ({ open, onClose }) => {
  const queryClient = useQueryClient();
  const { data: status, isLoading } = useQuery<TelegramStatus>({
    queryKey: ['telegram-status'],
    queryFn: () => apiClient.get('/api/telegram/status').then(res => res.data),
    enabled: open,
    // Пока окно открыто — проверяем, не привязал ли пользователь бота
    refetchInterval: open ? 4000 : false,
  });

  const createLink = useMutation<{ url: string }>({
    mutationFn: () => apiClient.post('/api/telegram/link').then(res => res.data),
    onSuccess: (data) => openTelegramLink(data.url),
  });
  const unlink = useMutation({
    mutationFn: () => apiClient.delete('/api/telegram/link'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['telegram-status'] }),
  });
  const setNotifications = useMutation({
    mutationFn: (enabled: boolean) => apiClient.put('/api/telegram/notifications', { enabled }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['telegram-status'] }),
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <TelegramIcon sx={{ color: '#229ED9' }} /> Telegram
      </DialogTitle>
      <DialogContent>
        {isLoading && <Box sx={{ textAlign: 'center', py: 2 }}><CircularProgress size={28} /></Box>}
        {status && !status.enabled && (
          <Alert severity="info">Уведомления в Telegram скоро появятся.</Alert>
        )}
        {status?.enabled && !status.linked && (
          <>
            <Typography sx={{ mb: 2 }}>
              Подключите бота @{status.botUsername}, чтобы получать статусы заказов в Telegram и заказывать прямо в нём.
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Нажмите кнопку, в Telegram нажмите «Запустить» — и всё готово.
            </Typography>
          </>
        )}
        {status?.enabled && status.linked && (
          <>
            <Alert severity="success" sx={{ mb: 2 }}>Telegram подключён</Alert>
            <FormControlLabel
              control={
                <Switch
                  checked={status.notifications}
                  onChange={(e) => setNotifications.mutate(e.target.checked)}
                />
              }
              label="Уведомления о статусе заказов"
            />
          </>
        )}
        {(createLink.isError || unlink.isError) && <Alert severity="error" sx={{ mt: 2 }}>Что-то пошло не так, попробуйте ещё раз</Alert>}
      </DialogContent>
      <DialogActions>
        {status?.enabled && status.linked && (
          <Button color="error" onClick={() => unlink.mutate()} disabled={unlink.isPending}>Отключить</Button>
        )}
        <Box sx={{ flex: 1 }} />
        <Button onClick={onClose}>Закрыть</Button>
        {status?.enabled && !status.linked && (
          <Button
            variant="contained"
            startIcon={<TelegramIcon />}
            onClick={() => createLink.mutate()}
            disabled={createLink.isPending}
          >
            Подключить
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
};

export default TelegramConnectDialog;
