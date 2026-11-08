import * as React from 'react';
import {
  Box, Typography, TextField, Alert, CircularProgress, Chip, Paper, Collapse, ButtonBase, Button,
  Dialog, DialogTitle, DialogContent, DialogActions, ToggleButtonGroup, ToggleButton, MenuItem, Snackbar,
} from '@mui/material';
import { ExpandMore as ExpandIcon } from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import {
  useShiftCurrent, useShifts, useShiftDetails, parseUtc, useUnfinishedReceipts, useRetryReceipt, useReturnCheckFiscal, payLabel,
  useCancelCheckAll, useSiteOrders, useCloseSiteOrder, useSiteOrderFiscal, useCancelSiteOrder, useStaffChecksInfo, atolText,
  type ShiftRow, type ShiftDetails, type UnfinishedReceipt, type SiteOrder, type CancelAllResult,
} from '../api/waiter';
import { apiError } from '../api/inventory';
import { todayMsk } from '../api/haccp';
import { useCan, P } from '../auth/permissions';
import { ShiftBar, PushButton } from './WaiterPage';
import DiscountDialog, { discountOf, type DiscountType } from '../components/DiscountDialog';

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
              {r.kind === 'sell' ? 'Продажа' : 'Возврат'} · {r.banquetId != null ? `банкет №${r.banquetId}` : r.checkId != null ? `стол ${r.table ?? '—'}${r.label ? ` · ${r.label}` : ''}` : `заказ #${r.orderId}`}
              {' · '}{money(r.total)}{r.paymentMethod ? ` · ${payLabel(r.paymentMethod)}` : ''}
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
const CANCEL_REASONS = ['Гость передумал', 'Ошибка заказа', 'Долго ждали', 'Нет в наличии', 'Гость не пришёл'];

const cancelText = (r: CancelAllResult, what: string) =>
  (r.returned ? 'Чек возврата пробит. ' : '') +
  (r.cancelled ? `${what} отменён` : r.pending > 0
    ? `Убрано ${r.done}, ждёт кухню ${r.pending} — ${what.toLowerCase()} отменится, когда кухня подтвердит`
    : `Убрано ${r.done}`);

/** Отменить всё: причина; блюда убираются по правилам отмены (начатые — с подтверждением кухни, готовые — со списанием) */
const CancelAllDialog: React.FC<{
  title: string; warning?: string; onClose: () => void;
  onSubmit: (reason: string) => Promise<CancelAllResult>; onDone: (r: CancelAllResult) => void;
}> = ({ title, warning, onClose, onSubmit, onDone }) => {
  const [reason, setReason] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [err, setErr] = React.useState('');
  const submit = async () => {
    setBusy(true); setErr('');
    try { onDone(await onSubmit(reason.trim())); }
    catch (e) { setErr(apiError(e)); }
    finally { setBusy(false); }
  };
  return (
    <Dialog open onClose={() => !busy && onClose()} maxWidth="xs" fullWidth>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        <Typography variant="body2" color="text.secondary">
          Не принятые кухней блюда убираются сразу, принятые и готовящиеся — после подтверждения кухни, готовые или отданные — со списанием продуктов.
        </Typography>
        {warning && <Alert severity="warning">{warning}</Alert>}
        <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap' }}>
          {CANCEL_REASONS.map(r => <Chip key={r} label={r} color={reason === r ? 'primary' : 'default'} onClick={() => setReason(r)} />)}
        </Box>
        <TextField size="small" label="Причина" value={reason} onChange={e => setReason(e.target.value)} slotProps={{ htmlInput: { maxLength: 300 } }} />
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button onClick={onClose} disabled={busy} sx={{ color: 'text.secondary' }}>Назад</Button>
        <Button variant="contained" color="error" onClick={submit} disabled={busy || !reason.trim()}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>Отменить всё</Button>
      </DialogActions>
    </Dialog>
  );
};

const SITE_STATUS_COLOR: Record<string, 'default' | 'success' | 'warning' | 'info'> = {
  'Новый': 'info', 'Принят': 'info', 'Готовится': 'warning', 'Готов': 'success', 'В пути': 'warning', 'Доставлен': 'success', 'Выполнен': 'success', 'Отменён': 'default',
};

