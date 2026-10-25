import * as React from 'react';
import { Box, Typography, CircularProgress, TextField, ToggleButtonGroup, ToggleButton, Alert } from '@mui/material';
import { useSalesReport, useConsumptionReport, fmtMoney, fmtQty, toDateInput } from '../../api/inventory';
import { tokens } from '../../theme';
import { Panel, ListRow, ListHeader, EmptyState, StatTile, FoodCost, Pill } from './ui';

type Preset = '7' | '30' | 'month' | 'custom';

const daysAgo = (n: number) => {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return toDateInput(d.toISOString());
};

const ReportsTab: React.FC = () => {
  const [report, setReport] = React.useState<'sales' | 'consumption'>('sales');
  const [preset, setPreset] = React.useState<Preset>('30');
  const [from, setFrom] = React.useState(daysAgo(29));
  const [to, setTo] = React.useState(toDateInput());

  const applyPreset = (p: Preset) => {
    setPreset(p);
    if (p === '7') { setFrom(daysAgo(6)); setTo(toDateInput()); }
    if (p === '30') { setFrom(daysAgo(29)); setTo(toDateInput()); }
    if (p === 'month') {
      const now = new Date();
      setFrom(toDateInput(new Date(now.getFullYear(), now.getMonth(), 1).toISOString()));
      setTo(toDateInput());
    }
  };

  const sales = useSalesReport(from, to);
  const consumption = useConsumptionReport(from, to);
  const loading = report === 'sales' ? sales.isLoading : consumption.isLoading;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
        <ToggleButtonGroup exclusive size="small" value={report} onChange={(_, v) => v && setReport(v)}>
          <ToggleButton value="sales">Продажи и маржа</ToggleButton>
          <ToggleButton value="consumption">Движение продуктов</ToggleButton>
        </ToggleButtonGroup>
        <ToggleButtonGroup exclusive size="small" value={preset} onChange={(_, v) => v && applyPreset(v)} sx={{ ml: { md: 'auto' } }}>
          <ToggleButton value="7">7 дней</ToggleButton>
          <ToggleButton value="30">30 дней</ToggleButton>
          <ToggleButton value="month">Этот месяц</ToggleButton>
          <ToggleButton value="custom">Период</ToggleButton>
        </ToggleButtonGroup>
        {preset === 'custom' && (
          <Box sx={{ display: 'flex', gap: 1 }}>
            <TextField size="small" type="date" label="С" value={from} onChange={e => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} sx={{ '& .MuiOutlinedInput-root': { bgcolor: 'background.paper' } }} />
            <TextField size="small" type="date" label="По" value={to} onChange={e => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} sx={{ '& .MuiOutlinedInput-root': { bgcolor: 'background.paper' } }} />
          </Box>
        )}
      </Box>

      {loading ? (
        <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
      ) : report === 'sales' && sales.data ? (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: 1.5 }}>
            <StatTile label="Выручка" value={fmtMoney(sales.data.totals.revenue)} />
            <StatTile label="Себестоимость" value={fmtMoney(sales.data.totals.cost)} hint="по блюдам с техкартой" />
            {/* Прибыль и фудкост считаются только по блюдам с техкартой — иначе их легко сравнить со всей выручкой */}
            <StatTile label="Валовая прибыль" value={fmtMoney(sales.data.totals.margin)} tone="good" hint="по блюдам с техкартой" />
            <StatTile
              label="Фудкост"
              hint="по блюдам с техкартой"
              value={sales.data.totals.foodCostPercent !== null ? `${sales.data.totals.foodCostPercent} %` : '—'}
              tone={(sales.data.totals.foodCostPercent ?? 0) > 40 ? 'danger' : (sales.data.totals.foodCostPercent ?? 0) > 30 ? 'warning' : 'good'}
            />
          </Box>
          {sales.data.totals.withoutCardCount > 0 && (
            <Alert severity="warning">
              {sales.data.totals.withoutCardCount} поз. ({sales.data.totals.withoutCardQuantity} шт.) продано без техкарты — по ним не считается себестоимость и не списываются продукты.
            </Alert>
          )}
          <Panel padding={false}>
            {sales.data.rows.length === 0 ? (
              <EmptyState title="За этот период продаж нет" />
            ) : (
              <>
                <ListHeader>
                  <Box sx={{ flex: 1 }}>Блюдо</Box>
                  <Box sx={{ width: 70, textAlign: 'right' }}>Продано</Box>
                  <Box sx={{ width: 120, textAlign: 'right' }}>Выручка</Box>
                  <Box sx={{ width: 120, textAlign: 'right' }}>Себестоимость</Box>
                  <Box sx={{ width: 120, textAlign: 'right' }}>Прибыль</Box>
                  <Box sx={{ width: 90, textAlign: 'right' }}>Фудкост</Box>
                </ListHeader>
                {sales.data.rows.map(r => (
                  <ListRow key={`${r.shawarmaId}-${r.name}`}>
                    <Box sx={{ flex: 1, minWidth: 0 }}>
                      <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
                        <Typography sx={{ fontWeight: 600 }}>{r.name}</Typography>
                        {!r.hasTechCard && <Pill color="#C77700" bg="rgba(232,137,12,0.12)">нет техкарты</Pill>}
                      </Box>
                      <Typography variant="body2" color="text.secondary" sx={{ display: { md: 'none' } }}>
                        {r.quantity} шт · {fmtMoney(r.revenue)}{r.margin !== null ? ` · прибыль ${fmtMoney(r.margin)}` : ''}
                      </Typography>
                    </Box>
                    <Box sx={{ width: 70, textAlign: 'right', display: { xs: 'none', md: 'block' } }}>{r.quantity}</Box>
                    <Box sx={{ width: 120, textAlign: 'right', display: { xs: 'none', md: 'block' }, fontVariantNumeric: 'tabular-nums' }}>{fmtMoney(r.revenue)}</Box>
                    <Box sx={{ width: 120, textAlign: 'right', display: { xs: 'none', md: 'block' }, fontVariantNumeric: 'tabular-nums' }}>{fmtMoney(r.cost)}</Box>
                    <Box sx={{ width: 120, textAlign: 'right', display: { xs: 'none', md: 'block' }, fontVariantNumeric: 'tabular-nums', fontWeight: 600 }}>{fmtMoney(r.margin)}</Box>
                    <Box sx={{ width: { md: 90 }, textAlign: 'right' }}><FoodCost value={r.foodCostPercent} /></Box>
                  </ListRow>
                ))}
              </>
            )}
          </Panel>
          <Typography variant="caption" color="text.secondary">
            Себестоимость считается по текущим техкартам и ценам склада. Отменённые заказы не учитываются.
          </Typography>
        </>
      ) : report === 'consumption' && consumption.data ? (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(3, 1fr)' }, gap: 1.5 }}>
            <StatTile label="Расход по продажам" value={fmtMoney(consumption.data.totals.soldValue)} hint="по техкартам" />
            <StatTile label="Списано" value={fmtMoney(consumption.data.totals.writtenOffValue)} tone={consumption.data.totals.writtenOffValue > 0 ? 'warning' : 'default'} />
            <StatTile
              label="Расхождения инвентаризаций"
              value={fmtMoney(consumption.data.totals.inventoryDiffValue)}
              tone={consumption.data.totals.inventoryDiffValue < 0 ? 'danger' : 'default'}
              hint="минус — недостача"
            />
          </Box>
          <Panel padding={false}>
            {consumption.data.rows.length === 0 ? (
              <EmptyState title="За этот период движений не было" />
            ) : (
              <>
                <ListHeader>
                  <Box sx={{ flex: 1 }}>Позиция</Box>
                  <Box sx={{ width: 100, textAlign: 'right' }}>Приход</Box>
                  <Box sx={{ width: 100, textAlign: 'right' }}>Продажи</Box>
                  <Box sx={{ width: 100, textAlign: 'right' }}>Списано</Box>
                  <Box sx={{ width: 110, textAlign: 'right' }}>Инвентар.</Box>
                  <Box sx={{ width: 100, textAlign: 'right' }}>Остаток</Box>
                </ListHeader>
                {consumption.data.rows.map(r => (
                  <ListRow key={r.stockItemId}>
                    <Box sx={{ flex: 1, minWidth: 0 }}>
                      <Typography sx={{ fontWeight: 600 }}>{r.name}</Typography>
                      <Typography variant="body2" color="text.secondary" sx={{ display: { md: 'none' } }}>
                        +{fmtQty(r.received)} · продажи {fmtQty(r.sold)} · списано {fmtQty(r.writtenOff)}
                        {r.inventoryDiff !== 0 ? ` · инв. ${fmtQty(r.inventoryDiff)}` : ''}
                      </Typography>
                    </Box>
                    {[r.received, r.sold, r.writtenOff].map((v, i) => (
                      <Box key={i} sx={{ width: 100, textAlign: 'right', display: { xs: 'none', md: 'block' }, fontVariantNumeric: 'tabular-nums', color: v ? 'text.primary' : 'text.secondary' }}>
                        {v ? fmtQty(v, r.unit) : '—'}
                      </Box>
                    ))}
                    <Box sx={{ width: 110, textAlign: 'right', display: { xs: 'none', md: 'block' }, fontVariantNumeric: 'tabular-nums', fontWeight: r.inventoryDiff ? 700 : 400, color: r.inventoryDiff < 0 ? tokens.chili : r.inventoryDiff > 0 ? tokens.herb : 'text.secondary' }}>
                      {r.inventoryDiff ? `${r.inventoryDiff > 0 ? '+' : ''}${fmtQty(r.inventoryDiff, r.unit)}` : '—'}
                    </Box>
                    <Box sx={{ width: { md: 100 }, textAlign: 'right', fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>{fmtQty(r.balance, r.unit)}</Box>
                  </ListRow>
                ))}
              </>
            )}
          </Panel>
          <Typography variant="caption" color="text.secondary">
            Сравните «Продажи» (сколько должно было уйти по техкартам) с недостачей по инвентаризации — так видны перерасход и потери.
          </Typography>
        </>
      ) : null}
    </Box>
  );
};

export default ReportsTab;
