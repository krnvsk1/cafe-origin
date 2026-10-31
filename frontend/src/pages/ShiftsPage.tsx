import * as React from 'react';
import {
  Box, Typography, TextField, Alert, CircularProgress, Chip, Paper, Collapse, ButtonBase,
} from '@mui/material';
import { ExpandMore as ExpandIcon } from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import { useShiftCurrent, useShifts, useShiftDetails, parseUtc, type ShiftRow } from '../api/waiter';
import { apiError } from '../api/inventory';
import { todayMsk } from '../api/haccp';
import { useCan, P } from '../auth/permissions';
import { ShiftBar } from './WaiterPage';

const money = (v: number) => `${Math.round(v).toLocaleString('ru-RU')} ₽`;
const time = (iso: string) => parseUtc(iso).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
const day = (iso: string) => new Date(iso.slice(0, 10) + 'T12:00:00').toLocaleDateString('ru-RU', { weekday: 'short', day: 'numeric', month: 'long' });
const addDays = (d: string, n: number) => {
  const x = new Date(d + 'T12:00:00Z');
  x.setUTCDate(x.getUTCDate() + n);
  return x.toISOString().slice(0, 10);
};

const STATUS: Record<string, { text: string; color: 'default' | 'success' | 'warning' }> = {
  open: { text: 'открыт', color: 'warning' },
  closed: { text: 'оплачен', color: 'success' },
  cancelled: { text: 'отменён', color: 'default' },
};

/** Подробности смены: официанты и все чеки */
const ShiftDetailsView: React.FC<{ id: number }> = ({ id }) => {
  const navigate = useNavigate();
  const { data, isLoading, error } = useShiftDetails(id);
  if (isLoading) return <Box sx={{ display: 'grid', placeItems: 'center', py: 3 }}><CircularProgress size={28} /></Box>;
  if (error || !data) return <Alert severity="error">{apiError(error, 'Не удалось загрузить смену')}</Alert>;
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, pt: 1 }}>
      {data.waiters.length > 0 && (
        <Box>
          <Typography variant="overline" color="text.secondary">Официанты</Typography>
          {data.waiters.map(w => (
            <Box key={w.id} sx={{ display: 'flex', gap: 1, alignItems: 'baseline', py: 0.25 }}>
              <Typography sx={{ fontWeight: 700, flex: 1 }}>{w.name ?? `Сотрудник #${w.userId}`}</Typography>
              <Typography variant="body2" color="text.secondary">{time(w.openedAt)}–{w.closedAt ? time(w.closedAt) : 'сейчас'}</Typography>
              <Typography variant="body2" color="text.secondary">{w.checks} чек.</Typography>
              <Typography variant="body2" sx={{ fontWeight: 700, minWidth: 80, textAlign: 'right' }}>{money(w.revenue)}</Typography>
            </Box>
          ))}
        </Box>
      )}
      <Box>
        <Typography variant="overline" color="text.secondary">Чеки</Typography>
        {data.checks.length === 0 && <Typography color="text.secondary">Чеков в эту смену не было</Typography>}
        {data.checks.map(c => (
          <ButtonBase key={c.id} onClick={() => navigate(`/waiter/${c.id}`)}
            sx={{ display: 'grid', gridTemplateColumns: '1fr auto auto', gap: 1, width: '100%', textAlign: 'left', alignItems: 'center',
              py: 0.75, px: 1, borderRadius: '10px', '&:hover': { bgcolor: 'action.hover' } }}>
            <Box sx={{ minWidth: 0 }}>
              <Typography sx={{ fontWeight: 700 }} noWrap>Стол {c.table}{c.label ? ` · ${c.label}` : ''}</Typography>
              <Typography variant="body2" color="text.secondary" noWrap>
                {c.waiter ?? '—'} · {time(c.openedAt)}{c.closedAt ? `–${time(c.closedAt)}` : ''}
                {c.paymentMethod ? ` · ${c.paymentMethod === 'cash' ? 'наличные' : 'карта'}` : ''}
              </Typography>
            </Box>
            <Chip size="small" color={STATUS[c.status].color} label={STATUS[c.status].text} />
            <Typography sx={{ fontWeight: 700, minWidth: 80, textAlign: 'right' }}>{money(c.total)}</Typography>
          </ButtonBase>
        ))}
      </Box>
    </Box>
  );
};

