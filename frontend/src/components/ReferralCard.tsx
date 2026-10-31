import * as React from 'react';
import { useBrand } from '../api/hooks';
import { Paper, Box, Typography, TextField, Button, Snackbar, Skeleton, InputAdornment, IconButton, Tooltip } from '@mui/material';
import { ContentCopy as CopyIcon, Share as ShareIcon, CardGiftcard as GiftIcon } from '@mui/icons-material';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../api/client';

interface ReferralInfo {
  enabled: boolean;
  code: string | null;
  invitedCount: number;
  rewardedCount: number;
  earnedPoints: number;
  inviterBonus: number;
  inviteeBonus: number;
  minOrderAmount: number;
}

/** «Пригласи друга»: персональная ссылка и статистика приглашений */
const ReferralCard: React.FC = () => {
  const brand = useBrand();
  const { data, isLoading } = useQuery<ReferralInfo>({
    queryKey: ['referral'],
    queryFn: () => apiClient.get('/api/referrals/me').then(res => res.data),
  });
  const [toast, setToast] = React.useState('');

  if (isLoading) return <Skeleton variant="rounded" height={180} sx={{ mb: 3 }} />;
  if (!data?.enabled || !data.code) return null;

  const link = `${window.location.origin}/?ref=${data.code}`;
  const text = `Попробуй «${brand.name}»! По моей ссылке получишь ${data.inviteeBonus} баллов после первого заказа 🍽️`;

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(link);
      setToast('Ссылка скопирована');
    } catch {
      setToast('Не удалось скопировать — выделите ссылку вручную');
    }
  };

  const share = async () => {
    if (navigator.share) {
      try { await navigator.share({ title: brand.name, text, url: link }); } catch { /* отменено */ }
    } else {
      void copy();
    }
  };

  return (
    <Paper sx={{ p: 3, mb: 3, borderRadius: 4 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
        <GiftIcon color="secondary" />
        <Typography variant="h6" fontWeight={700}>Пригласите друга</Typography>
      </Box>
      <Typography color="text.secondary" sx={{ mb: 2 }}>
        Друг получит <b>{data.inviteeBonus}</b> баллов, а вы — <b>{data.inviterBonus}</b>, когда он получит первый заказ
        {data.minOrderAmount > 0 ? ` от ${data.minOrderAmount} ₽` : ''}.
      </Typography>
      <TextField
        fullWidth
        size="small"
        value={link}
        InputProps={{
          readOnly: true,
          endAdornment: (
            <InputAdornment position="end">
              <Tooltip title="Скопировать">
                <IconButton onClick={copy} edge="end"><CopyIcon /></IconButton>
              </Tooltip>
            </InputAdornment>
          ),
        }}
        sx={{ mb: 1.5 }}
      />
      <Box sx={{ display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
        <Button variant="contained" startIcon={<ShareIcon />} onClick={share} sx={{ borderRadius: '9999px' }}>
          Поделиться
        </Button>
        <Typography variant="body2" color="text.secondary">
          Приглашено: <b>{data.invitedCount}</b> · сделали заказ: <b>{data.rewardedCount}</b> · заработано: <b>{data.earnedPoints}</b> баллов
        </Typography>
      </Box>
      <Snackbar open={!!toast} autoHideDuration={3000} onClose={() => setToast('')} message={toast} />
    </Paper>
  );
};

export default ReferralCard;
