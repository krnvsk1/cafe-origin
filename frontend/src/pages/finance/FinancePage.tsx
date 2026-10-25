import * as React from 'react';
import { Box, Typography, Chip, CircularProgress, Alert, ToggleButtonGroup, ToggleButton, IconButton, Tooltip, Badge } from '@mui/material';
import { ChevronRight as ChevronRightIcon, PaymentsOutlined as PaymentsIcon, CalendarMonthOutlined as CalendarIcon } from '@mui/icons-material';
import { useFinancePayments, type PaymentFilter, type BucketSum } from '../../api/finance';
import { fmtMoney, fmtDate, apiError } from '../../api/inventory';
import { Panel, StatTile, ListRow, EmptyState } from '../inventory/ui';
import { plural } from '../../components/CartPanel';
import { DueLabel } from './DueLabel';
import PaymentDialog from './PaymentDialog';
import SuppliersView from './SuppliersView';
import SupplierDialog from './SupplierDialog';
import CalendarDrawer from './CalendarDrawer';

const FILTERS: { value: PaymentFilter; label: string }[] = [
  { value: 'unpaid', label: 'К оплате' },
  { value: 'paid', label: 'Оплачено' },
  { value: 'all', label: 'Все' },
];

const DOC_FORMS: [string, string, string] = ['накладная', 'накладные', 'накладных'];

const countHint = (b: BucketSum) => (b.count ? `${b.count} ${plural(b.count, DOC_FORMS)}` : 'нет');

/** Финансы: предстоящие платежи поставщикам по накладным СБИС */
const FinancePage: React.FC = () => {
  const [tab, setTab] = React.useState<'payments' | 'suppliers'>('payments');
  const [filter, setFilter] = React.useState<PaymentFilter>('unpaid');
  const [openId, setOpenId] = React.useState<number | null>(null);
  const [supplierInn, setSupplierInn] = React.useState<string | null>(null);
  const [calendarOpen, setCalendarOpen] = React.useState(false);
  const { data, isLoading, error } = useFinancePayments(filter);
  // Календарь всегда по неоплаченным, независимо от фильтра списка (тот же запрос, что у «К оплате», — из кэша)
  const { data: unpaid } = useFinancePayments('unpaid');
  const s = data?.summary ?? unpaid?.summary;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
        <Box sx={{ flex: 1, minWidth: 220 }}>
          <Typography variant="h4" component="h1" sx={{ fontSize: { xs: '1.6rem', sm: '2rem' } }}>Финансы</Typography>
          <Typography color="text.secondary">
            Платежи поставщикам по накладным СБИС{s ? ` · всего к оплате ${fmtMoney(s.unpaidTotal)}` : ''}
          </Typography>
        </Box>
        <ToggleButtonGroup exclusive size="small" value={tab} onChange={(_, v) => v && setTab(v)}>
          <ToggleButton value="payments">Платежи</ToggleButton>
          <ToggleButton value="suppliers">Поставщики</ToggleButton>
        </ToggleButtonGroup>
      </Box>

      {tab === 'suppliers' && <SuppliersView onOpen={setSupplierInn} />}

      {tab === 'payments' && s && (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: 1.5 }}>
          <StatTile label="Просрочено" value={fmtMoney(s.overdue.total)} hint={countHint(s.overdue)} tone={s.overdue.count ? 'danger' : 'default'} />
          <StatTile label="Ближайшие 7 дней" value={fmtMoney(s.week.total)} hint={countHint(s.week)} tone={s.week.count ? 'warning' : 'default'} />
          <StatTile label="Позже" value={fmtMoney(s.later.total)} hint={countHint(s.later)} />
          <StatTile
            label="Срок не указан"
            value={fmtMoney(s.noDue.total)}
            hint={s.noDue.count ? `${countHint(s.noDue)} — задайте отсрочку поставщику` : 'нет'}
          />
        </Box>
      )}

      {tab === 'payments' && (
        <>
          <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
            {FILTERS.map(f => (
              <Chip
                key={f.value}
                label={f.label}
                onClick={() => setFilter(f.value)}
                color={filter === f.value ? 'primary' : 'default'}
                variant={filter === f.value ? 'filled' : 'outlined'}
              />
            ))}
            <Box sx={{ flex: 1 }} />
            <Tooltip title="Календарь платежей">
              <IconButton onClick={() => setCalendarOpen(true)} aria-label="Открыть календарь платежей" sx={{ bgcolor: 'background.paper' }}>
                <Badge badgeContent={unpaid?.summary.overdue.count ?? 0} color="error" max={99}>
                  <CalendarIcon />
                </Badge>
              </IconButton>
            </Tooltip>
          </Box>

          {error && <Alert severity="error">{apiError(error, 'Не удалось загрузить платежи')}</Alert>}

          <Panel padding={false}>
            {isLoading ? (
              <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>
            ) : !data || data.items.length === 0 ? (
              <EmptyState
                icon={<PaymentsIcon />}
                title={filter === 'paid' ? 'Оплаченных пока нет' : 'Платить некому'}
                text={filter === 'unpaid' ? 'Здесь появятся накладные из СБИС, которые ещё не оплачены.' : undefined}
              />
            ) : data.items.map(p => (
              <ListRow key={p.id} onClick={() => setOpenId(p.id)}>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography sx={{ fontWeight: 600 }} noWrap>{p.supplierName ?? `ИНН ${p.supplierInn ?? '—'}`}</Typography>
                  <Typography variant="body2" color="text.secondary" noWrap>
                    № {p.number ?? '—'} от {fmtDate(p.date)}
                    {p.dueDate ? ` · до ${fmtDate(p.dueDate)}` : ''}
                  </Typography>
                </Box>
                <Box sx={{ textAlign: 'right', flexShrink: 0, display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 0.5 }}>
                  <Typography sx={{ fontWeight: 700, whiteSpace: 'nowrap' }}>{fmtMoney(p.total)}</Typography>
                  <DueLabel dueDate={p.dueDate} daysLeft={p.daysLeft} paid={p.bucket === 'paid'} />
                </Box>
                <ChevronRightIcon sx={{ color: 'text.secondary', display: { xs: 'none', sm: 'block' } }} />
              </ListRow>
            ))}
          </Panel>

          {unpaid && (
            <CalendarDrawer
              open={calendarOpen}
              onClose={() => setCalendarOpen(false)}
              items={unpaid.items}
              today={unpaid.today}
              onOpenPayment={setOpenId}
            />
          )}
        </>
      )}

      <SupplierDialog inn={supplierInn} onClose={() => setSupplierInn(null)} onOpenPayment={setOpenId} />
      <PaymentDialog id={openId} onClose={() => setOpenId(null)} />
    </Box>
  );
};

export default FinancePage;