const ShiftCard: React.FC<{ s: ShiftRow; open: boolean; onToggle: () => void }> = ({ s, open, onToggle }) => (
  <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2 }}>
    <ButtonBase onClick={onToggle} sx={{ width: '100%', textAlign: 'left', display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
      <Box sx={{ flex: 1, minWidth: 180 }}>
        <Typography sx={{ fontWeight: 800 }}>{day(s.businessDate)}</Typography>
        <Typography variant="body2" color="text.secondary">
          {time(s.openedAt)}–{s.closedAt ? time(s.closedAt) : 'открыта'} · открыл {s.openedBy ?? '—'}{s.closedBy ? `, закрыл ${s.closedBy}` : ''}
        </Typography>
      </Box>
      {!s.closedAt && <Chip size="small" color="warning" label="идёт" />}
      <Box sx={{ textAlign: 'right' }}>
        <Typography sx={{ fontWeight: 800 }}>{money(s.revenue)}</Typography>
        <Typography variant="body2" color="text.secondary">
          {s.checks} чек. · нал {money(s.cash)} · карта {money(s.card)}{s.openChecks ? ` · открыто ${s.openChecks}` : ''}
        </Typography>
      </Box>
      <ExpandIcon sx={{ transform: open ? 'rotate(180deg)' : 'none', transition: 'transform .2s' }} />
    </ButtonBase>
    <Collapse in={open} unmountOnExit><ShiftDetailsView id={s.id} /></Collapse>
  </Paper>
);

/** Смены заведения: открыть/закрыть сегодняшнюю, история смен со всеми чеками */
const ShiftsPage: React.FC = () => {
  const can = useCan();
  const seesAll = can(P.ChecksAll);
  const today = todayMsk();
  const [from, setFrom] = React.useState(addDays(today, -13));
  const [to, setTo] = React.useState(today);
  const [openId, setOpenId] = React.useState<number | null>(null);
  const { data: current } = useShiftCurrent();
  const { data: shifts, isLoading, error } = useShifts(from, to);

  return (
    <Box sx={{ maxWidth: 1000, mx: 'auto', p: { xs: 2, md: 3 }, display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Typography variant="h4" sx={{ fontWeight: 800 }}>Смены и чеки</Typography>

      {current && <ShiftBar shift={current} hideMine />}
      {current?.venue && current.waiters && (
        <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2 }}>
          <Typography sx={{ fontWeight: 700, mb: 0.5 }}>
            Сейчас на смене {current.waiters.length ? '' : '— никого'}
          </Typography>
          {current.waiters.map(w => (
            <Typography key={w.id} variant="body2">
              {w.name ?? `Сотрудник #${w.userId}`} · с {time(w.openedAt)}{w.openChecks ? ` · открытых счетов: ${w.openChecks}` : ''}
            </Typography>
          ))}
        </Paper>
      )}

      {seesAll && (
        <>
          <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
            <Typography sx={{ fontWeight: 700, flex: 1 }}>История смен</Typography>
            <TextField type="date" size="small" label="С" value={from} onChange={e => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
            <TextField type="date" size="small" label="По" value={to} onChange={e => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
          </Box>
          {error && <Alert severity="error">{apiError(error, 'Не удалось загрузить смены')}</Alert>}
          {isLoading && <Box sx={{ display: 'grid', placeItems: 'center', py: 4 }}><CircularProgress /></Box>}
          {shifts?.length === 0 && <Typography color="text.secondary" sx={{ textAlign: 'center', py: 4 }}>За эти дни смен не было</Typography>}
          {shifts?.map(s => <ShiftCard key={s.id} s={s} open={openId === s.id} onToggle={() => setOpenId(openId === s.id ? null : s.id)} />)}
        </>
      )}
    </Box>
  );
};

export default ShiftsPage;
