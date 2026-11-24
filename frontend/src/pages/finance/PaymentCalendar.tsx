import * as React from 'react';
import { Box, Typography, IconButton, Button, alpha, useTheme } from '@mui/material';
import { ChevronLeft as PrevIcon, ChevronRight as NextIcon } from '@mui/icons-material';
import type { PaymentRow } from '../../api/finance';
import { fmtMoney } from '../../api/inventory';
import { tokens } from '../../theme';
import { plural } from '../../utils/plural';
import { dateKey } from '../../utils/period';

const DOC_FORMS: [string, string, string] = ['накладная', 'накладные', 'накладных'];

const WEEKDAYS = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'];
const WARNING_COLOR = '#C77700';
// Срок в ближайшие столько дней подсвечиваем как «скоро»
const SOON_DAYS = 7;

// В клетке мало места — суммы коротко: «20,2 тыс.»
const compact = new Intl.NumberFormat('ru-RU', { notation: 'compact', maximumFractionDigits: 1 });
const monthTitle = new Intl.DateTimeFormat('ru-RU', { month: 'long', year: 'numeric' });

const keyOf = (year: number, month: number, day: number) =>
  `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;

const addDays = (key: string, days: number) => {
  const [y, m, d] = key.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d + days));
  return keyOf(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate());
};

/** Календарь платежей: сумма к оплате в каждую дату; нажатие на дату фильтрует список */
const PaymentCalendar: React.FC<{
  items: PaymentRow[];
  today: string;
  selected: string | null;
  onSelect: (key: string | null) => void;
}> = ({ items, today, selected, onSelect }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const todayKey = dateKey(today);
  const [month, setMonth] = React.useState(() => ({ year: Number(todayKey.slice(0, 4)), month: Number(todayKey.slice(5, 7)) - 1 }));

  // Сумма неоплаченного по датам срока
  const byDay = React.useMemo(() => {
    const map = new Map<string, { total: number; count: number }>();
    items.forEach(p => {
      if (!p.dueDate || p.paidAt) return;
      const key = dateKey(p.dueDate);
      const cur = map.get(key) ?? { total: 0, count: 0 };
      map.set(key, { total: cur.total + p.total, count: cur.count + 1 });
    });
    return map;
  }, [items]);

  const first = new Date(Date.UTC(month.year, month.month, 1));
  const daysInMonth = new Date(Date.UTC(month.year, month.month + 1, 0)).getUTCDate();
  const offset = (first.getUTCDay() + 6) % 7; // понедельник — первый день недели
  const cells: (number | null)[] = [...Array(offset).fill(null), ...Array.from({ length: daysInMonth }, (_, i) => i + 1)];
  const soonKey = addDays(todayKey, SOON_DAYS);

  const monthKeyPrefix = keyOf(month.year, month.month, 1).slice(0, 7);
  const monthTotal = [...byDay.entries()].filter(([k]) => k.startsWith(monthKeyPrefix)).reduce((s, [, v]) => s + v.total, 0);

  const shift = (delta: number) => setMonth(m => {
    const d = new Date(Date.UTC(m.year, m.month + delta, 1));
    return { year: d.getUTCFullYear(), month: d.getUTCMonth() };
  });

  return (
    <Box sx={{ bgcolor: 'background.paper', borderRadius: '20px', p: { xs: 1.5, sm: 2 } }}>
      <Box sx={{ display: 'flex', alignItems: 'center', mb: 1 }}>
        <IconButton size="small" onClick={() => shift(-1)} aria-label="Предыдущий месяц"><PrevIcon /></IconButton>
        <Typography sx={{ flex: 1, textAlign: 'center', fontWeight: 800, fontSize: { xs: '1rem', sm: '1.15rem' }, textTransform: 'capitalize' }}>
          {monthTitle.format(first).replace(' г.', '')}
        </Typography>
        <IconButton size="small" onClick={() => shift(1)} aria-label="Следующий месяц"><NextIcon /></IconButton>
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(7, minmax(0, 1fr))', gap: { xs: 0.5, sm: 0.75 } }}>
        {WEEKDAYS.map(w => (
          <Typography key={w} variant="caption" color="text.secondary" sx={{ textAlign: 'center', py: 0.25 }}>{w}</Typography>
        ))}
        {cells.map((day, i) => {
          if (day === null) return <Box key={`empty-${i}`} />;
          const key = keyOf(month.year, month.month, day);
          const sum = byDay.get(key);
          const isToday = key === todayKey;
          const isSelected = key === selected;
          const overdue = !!sum && key < todayKey;
          const soon = !!sum && !overdue && key <= soonKey;
          const accent = overdue ? tokens.chili : soon ? WARNING_COLOR : theme.palette.text.primary;
          return (
            <Box
              key={key}
              component="button"
              type="button"
              disabled={!sum}
              onClick={() => onSelect(isSelected ? null : key)}
              aria-pressed={isSelected}
              aria-label={sum ? `${day}: к оплате ${fmtMoney(sum.total)}` : String(day)}
              sx={{
                minHeight: { xs: 58, sm: 88 },
                p: { xs: 0.5, sm: 0.75 },
                border: '1.5px solid',
                borderColor: isSelected ? 'text.primary' : isToday ? alpha(theme.palette.text.primary, 0.5) : 'transparent',
                borderRadius: '12px',
                bgcolor: isSelected ? alpha(accent, 0.16) : sum ? alpha(accent, overdue || soon ? 0.1 : 0.06) : 'transparent',
                color: 'text.primary',
                font: 'inherit',
                cursor: sum ? 'pointer' : 'default',
                display: 'flex',
                flexDirection: 'column',
                alignItems: { xs: 'center', sm: 'flex-start' },
                justifyContent: 'flex-start',
                gap: 0.25,
                textAlign: 'left',
                '&:hover': sum ? { bgcolor: alpha(accent, 0.18) } : undefined,
                '&:disabled': { color: 'text.secondary' },
              }}
            >
              <Typography sx={{ fontSize: { xs: '0.8rem', sm: '0.95rem' }, fontWeight: isToday ? 800 : 500, lineHeight: 1.2 }}>{day}</Typography>
              {sum && (
                <>
                  {/* На телефоне — коротко («20,2 тыс.»), на широком экране — полная сумма */}
                  <Typography sx={{ display: { xs: 'block', sm: 'none' }, fontSize: '0.68rem', fontWeight: 700, color: accent, lineHeight: 1.1, whiteSpace: 'nowrap' }}>
                    {compact.format(sum.total)}
                  </Typography>
                  <Typography sx={{ display: { xs: 'none', sm: 'block' }, fontSize: '0.82rem', fontWeight: 800, color: accent, lineHeight: 1.15, whiteSpace: 'nowrap' }}>
                    {fmtMoney(sum.total)}
                  </Typography>
                  <Typography sx={{ display: { xs: 'none', sm: 'block' }, fontSize: '0.7rem', color: 'text.secondary', lineHeight: 1.1 }}>
                    {sum.count} {plural(sum.count, DOC_FORMS)}
                  </Typography>
                </>
              )}
            </Box>
          );
        })}
      </Box>

      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 1.5, pt: 1.25, borderTop: '1px solid', borderColor: 'divider' }}>
        <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }}>
          За месяц: <b style={{ color: theme.palette.text.primary }}>{fmtMoney(monthTotal)}</b>
        </Typography>
        {(month.year !== Number(todayKey.slice(0, 4)) || month.month !== Number(todayKey.slice(5, 7)) - 1) && (
          <Button size="small" onClick={() => setMonth({ year: Number(todayKey.slice(0, 4)), month: Number(todayKey.slice(5, 7)) - 1 })} sx={{ bgcolor: t.surfaceMuted }}>
            Сегодня
          </Button>
        )}
      </Box>
    </Box>
  );
};

export default PaymentCalendar;
