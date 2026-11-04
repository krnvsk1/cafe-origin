import * as React from 'react';
import {
  Box, Typography, TextField, Alert, CircularProgress, Chip, Paper, Collapse, ButtonBase, Button,
} from '@mui/material';
import { ExpandMore as ExpandIcon } from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import {
  useShiftCurrent, useShifts, useShiftDetails, parseUtc, useUnfinishedReceipts, useRetryReceipt, useReturnCheckFiscal,
  type ShiftRow, type ShiftDetails, type UnfinishedReceipt,
} from '../api/waiter';
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

const fiscalText = (f: ShiftDetails['checks'][number]['fiscal']) =>
  !f ? null : f.status === 'done' ? `ФД ${f.fiscalDocumentNumber ?? '—'}` : f.status === 'processing' ? 'касса не ответила' : 'не пробит';

/** Кассовые чеки, которые не пробились: повторить или узнать у кассы, пробит ли */
const UnfinishedReceipts: React.FC = () => {
  const { data = [] } = useUnfinishedReceipts(true);
  const retry = useRetryReceipt();
  const [msg, setMsg] = React.useState<{ id: number; ok: boolean; text: string } | null>(null);
  if (data.length === 0) return null;
  const run = async (r: UnfinishedReceipt) => {
    setMsg(null);
    try {
      const res = await retry.mutateAsync(r);
      setMsg({ id: r.id, ok: res.status === 'done', text: res.status === 'done' ? `Пробит · ФД ${res.fiscalDocumentNumber ?? '—'}` : res.error ?? 'Касса ещё не ответила' });
    } catch (e) { setMsg({ id: r.id, ok: false, text: apiError(e) }); }
  };
  return (
    <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2, borderColor: 'error.main' }}>
      <Typography sx={{ fontWeight: 800 }}>Непробитые кассовые чеки · {data.length}</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Деньги получены, а чек на кассе не пробит. Пробейте их до закрытия смены (Z-отчёта); если касса не ответила — сначала «Проверить»: второй чек не пробьётся.
      </Typography>
      {data.map(r => (
        <Box key={r.id} sx={{ display: 'flex', gap: 1.5, alignItems: 'center', py: 0.75, borderTop: 1, borderColor: 'divider', flexWrap: 'wrap' }}>
          <Box sx={{ flex: 1, minWidth: 200 }}>
            <Typography sx={{ fontWeight: 700 }}>
              {r.kind === 'sell' ? 'Продажа' : 'Возврат'} · {r.checkId != null ? `стол ${r.table ?? '—'}${r.label ? ` · ${r.label}` : ''}` : `заказ #${r.orderId}`}
              {' · '}{money(r.total)}{r.paymentMethod ? ` · ${r.paymentMethod === 'cash' ? 'наличные' : 'карта'}` : ''}
            </Typography>
            <Typography variant="body2" color={r.status === 'processing' ? 'warning.main' : 'error.main'}>
              {parseUtc(r.createdAt).toLocaleString('ru-RU', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })} · {r.error ?? (r.status === 'processing' ? 'касса не ответила' : 'не пробит')}
            </Typography>
            {msg?.id === r.id && <Typography variant="body2" color={msg.ok ? 'success.main' : 'error.main'}>{msg.text}</Typography>}
          </Box>
          <Button variant="contained" size="small" disabled={retry.isPending} onClick={() => run(r)}>
            {r.status === 'processing' ? 'Проверить' : 'Пробить'}
          </Button>
        </Box>
      ))}
    </Paper>
  );
};

/** Подробности смены: официанты и все чеки */
const ShiftDetailsView: React.FC<{ id: number }> = ({ id }) => {
  const navigate = useNavigate();
  const { data, isLoading, error } = useShiftDetails(id);
  const giveBack = useReturnCheckFiscal();
  const [note, setNote] = React.useState<{ id: number; ok: boolean; text: string } | null>(null);
  const doReturn = async (checkId: number, total: number) => {
    if (!window.confirm(`Пробить чек возврата на ${money(total)}? Деньги гостю возвращаете вы — касса только пробивает чек.`)) return;
    setNote(null);
    try {
      const r = await giveBack.mutateAsync(checkId);
      setNote({ id: checkId, ok: r.status === 'done', text: r.status === 'done' ? `Чек возврата пробит · ФД ${r.fiscalDocumentNumber ?? '—'}` : r.error ?? 'Касса ещё не ответила' });
    } catch (e) { setNote({ id: checkId, ok: false, text: apiError(e) }); }
  };
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
        {data.checks.map(c => {
          const sold = c.fiscal?.status === 'done';
          const returned = c.fiscalReturn?.status === 'done';
          const ft = returned ? `возврат · ФД ${c.fiscalReturn?.fiscalDocumentNumber ?? '—'}` : fiscalText(c.fiscal);
          return (
            <Box key={c.id} sx={{ display: 'flex', alignItems: 'center', gap: 1, borderRadius: '10px', '&:hover': { bgcolor: 'action.hover' } }}>
              <ButtonBase onClick={() => navigate(`/waiter/${c.id}`)}
                sx={{ display: 'grid', gridTemplateColumns: '1fr auto auto', gap: 1, flex: 1, minWidth: 0, textAlign: 'left', alignItems: 'center', py: 0.75, px: 1, borderRadius: '10px' }}>
                <Box sx={{ minWidth: 0 }}>
                  <Typography sx={{ fontWeight: 700 }} noWrap>Стол {c.table}{c.label ? ` · ${c.label}` : ''}</Typography>
                  <Typography variant="body2" color="text.secondary" noWrap>
                    {c.waiter ?? '—'} · {time(c.openedAt)}{c.closedAt ? `–${time(c.closedAt)}` : ''}
                    {c.paymentMethod ? ` · ${c.paymentMethod === 'cash' ? 'наличные' : 'карта'}` : ''}
                    {ft && <Box component="span" sx={{ color: sold || returned ? 'text.secondary' : 'error.main' }}> · чек: {ft}</Box>}
                  </Typography>
                  {note?.id === c.id && <Typography variant="body2" color={note.ok ? 'success.main' : 'error.main'}>{note.text}</Typography>}
                </Box>
                <Chip size="small" color={STATUS[c.status].color} label={STATUS[c.status].text} />
                <Typography sx={{ fontWeight: 700, minWidth: 80, textAlign: 'right' }}>{money(c.total)}</Typography>
              </ButtonBase>
              {sold && !returned && (
                <Button size="small" color="inherit" disabled={giveBack.isPending} onClick={() => doReturn(c.id, c.total)} sx={{ color: 'text.secondary', mr: 0.5 }}>
                  Возврат
                </Button>
              )}
            </Box>
          );
        })}
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
      {(seesAll || can(P.Pos)) && current?.unfiscalized != null && <UnfinishedReceipts />}
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
