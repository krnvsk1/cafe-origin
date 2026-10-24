import * as React from 'react';
import {
  Box, Typography, Paper, ToggleButton, ToggleButtonGroup, TextField, LinearProgress, Alert,
  Table, TableHead, TableRow, TableCell, TableBody, Button, Rating,
} from '@mui/material';
import { Insights as InsightsIcon, TableRows as TableIcon, BarChart as ChartIcon } from '@mui/icons-material';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../api/client';
import SimpleBarChart from '../components/SimpleBarChart';

interface Analytics {
  from: string;
  to: string;
  summary: {
    revenue: number; revenueChange: number | null;
    orders: number; ordersChange: number | null;
    averageCheck: number; averageCheckChange: number | null;
    cancelled: number; customers: number; newCustomers: number; repeatCustomers: number;
    deliveryRevenue: number; promoOrders: number; promoDiscount: number;
    pointsSpent: number; pointsEarned: number;
    reviewsCount: number; averageRating: number | null;
  };
  byDay: { date: string; revenue: number; orders: number }[];
  byHour: { hour: number; orders: number }[];
  topItems: { name: string; quantity: number; revenue: number }[];
  deliveryTypes: { type: string; orders: number; revenue: number }[];
  paymentMethods: { method: string; orders: number; revenue: number }[];
}

const rub = (v: number) => `${Math.round(v).toLocaleString('ru-RU')} ₽`;
const short = (v: number) => (v >= 1000 ? `${(v / 1000).toLocaleString('ru-RU', { maximumFractionDigits: 1 })}k` : String(Math.round(v)));
const PAYMENT_LABELS: Record<string, string> = { online: 'Онлайн', card: 'Картой при получении', cash: 'Наличными' };

const toInputDate = (d: Date) => {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
};

const Kpi: React.FC<{ title: string; value: string; change?: number | null; hint?: string }> = ({ title, value, change, hint }) => (
  <Paper sx={{ p: 2.5 }}>
    <Typography variant="body2" color="text.secondary">{title}</Typography>
    <Typography variant="h4" fontWeight={800} sx={{ my: 0.5 }}>{value}</Typography>
    {change !== undefined && change !== null && (
      <Typography variant="body2" color={change >= 0 ? 'success.main' : 'error.main'} fontWeight={600}>
        {change >= 0 ? '▲' : '▼'} {Math.abs(change)}% к прошлому периоду
      </Typography>
    )}
    {hint && <Typography variant="body2" color="text.secondary">{hint}</Typography>}
  </Paper>
);

const ShareList: React.FC<{ rows: { label: string; value: number; extra: string }[] }> = ({ rows }) => {
  const total = rows.reduce((s, r) => s + r.value, 0) || 1;
  return (
    <Box>
      {rows.map(r => (
        <Box key={r.label} sx={{ mb: 1.25 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.25 }}>
            <Typography variant="body2">{r.label}</Typography>
            <Typography variant="body2" color="text.secondary">{r.extra} · {Math.round((r.value / total) * 100)}%</Typography>
          </Box>
          <LinearProgress variant="determinate" value={(r.value / total) * 100} sx={{ height: 8, borderRadius: 4 }} />
        </Box>
      ))}
      {rows.length === 0 && <Typography variant="body2" color="text.secondary">Нет данных</Typography>}
    </Box>
  );
};

