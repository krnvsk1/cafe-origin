import * as React from 'react';
import { Drawer, Box, Typography, IconButton, useMediaQuery, useTheme } from '@mui/material';
import { Close as CloseIcon, ChevronRight as ChevronRightIcon } from '@mui/icons-material';
import type { PaymentRow } from '../../api/finance';
import { fmtMoney, fmtDate } from '../../api/inventory';
import { DueLabel } from './DueLabel';
import PaymentCalendar from './PaymentCalendar';
import { dateKey } from '../../utils/period';

/** Выезжающий справа календарь платежей в большом размере; под ним — платежи выбранного дня */
const CalendarDrawer: React.FC<{
  open: boolean;
  onClose: () => void;
  items: PaymentRow[];
  today: string;
  onOpenPayment: (id: number) => void;
}> = ({ open, onClose, items, today, onOpenPayment }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const [day, setDay] = React.useState<string | null>(null);
  const dayRef = React.useRef<HTMLDivElement>(null);
  // Выбрали дату — показываем её платежи, даже если они ниже края панели
  React.useEffect(() => {
    if (day) dayRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }, [day]);
  const dayItems = day ? items.filter(p => p.dueDate && dateKey(p.dueDate) === day) : [];
  const dayTotal = dayItems.reduce((sum, p) => sum + p.total, 0);

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      PaperProps={{ sx: { width: fullScreen ? '100%' : { sm: 640, md: 820 }, bgcolor: 'background.default' } }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', px: { xs: 2, sm: 3 }, pt: { xs: 2, sm: 2.5 }, pb: 1 }}>
        <Typography sx={{ fontWeight: 800, fontSize: '1.5rem', flex: 1 }}>Календарь платежей</Typography>
        <IconButton onClick={onClose} aria-label="Закрыть календарь"><CloseIcon /></IconButton>
      </Box>

      {/* Прокручивается эта область; блоки внутри не сжимаются, иначе список дня обрезается по высоте панели */}
      <Box sx={{ flex: 1, minHeight: 0, px: { xs: 1.5, sm: 3 }, pb: 3, overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 2, '& > *': { flexShrink: 0 } }}>
        <PaymentCalendar items={items} today={today} selected={day} onSelect={setDay} />

        <Box ref={dayRef} sx={{ bgcolor: 'background.paper', borderRadius: '20px', overflow: 'hidden', scrollMarginBottom: 16 }}>
          {!day ? (
            <Typography color="text.secondary" sx={{ px: 2.5, py: 2.5 }}>
              Нажмите на дату с суммой, чтобы увидеть, что оплатить в этот день.
            </Typography>
          ) : (
            <>
              <Box sx={{ display: 'flex', alignItems: 'baseline', px: 2.5, pt: 2, pb: 1 }}>
                <Typography sx={{ fontWeight: 700, flex: 1 }}>К оплате {fmtDate(day)}</Typography>
                <Typography sx={{ fontWeight: 800 }}>{fmtMoney(dayTotal)}</Typography>
              </Box>
              {dayItems.map(p => (
                <Box
                  key={p.id}
                  component="button"
                  type="button"
                  onClick={() => onOpenPayment(p.id)}
                  sx={{
                    display: 'flex', alignItems: 'center', gap: 1.5, width: '100%', px: 2.5, py: 1.25,
                    border: 0, borderTop: '1px solid', borderColor: 'divider', bgcolor: 'transparent',
                    color: 'inherit', font: 'inherit', textAlign: 'left', cursor: 'pointer',
                    '&:hover': { bgcolor: 'action.hover' },
                  }}
                >
                  <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Typography sx={{ fontWeight: 600 }} noWrap>{p.supplierName ?? `ИНН ${p.supplierInn ?? '—'}`}</Typography>
                    <Typography variant="body2" color="text.secondary" noWrap>№ {p.number ?? '—'} от {fmtDate(p.date)}</Typography>
                  </Box>
                  <Box sx={{ textAlign: 'right', display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 0.5 }}>
                    <Typography sx={{ fontWeight: 700, whiteSpace: 'nowrap' }}>{fmtMoney(p.total)}</Typography>
                    <DueLabel dueDate={p.dueDate} daysLeft={p.daysLeft} paid={false} />
                  </Box>
                  <ChevronRightIcon sx={{ color: 'text.secondary' }} />
                </Box>
              ))}
            </>
          )}
        </Box>
      </Box>
    </Drawer>
  );
};

export default CalendarDrawer;