/** Закрыть заказ с сайта: способ оплаты → чек в АТОЛ, или на сотрудника без чека */
const CloseSiteDialog: React.FC<{ order: SiteOrder; onClose: () => void; onDone: (text: string) => void }> = ({ order, onClose, onDone }) => {
  const close = useCloseSiteOrder();
  const online = order.paymentMethod === 'online';
  const [method, setMethod] = React.useState<string>(online ? 'online' : order.paymentMethod === 'card' ? 'card' : 'cash');
  const { data: staffInfo } = useStaffChecksInfo(!online);
  const [staffId, setStaffId] = React.useState<number | ''>('');
  const [err, setErr] = React.useState('');
  const canDiscount = useCan()(P.Discounts) && !online;
  const [disc, setDisc] = React.useState<{ type: DiscountType; value: number } | null>(null);
  const [discOpen, setDiscOpen] = React.useState(false);
  const goods = Math.max(0, order.total - (order.deliveryPrice ?? 0));
  const discount = disc ? discountOf(disc.type, disc.value, goods) : 0;
  React.useEffect(() => {
    if (staffId === '' && staffInfo?.me && staffInfo.staff?.some(u => u.id === staffInfo.me)) setStaffId(staffInfo.me);
  }, [staffInfo, staffId]);
  const submit = async () => {
    setErr('');
    try {
      const r = await close.mutateAsync({ id: order.id, paymentMethod: method,
        ...(method === 'staff' ? { staffUserId: staffId === '' ? null : staffId } : {}),
        ...(disc ? { discountType: disc.type, discountValue: disc.value } : {}) });
      onDone(`Заказ #${order.id} закрыт · ${payLabel(method === 'online' ? 'онлайн' : method)}. ${atolText(r.fiscal, r.fiscalSkipped, r.staff)}`);
    } catch (e) { setErr(apiError(e)); }
  };
  return (
    <Dialog open onClose={() => !close.isPending && onClose()} maxWidth="xs" fullWidth>
      <DialogTitle>Закрыть заказ #{order.id} · {money(order.total - discount)}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Typography variant="body2" color="text.secondary">{order.deliveryType} · {order.customerName}{order.phone ? ` · ${order.phone}` : ''}</Typography>
        {canDiscount && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Button size="small" onClick={() => setDiscOpen(true)}>{disc ? 'Изменить скидку' : 'Добавить скидку'}</Button>
            {discount > 0 && <Typography variant="body2" color="text.secondary">{money(order.total)} − {money(discount)}</Typography>}
          </Box>
        )}
        <DiscountDialog open={discOpen} base={goods} type={disc?.type} value={disc?.value} onClose={() => setDiscOpen(false)}
          onSave={(type, value) => setDisc(type && value ? { type, value } : null)} />
        {online ? (
          <Alert severity="info">Оплачен онлайн — способ оплаты не меняется.</Alert>
        ) : (
          <ToggleButtonGroup exclusive fullWidth value={method} onChange={(_, v) => v && setMethod(v)}>
            {staffInfo?.allowed && <ToggleButton value="staff">На сотрудника</ToggleButton>}
            <ToggleButton value="card">Карта</ToggleButton>
            <ToggleButton value="cash">Наличные</ToggleButton>
          </ToggleButtonGroup>
        )}
        {method === 'staff' && staffInfo?.allowed && (
          <>
            <TextField select label="Сотрудник" value={staffId} onChange={e => setStaffId(Number(e.target.value))}>
              {(staffInfo.staff ?? []).map(u => <MenuItem key={u.id} value={u.id}>{u.name}</MenuItem>)}
            </TextField>
          </>
        )}
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button onClick={onClose} disabled={close.isPending} sx={{ color: 'text.secondary' }}>Назад</Button>
        <Button variant="contained" color="secondary" onClick={submit} disabled={close.isPending}
          startIcon={close.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}>
          {method === 'staff' ? 'Закрыть на сотрудника' : 'Закрыть'}
        </Button>
      </DialogActions>
    </Dialog>
  );
};

