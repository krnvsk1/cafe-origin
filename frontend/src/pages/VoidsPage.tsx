import * as React from 'react';
import { Box, Typography, TextField, Alert, CircularProgress, Chip, Paper } from '@mui/material';
import { useVoidsReport, parseUtc, type VoidRow } from '../api/waiter';
import { apiError } from '../api/inventory';
import { todayMsk } from '../api/haccp';

const money = (v: number) => `${Math.round(v).toLocaleString('ru-RU')} ₽`;

const decision = (v: VoidRow): { text: string; color: 'default' | 'warning' | 'error' | 'success' } => {
  if (v.status === 'pending') return { text: 'ждёт кухню', color: 'warning' };
  if (v.status === 'rejected') return { text: 'кухня отказала', color: 'default' };
  return v.writeOff ? { text: 'списано', color: 'error' } : { text: 'без списания', color: 'success' };
};

/** Отменённые блюда: что, кто, когда и почему убрал из счёта, и решение кухни */
const VoidsPage: React.FC = () => {
  const today = todayMsk();
  const [from, setFrom] = React.useState(today.slice(0, 8) + '01');
  const [to, setTo] = React.useState(today);
  const { data, isLoading, error } = useVoidsReport(from, to);

  const tiles = data ? [
    { label: 'Отмен', value: String(data.summary.count) },
    { label: 'На сумму', value: money(data.summary.amount) },
    { label: 'Списано продуктов', value: money(data.summary.writtenOff) },
    { label: 'Ждут кухню', value: String(data.summary.pending) },
  ] : [];

  return (
    <Box sx={{ maxWidth: 1100, mx: 'auto', p: { xs: 2, md: 3 }, display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Typography variant="h4" sx={{ fontWeight: 800 }}>Отменённые блюда</Typography>
      <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap' }}>
        <TextField type="date" size="small" label="С" value={from} onChange={e => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
        <TextField type="date" size="small" label="По" value={to} onChange={e => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
      </Box>
      {error && <Alert severity="error">{apiError(error, 'Не удалось загрузить отмены')}</Alert>}
      {isLoading && <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>}
      {data && (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: 1.5 }}>
            {tiles.map(t => (
              <Paper key={t.label} variant="outlined" sx={{ p: 2, borderRadius: '16px' }}>
                <Typography variant="body2" color="text.secondary">{t.label}</Typography>
                <Typography sx={{ fontWeight: 800, fontSize: '1.5rem' }}>{t.value}</Typography>
              </Paper>
            ))}
          </Box>
          {data.items.length === 0 ? (
            <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>За этот период блюда не отменяли</Typography>
          ) : (
            <Paper variant="outlined" sx={{ borderRadius: '16px', overflow: 'hidden' }}>
              {data.items.map(v => {
                const d = decision(v);
                const t = parseUtc(v.requestedAt);
                return (
                  <Box key={v.id} sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr auto', md: '110px 1.6fr 1fr 1fr auto' }, gap: 1, alignItems: 'center',
                    px: 2, py: 1.25, borderBottom: '1px solid', borderColor: 'divider', '&:last-child': { borderBottom: 0 } }}>
                    <Typography variant="body2" color="text.secondary" sx={{ display: { xs: 'none', md: 'block' } }}>
                      {t.toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit' })} {t.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })}
                    </Typography>
                    <Box>
                      <Typography sx={{ fontWeight: 700 }}>{v.quantity} × {v.name} · {money(v.amount)}</Typography>
                      <Typography variant="body2" color="text.secondary">{v.reason}</Typography>
                    </Box>
                    <Typography variant="body2" sx={{ display: { xs: 'none', md: 'block' } }}>
                      {v.table ? `Стол ${v.table}` : `Заказ №${v.orderId}`}<br />
                      <Typography component="span" variant="body2" color="text.secondary">{v.requestedBy ?? '—'}</Typography>
                    </Typography>
                    <Typography variant="body2" color="text.secondary" sx={{ display: { xs: 'none', md: 'block' } }}>
                      {v.orderStatus ? `было: ${v.orderStatus}` : ''}{v.resolvedBy ? <><br />решил: {v.resolvedBy}</> : null}
                    </Typography>
                    <Chip size="small" color={d.color} label={d.text} />
                  </Box>
                );
              })}
            </Paper>
          )}
        </>
      )}
    </Box>
  );
};

export default VoidsPage;