const AnalyticsPage: React.FC = () => {
  const [preset, setPreset] = React.useState<number | 'custom'>(30);
  const today = toInputDate(new Date());
  const [customFrom, setCustomFrom] = React.useState(toInputDate(new Date(Date.now() - 29 * 86400000)));
  const [customTo, setCustomTo] = React.useState(today);
  const [dayView, setDayView] = React.useState<'chart' | 'table'>('chart');

  const range = preset === 'custom'
    ? { from: customFrom, to: customTo }
    : { from: toInputDate(new Date(Date.now() - (preset - 1) * 86400000)), to: today };

  const { data, isLoading, error } = useQuery<Analytics>({
    queryKey: ['analytics', range.from, range.to],
    queryFn: () => apiClient.get('/api/analytics', { params: range }).then(res => res.data),
  });

  const s = data?.summary;

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 2, flexWrap: 'wrap' }}>
        <InsightsIcon color="primary" />
        <Typography variant="h4" fontWeight={700} sx={{ flex: 1 }}>Аналитика</Typography>
        <ToggleButtonGroup size="small" exclusive value={preset} onChange={(_, v) => v !== null && setPreset(v)}>
          <ToggleButton value={7}>7 дней</ToggleButton>
          <ToggleButton value={30}>30 дней</ToggleButton>
          <ToggleButton value={90}>90 дней</ToggleButton>
          <ToggleButton value="custom">Период</ToggleButton>
        </ToggleButtonGroup>
        {preset === 'custom' && (
          <Box sx={{ display: 'flex', gap: 1 }}>
            <TextField size="small" type="date" label="С" value={customFrom} onChange={e => setCustomFrom(e.target.value)} InputLabelProps={{ shrink: true }} />
            <TextField size="small" type="date" label="По" value={customTo} onChange={e => setCustomTo(e.target.value)} InputLabelProps={{ shrink: true }} />
          </Box>
        )}
      </Box>

      {isLoading && <LinearProgress sx={{ mb: 2 }} />}
      {error && <Alert severity="error">Не удалось загрузить аналитику</Alert>}

      {s && data && (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: 2, mb: 3 }}>
            <Kpi title="Выручка" value={rub(s.revenue)} change={s.revenueChange} />
            <Kpi title="Заказы" value={String(s.orders)} change={s.ordersChange} hint={s.cancelled ? `отменено: ${s.cancelled}` : undefined} />
            <Kpi title="Средний чек" value={rub(s.averageCheck)} change={s.averageCheckChange} />
            <Kpi title="Клиенты" value={String(s.customers)} hint={`новых: ${s.newCustomers} · повторных: ${s.repeatCustomers}`} />
          </Box>

          <Paper sx={{ p: 2.5, mb: 3 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', mb: 1 }}>
              <Typography variant="h6" fontWeight={700} sx={{ flex: 1 }}>Выручка по дням</Typography>
              <Button size="small" startIcon={dayView === 'chart' ? <TableIcon /> : <ChartIcon />} onClick={() => setDayView(v => (v === 'chart' ? 'table' : 'chart'))}>
                {dayView === 'chart' ? 'Таблица' : 'График'}
              </Button>
            </Box>
            {dayView === 'chart' ? (
              <SimpleBarChart
                ariaLabel="Выручка по дням"
                formatValue={short}
                data={data.byDay.map(d => {
                  const date = new Date(`${d.date}T12:00:00`);
                  return {
                    key: d.date,
                    label: date.toLocaleDateString('ru-RU', { day: 'numeric', month: 'short' }),
                    value: d.revenue,
                    tooltip: `${date.toLocaleDateString('ru-RU', { weekday: 'short', day: 'numeric', month: 'long' })}\n${rub(d.revenue)} · заказов: ${d.orders}`,
                  };
                })}
              />
            ) : (
              <Box sx={{ maxHeight: 320, overflow: 'auto' }}>
                <Table size="small" stickyHeader>
                  <TableHead><TableRow><TableCell>Дата</TableCell><TableCell align="right">Заказы</TableCell><TableCell align="right">Выручка</TableCell></TableRow></TableHead>
                  <TableBody>
                    {[...data.byDay].reverse().map(d => (
                      <TableRow key={d.date}>
                        <TableCell>{new Date(`${d.date}T12:00:00`).toLocaleDateString('ru-RU')}</TableCell>
                        <TableCell align="right">{d.orders}</TableCell>
                        <TableCell align="right">{rub(d.revenue)}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Box>
            )}
          </Paper>

          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2, mb: 3 }}>
            <Paper sx={{ p: 2.5 }}>
              <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>Заказы по часам</Typography>
              <SimpleBarChart
                ariaLabel="Количество заказов по часам дня"
                height={200}
                data={data.byHour.map(h => ({
                  key: String(h.hour),
                  label: String(h.hour),
                  value: h.orders,
                  tooltip: `${String(h.hour).padStart(2, '0')}:00–${String(h.hour).padStart(2, '0')}:59\nзаказов: ${h.orders}`,
                }))}
              />
            </Paper>
            <Paper sx={{ p: 2.5 }}>
              <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>Популярные позиции</Typography>
              <Table size="small">
                <TableHead><TableRow><TableCell>Позиция</TableCell><TableCell align="right">Шт.</TableCell><TableCell align="right">Выручка</TableCell></TableRow></TableHead>
                <TableBody>
                  {data.topItems.map(i => (
                    <TableRow key={i.name}>
                      <TableCell>{i.name}</TableCell>
                      <TableCell align="right">{i.quantity}</TableCell>
                      <TableCell align="right">{rub(i.revenue)}</TableCell>
                    </TableRow>
                  ))}
                  {data.topItems.length === 0 && <TableRow><TableCell colSpan={3}>Нет данных</TableCell></TableRow>}
                </TableBody>
              </Table>
            </Paper>
          </Box>

          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3, 1fr)' }, gap: 2 }}>
            <Paper sx={{ p: 2.5 }}>
              <Typography variant="h6" fontWeight={700} sx={{ mb: 1.5 }}>Способ получения</Typography>
              <ShareList rows={data.deliveryTypes.map(d => ({ label: d.type, value: d.orders, extra: `${d.orders} зак.` }))} />
            </Paper>
            <Paper sx={{ p: 2.5 }}>
              <Typography variant="h6" fontWeight={700} sx={{ mb: 1.5 }}>Оплата</Typography>
              <ShareList rows={data.paymentMethods.map(p => ({ label: PAYMENT_LABELS[p.method] ?? p.method, value: p.revenue, extra: rub(p.revenue) }))} />
            </Paper>
            <Paper sx={{ p: 2.5 }}>
              <Typography variant="h6" fontWeight={700} sx={{ mb: 1.5 }}>Скидки и лояльность</Typography>
              <Typography variant="body2" sx={{ mb: 0.75 }}>Заказов с промокодом: <b>{s.promoOrders}</b> · скидка {rub(s.promoDiscount)}</Typography>
              <Typography variant="body2" sx={{ mb: 0.75 }}>Оплачено баллами: <b>{rub(s.pointsSpent)}</b></Typography>
              <Typography variant="body2" sx={{ mb: 0.75 }}>Начислено баллов: <b>{s.pointsEarned}</b></Typography>
              <Typography variant="body2" sx={{ mb: 0.75 }}>Выручка с доставки: <b>{rub(s.deliveryRevenue)}</b></Typography>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 1 }}>
                <Typography variant="body2">Оценка:</Typography>
                {s.averageRating !== null ? (
                  <>
                    <Rating value={s.averageRating} precision={0.1} readOnly size="small" />
                    <Typography variant="body2" color="text.secondary">{s.averageRating} ({s.reviewsCount})</Typography>
                  </>
                ) : <Typography variant="body2" color="text.secondary">нет отзывов</Typography>}
              </Box>
            </Paper>
          </Box>
        </>
      )}
    </Box>
  );
};

export default AnalyticsPage;