/** Заказы с сайта (доставка и самовывоз): закрывает менеджер — оплата и чек в АТОЛ; отмена всего заказа */
const SiteOrdersPanel: React.FC = () => {
  const { data, isLoading, error } = useSiteOrders(true);
  const cancel = useCancelSiteOrder();
  const fiscal = useSiteOrderFiscal();
  const [closing, setClosing] = React.useState<SiteOrder | null>(null);
  const [cancelling, setCancelling] = React.useState<SiteOrder | null>(null);
  const [toast, setToast] = React.useState('');
  const [showDone, setShowDone] = React.useState(false);
  if (isLoading) return null;
  if (error) return <Alert severity="error">{apiError(error, 'Не удалось загрузить заказы с сайта')}</Alert>;
  const all = data ?? [];
  const waiting = all.filter(o => !o.closedAt && o.status !== 'Отменён');
  const done = all.filter(o => o.closedAt || o.status === 'Отменён');
  if (all.length === 0) return null;
  const rows = showDone ? [...waiting, ...done] : waiting;
  const retry = async (o: SiteOrder) => {
    try { const f = await fiscal.mutateAsync(o.id); setToast(`Заказ #${o.id}: ${atolText(f)}`); }
    catch (e) { setToast(apiError(e)); }
  };
  return (
    <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.5 }}>
        <Typography sx={{ fontWeight: 800, flex: 1 }}>Заказы с сайта · ждут закрытия: {waiting.length}</Typography>
        {done.length > 0 && <Button size="small" onClick={() => setShowDone(v => !v)}>{showDone ? 'Скрыть закрытые' : `Закрытые сегодня (${done.length})`}</Button>}
      </Box>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Доставку и самовывоз закрывает менеджер или администратор: способ оплаты, чек уходит в АТОЛ.
      </Typography>
      {rows.length === 0 && <Typography color="text.secondary">Все заказы закрыты</Typography>}
      {rows.map(o => {
        const isCancelled = o.status === 'Отменён';
        const fiscalBad = !!o.closedAt && !isCancelled && o.paymentMethod !== 'staff' && o.fiscal && o.fiscal.status !== 'done';
        return (
          <Box key={o.id} sx={{ display: 'flex', gap: 1, alignItems: 'center', py: 0.75, borderTop: 1, borderColor: 'divider', flexWrap: 'wrap', opacity: isCancelled ? 0.6 : 1 }}>
            <Box sx={{ flex: 1, minWidth: 220 }}>
              <Typography sx={{ fontWeight: 700 }}>
                #{o.id} · {o.deliveryType} · {money(o.total)}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {time(o.createdAt)} · {o.customerName}
                {o.paymentMethod === 'online' ? ' · оплачен онлайн' : o.closedAt ? ` · ${payLabel(o.paymentMethod)}` : ''}
                {o.staffName ? ` (${o.staffName})` : ''}
                {o.closedAt ? ` · закрыл ${o.closedBy ?? '—'} в ${time(o.closedAt)}` : ''}
                {o.closedAt && o.paymentMethod !== 'staff' && o.fiscal?.status === 'done' && o.fiscal.kind === 'sell' ? ` · чек ФД ${o.fiscal.fiscalDocumentNumber ?? '—'}` : ''}
              </Typography>
              {o.pendingVoids > 0 && <Typography variant="body2" color="warning.main">Отмена ждёт подтверждения кухни: {o.pendingVoids}</Typography>}
              {fiscalBad && <Typography variant="body2" color="error.main">Чек не пробит: {o.fiscal?.error ?? 'касса не ответила'}</Typography>}
            </Box>
            <Chip size="small" color={SITE_STATUS_COLOR[o.status] ?? 'default'} label={o.status} />
            {fiscalBad && <Button size="small" variant="outlined" onClick={() => retry(o)} disabled={fiscal.isPending}>{o.fiscal?.status === 'processing' ? 'Проверить' : 'Пробить'}</Button>}
            {o.canClose && <Button size="small" variant="contained" color="secondary" onClick={() => setClosing(o)}>Закрыть</Button>}
            {!isCancelled && o.pendingVoids === 0 && <Button size="small" color="error" onClick={() => setCancelling(o)}>Отменить</Button>}
          </Box>
        );
      })}
      {closing && <CloseSiteDialog order={closing} onClose={() => setClosing(null)} onDone={t => { setClosing(null); setToast(t); }} />}
      {cancelling && (
        <CancelAllDialog title={`Отменить заказ #${cancelling.id}`}
          warning={[
            cancelling.closedAt && cancelling.fiscal?.sold ? `Чек уже пробит — сначала уйдёт чек возврата на ${money(cancelling.total)}.` : '',
            cancelling.paymentMethod === 'online' ? 'Онлайн-оплата вернётся гостю через ЮKassa, когда заказ отменится.' : '',
          ].filter(Boolean).join(' ') || undefined}
          onClose={() => setCancelling(null)}
          onSubmit={reason => cancel.mutateAsync({ id: cancelling.id, reason })}
          onDone={r => { setToast(`Заказ #${cancelling.id}: ${cancelText(r, 'Заказ')}`); setCancelling(null); }} />
      )}
      <Snackbar open={!!toast} autoHideDuration={7000} onClose={() => setToast('')} message={toast} anchorOrigin={{ vertical: 'top', horizontal: 'center' }} />
    </Paper>
  );
};

const ShiftDetailsView: React.FC<{ id: number }> = ({ id }) => {
  const navigate = useNavigate();
  const { data, isLoading, error } = useShiftDetails(id);
  const giveBack = useReturnCheckFiscal();
  const [note, setNote] = React.useState<{ id: number; ok: boolean; text: string } | null>(null);
  const cancelCheck = useCancelCheckAll();
  const [cancelling, setCancelling] = React.useState<number | null>(null);
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
      {cancelling != null && (
        <CancelAllDialog title="Отменить весь счёт"
          onClose={() => setCancelling(null)}
          onSubmit={reason => cancelCheck.mutateAsync({ checkId: cancelling, reason })}
          onDone={r => { setCancelling(null); setNote({ id: cancelling, ok: true, text: cancelText(r, 'Счёт') }); }} />
      )}
      {data.waiters.length > 0 && (
        <Box>
          <Typography variant="overline" color="text.secondary">Официанты</Typography>
          {data.waiters.map(w => (
            <Box key={w.id} sx={{ display: 'flex', gap: 1, alignItems: 'baseline', py: 0.25 }}>
              <Typography sx={{ fontWeight: 700, flex: 1 }}>{w.name ?? `Сотрудник #${w.userId}`}</Typography>
              <Typography variant="body2" color="text.secondary">{time(w.openedAt)}–{w.closedAt ? time(w.closedAt) : 'сейчас'}{w.approvedBy ? ` · подтв. ${w.approvedBy}` : ''}</Typography>
              <Typography variant="body2" color="text.secondary">{w.checks} сч.</Typography>
              <Typography variant="body2" sx={{ fontWeight: 700, minWidth: 80, textAlign: 'right' }}>{money(w.revenue)}</Typography>
            </Box>
          ))}
        </Box>
      )}
      {data.staff && data.staff.length > 0 && (
        <Box>
          <Typography variant="overline" color="text.secondary">На сотрудников · к удержанию (не выручка, без чека)</Typography>
          {data.staff.map(st => (
            <Box key={st.userId} sx={{ display: 'flex', gap: 1, alignItems: 'baseline', py: 0.25 }}>
              <Typography sx={{ fontWeight: 700, flex: 1 }}>{st.name}</Typography>
              <Typography variant="body2" color="text.secondary">{st.checks} сч.</Typography>
              <Typography variant="body2" sx={{ fontWeight: 700, minWidth: 80, textAlign: 'right' }}>{money(st.total)}</Typography>
            </Box>
          ))}
        </Box>
      )}
      <Box>
        <Typography variant="overline" color="text.secondary">Счета</Typography>
        {data.checks.length === 0 && <Typography color="text.secondary">Счетов в эту смену не было</Typography>}
        {data.checks.map(c => {
          const sold = c.fiscal?.status === 'done';
          const returned = c.fiscalReturn?.status === 'done';
          const ft = returned ? `возврат · ФД ${c.fiscalReturn?.fiscalDocumentNumber ?? '—'}` : fiscalText(c.fiscal);
          return (
            <Box key={c.id} sx={{ display: 'flex', alignItems: 'center', gap: 1, borderRadius: '10px', '&:hover': { bgcolor: 'action.hover' } }}>
              <ButtonBase onClick={() => navigate(`/waiter/${c.id}`, { state: { from: '/admin/shifts' } })}
                sx={{ display: 'grid', gridTemplateColumns: '1fr auto auto', gap: 1, flex: 1, minWidth: 0, textAlign: 'left', alignItems: 'center', py: 0.75, px: 1, borderRadius: '10px' }}>
                <Box sx={{ minWidth: 0 }}>
                  <Typography sx={{ fontWeight: 700 }} noWrap>Стол {c.table}{c.label ? ` · ${c.label}` : ''}</Typography>
                  <Typography variant="body2" color="text.secondary" noWrap>
                    {c.waiter ?? '—'} · {time(c.openedAt)}{c.closedAt ? `–${time(c.closedAt)}` : ''}
                    {c.paymentMethod === 'staff'
                      ? ` · на сотрудника: ${c.staffName ?? '—'}${c.staffReason ? ` (${c.staffReason})` : ''} · в АТОЛ не отправлялся`
                      : c.paymentMethod ? ` · ${payLabel(c.paymentMethod)}` : ''}
                    {c.paymentWas && <Box component="span" sx={{ color: 'warning.main' }}> · исправлено с «{payLabel(c.paymentWas)}»{c.paymentChangedBy ? `, ${c.paymentChangedBy}` : ''}{c.paymentChangedAt ? ` ${time(c.paymentChangedAt)}` : ''}</Box>}
                    {ft && <Box component="span" sx={{ color: sold || returned ? 'text.secondary' : 'error.main' }}> · кассовый чек: {ft}</Box>}
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
              {c.status === 'open' && (
                <Button size="small" color="error" onClick={() => setCancelling(c.id)} sx={{ mr: 0.5 }}>Отменить</Button>
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
          {s.checks} сч. · нал {money(s.cash)} · карта {money(s.card)}{s.staff ? ` · на сотрудников ${money(s.staff)}` : ''}{s.openChecks ? ` · открыто ${s.openChecks}` : ''}
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
  // Раскрытая смена запоминается, чтобы после возврата из счёта она осталась открытой
  const [openId, setOpenIdState] = React.useState<number | null>(() => {
    try { const v = sessionStorage.getItem('shifts-open'); return v ? Number(v) : null; } catch { return null; }
  });
  const setOpenId = (id: number | null) => {
    setOpenIdState(id);
    try { if (id == null) sessionStorage.removeItem('shifts-open'); else sessionStorage.setItem('shifts-open', String(id)); } catch { /* ignore */ }
  };
  const { data: current } = useShiftCurrent();
  const { data: shifts, isLoading, error } = useShifts(from, to);

  return (
    <Box sx={{ maxWidth: 1000, mx: 'auto', p: { xs: 2, md: 3 }, display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <Typography variant="h4" sx={{ fontWeight: 800, flex: 1 }}>Смены и счета</Typography>
        {current?.canApprove && (
          <PushButton title="Уведомления о запросах на смену"
            text="Официант нажал «Начать мою смену» — на это устройство придёт уведомление, даже если сайт закрыт. Нажмите на него, чтобы подтвердить смену."
            hint="Включите уведомления — узнаете, когда официант просит открыть смену" />
        )}
      </Box>

      {current && <ShiftBar shift={current} hideMine />}
      {(seesAll || can(P.Pos)) && current?.unfiscalized != null && <UnfinishedReceipts />}
      {seesAll && <SiteOrdersPanel />}
      {current?.venue && current.waiters && (
        <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2 }}>
          <Typography sx={{ fontWeight: 700, mb: 0.5 }}>
            Сейчас на смене {current.waiters.length ? '' : '— никого'}
          </Typography>
          {current.waiters.map(w => (
            <Typography key={w.id} variant="body2">
              {w.name ?? `Сотрудник #${w.userId}`} · с {time(w.openedAt)}{w.approvedBy ? ` · подтвердил(а) ${w.approvedBy}` : ''}{w.openChecks ? ` · открытых счетов: ${w.openChecks}` : ''}
            </Typography>
          ))}
        </Paper>
      )}

      {seesAll && (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: '1fr 180px 180px' }, gap: 1.5, alignItems: 'center' }}>
            <Typography sx={{ fontWeight: 700, gridColumn: { xs: '1 / -1', sm: 'auto' } }}>История смен</Typography>
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
