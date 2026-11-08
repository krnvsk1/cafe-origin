import * as React from 'react';
import {
  Box, Typography, Paper, Button, TextField, Alert, CircularProgress, Chip, IconButton, Dialog, DialogTitle, DialogContent,
  DialogActions, ToggleButtonGroup, ToggleButton, InputAdornment, Snackbar, ButtonBase, Divider,
} from '@mui/material';
import {
  Add as AddIcon, Remove as RemoveIcon, ArrowBack as BackIcon, DeleteOutline as DeleteIcon, PrintOutlined as PrintIcon,
  Edit as EditIcon, RestaurantMenu as KitchenIcon,
} from '@mui/icons-material';
import { useNavigate, useParams } from 'react-router-dom';
import {
  useBanquets, useBanquet, useCreateBanquet, useUpdateBanquet, useBanquetDiscount, useAddBanquetItems, useSetBanquetItemQty,
  useRemoveBanquetItem, useSendBanquetToKitchen, usePrintBanquet, usePrepayBanquet, useRefundBanquetPayment, useCloseBanquet,
  useCancelBanquet, useRetryBanquetReceipt, type Banquet, type BanquetInput,
} from '../api/banquets';
import { parseUtc, fetchPrintJob, atolText, payLabel } from '../api/waiter';
import { apiError } from '../api/inventory';
import { todayMsk } from '../api/haccp';
import { MenuPicker, type DraftLine } from './WaiterPage';
import DiscountDialog from '../components/DiscountDialog';

const money = (v: number) => `${v.toLocaleString('ru-RU', { minimumFractionDigits: 0, maximumFractionDigits: 2 })} ₽`;
const dt = (iso: string) => parseUtc(iso).toLocaleString('ru-RU', { weekday: 'short', day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit' });
const addDays = (d: string, n: number) => {
  const x = new Date(d + 'T12:00:00Z');
  x.setUTCDate(x.getUTCDate() + n);
  return x.toISOString().slice(0, 10);
};
/** ISO (UTC) → значение поля datetime-local в местном времени */
const toLocalInput = (iso: string | null | undefined) => {
  if (!iso) return '';
  const d = parseUtc(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
};
const fromLocalInput = (v: string) => (v ? new Date(v).toISOString() : null);

const STATUS: Record<string, { text: string; color: 'warning' | 'success' | 'default' }> = {
  open: { text: 'открыт', color: 'warning' },
  closed: { text: 'закрыт', color: 'success' },
  cancelled: { text: 'отменён', color: 'default' },
};

const KITCHEN: Record<string, string> = {
  'Новый': 'отправлено на кухню', 'Принят': 'кухня приняла', 'Готовится': 'готовится', 'Готов': 'готово', 'Выполнен': 'отдано', 'Отменён': 'отменено',
};

// ==================== Форма банкета ====================

const BanquetForm: React.FC<{ banquet?: Banquet | null; onClose: () => void; onSaved: (id: number) => void }> = ({ banquet, onClose, onSaved }) => {
  const create = useCreateBanquet();
  const update = useUpdateBanquet();
  const [f, setF] = React.useState(() => ({
    customerName: banquet?.customerName ?? '',
    phone: banquet?.phone ?? '',
    eventAt: toLocalInput(banquet?.eventAt),
    guests: banquet?.guests ? String(banquet.guests) : '',
    comment: banquet?.comment ?? '',
    kitchenAt: toLocalInput(banquet?.kitchenAt),
    serviceType: (banquet?.serviceType ?? 'percent') as 'percent' | 'amount',
    serviceValue: banquet?.serviceValue ? String(banquet.serviceValue) : '',
  }));
  const [err, setErr] = React.useState('');
  const set = (k: keyof typeof f, v: string) => setF(x => ({ ...x, [k]: v }));
  const busy = create.isPending || update.isPending;
  const submit = async () => {
    setErr('');
    const sv = Number(f.serviceValue.replace(',', '.')) || 0;
    const input: BanquetInput = {
      customerName: f.customerName.trim(),
      phone: f.phone.trim() || null,
      eventAt: fromLocalInput(f.eventAt) ?? '',
      guests: f.guests ? Number(f.guests) : null,
      tableId: banquet?.tableId ?? null,
      comment: f.comment.trim() || null,
      kitchenAt: fromLocalInput(f.kitchenAt),
      serviceType: sv > 0 ? f.serviceType : null,
      serviceValue: sv > 0 ? sv : null,
    };
    if (!input.customerName) { setErr('Укажите, на кого банкет'); return; }
    if (!input.eventAt) { setErr('Укажите дату и время банкета'); return; }
    try {
      if (banquet) { await update.mutateAsync({ id: banquet.id, ...input }); onSaved(banquet.id); }
      else { const r = await create.mutateAsync(input); onSaved(r.id); }
    } catch (e) { setErr(apiError(e)); }
  };
  return (
    <Dialog open onClose={() => !busy && onClose()} maxWidth="sm" fullWidth>
      <DialogTitle>{banquet ? `Банкет №${banquet.id}` : 'Новый банкет'}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.75, pt: '8px !important' }}>
        <TextField label="На кого (имя, компания)" value={f.customerName} onChange={e => set('customerName', e.target.value)} autoFocus />
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 1.5 }}>
          <TextField label="Телефон" value={f.phone} onChange={e => set('phone', e.target.value)} slotProps={{ htmlInput: { inputMode: 'decimal' } }} />
          <TextField label="Гостей" value={f.guests} onChange={e => set('guests', e.target.value.replace(/\D/g, ''))} slotProps={{ htmlInput: { inputMode: 'decimal' } }} />
          <TextField label="Дата и время банкета" type="datetime-local" value={f.eventAt} onChange={e => set('eventAt', e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
          <TextField label="Отправить на кухню" type="datetime-local" value={f.kitchenAt} onChange={e => set('kitchenAt', e.target.value)}
            slotProps={{ inputLabel: { shrink: true } }}
            helperText="Пусто — блюда на кухню не уходят. В это время уйдут сами" />
        </Box>
        <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <ToggleButtonGroup size="small" exclusive value={f.serviceType} onChange={(_, v) => v && set('serviceType', v)} sx={{ mt: 0.5 }}>
            <ToggleButton value="percent">% от суммы</ToggleButton>
            <ToggleButton value="amount">Суммой</ToggleButton>
          </ToggleButtonGroup>
          <TextField size="small" label="Обслуживание" value={f.serviceValue} onChange={e => set('serviceValue', e.target.value)}
            placeholder="0 — без обслуживания" sx={{ flex: 1, minWidth: 160 }}
            slotProps={{ htmlInput: { inputMode: 'decimal' }, input: { endAdornment: <InputAdornment position="end">{f.serviceType === 'percent' ? '%' : '₽'}</InputAdornment> } }} />
        </Box>
        <TextField label="Комментарий (зал, пожелания)" value={f.comment} onChange={e => set('comment', e.target.value)} multiline minRows={2} />
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button onClick={onClose} disabled={busy} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={submit} disabled={busy}>{banquet ? 'Сохранить' : 'Создать'}</Button>
      </DialogActions>
    </Dialog>
  );
};

// ==================== Карточка банкета ====================

const PrepayDialog: React.FC<{ banquet: Banquet; onClose: () => void; onDone: (t: string) => void }> = ({ banquet, onClose, onDone }) => {
  const prepay = usePrepayBanquet();
  const [amount, setAmount] = React.useState('');
  const [method, setMethod] = React.useState<'cash' | 'card'>('card');
  const [err, setErr] = React.useState('');
  const num = Number(amount.replace(',', '.')) || 0;
  const submit = async () => {
    setErr('');
    try {
      const r = await prepay.mutateAsync({ id: banquet.id, amount: num, method });
      onDone(r.fiscal ? `Предоплата ${money(num)} · ${payLabel(method)}. ${atolText(r.fiscal, r.fiscalSkipped)}` : `Предоплата ${money(num)} · ${payLabel(method)} записана`);
    } catch (e) { setErr(apiError(e)); }
  };
  return (
    <Dialog open onClose={() => !prepay.isPending && onClose()} maxWidth="xs" fullWidth>
      <DialogTitle>Предоплата</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
        <TextField autoFocus label="Сумма" value={amount} onChange={e => setAmount(e.target.value)}
          helperText={banquet.totals.due > 0 ? `Осталось оплатить ${money(banquet.totals.due)}` : undefined}
          slotProps={{ htmlInput: { inputMode: 'decimal' }, input: { endAdornment: <InputAdornment position="end">₽</InputAdornment> } }} />
        <ToggleButtonGroup exclusive fullWidth value={method} onChange={(_, v) => v && setMethod(v)}>
          <ToggleButton value="card">Карта</ToggleButton>
          <ToggleButton value="cash">Наличные</ToggleButton>
        </ToggleButtonGroup>
        <Typography variant="caption" color="text.secondary">
          Предоплата только записывается, чек не печатается. При закрытии банкета вся сумма (предоплата и остаток) уйдёт в АТОЛ одним чеком.
        </Typography>
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button onClick={onClose} disabled={prepay.isPending} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={submit} disabled={prepay.isPending || num <= 0}
          startIcon={prepay.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}>Внести</Button>
      </DialogActions>
    </Dialog>
  );
};

const CloseDialog: React.FC<{ banquet: Banquet; onClose: () => void; onDone: (t: string) => void }> = ({ banquet, onClose, onDone }) => {
  const close = useCloseBanquet();
  const t = banquet.totals;
  const [method, setMethod] = React.useState<'cash' | 'card' | 'staff'>('card');
  const [err, setErr] = React.useState('');
  const submit = async () => {
    setErr('');
    try {
      const r = await close.mutateAsync({ id: banquet.id, method });
      onDone(`Банкет закрыт · ${money(r.total)}. ${atolText(r.fiscal, r.fiscalSkipped, r.staff)}`);
    } catch (e) { setErr(apiError(e)); }
  };
  return (
    <Dialog open onClose={() => !close.isPending && onClose()} maxWidth="xs" fullWidth>
      <DialogTitle>Закрыть банкет №{banquet.id}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, pt: '8px !important' }}>
        <Box>
          <Row label="Итого" value={money(t.total)} bold />
          {t.prepaid > 0 && <Row label="Предоплата" value={`−${money(t.prepaid)}`} />}
          <Row label="К оплате" value={money(t.due)} bold big />
        </Box>
        {t.due > 0 && (
          <ToggleButtonGroup exclusive fullWidth value={method} onChange={(_, v) => v && setMethod(v)}>
            <ToggleButton value="card">Карта</ToggleButton>
            {banquet.staffAllowed && <ToggleButton value="staff">На заведение</ToggleButton>}
            <ToggleButton value="cash">Наличные</ToggleButton>
          </ToggleButtonGroup>
        )}
        {t.due > 0 && method === 'staff' && (
          <>
            <Alert severity="info">Остаток без оплаты — за счёт заведения, в выручку не входит и в чек не попадает.</Alert>
          </>
        )}
        {banquet.fiscalEnabled && (
          <Typography variant="caption" color="text.secondary">
            В АТОЛ уйдёт один чек на {money(t.prepaid + (method === 'staff' ? 0 : t.due))}: блюда{t.service > 0 ? ' и обслуживание' : ''}{t.prepaid > 0 ? ', оплата — предоплата и остаток, теми способами, которыми платили' : ''}.
          </Typography>
        )}
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button onClick={onClose} disabled={close.isPending} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={submit} disabled={close.isPending}
          startIcon={close.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}>
          {t.due <= 0 ? 'Закрыть' : method === 'staff' ? 'Закрыть на заведение' : 'Оплачено'}
        </Button>
      </DialogActions>
    </Dialog>
  );
};

const CancelDialog: React.FC<{ banquet: Banquet; onClose: () => void; onDone: (t: string) => void }> = ({ banquet, onClose, onDone }) => {
  const cancel = useCancelBanquet();
  const [reason, setReason] = React.useState('');
  const [err, setErr] = React.useState('');
  const prepaid = banquet.payments.filter(p => p.kind === 'prepay' && !p.refundedAt);
  const submit = async () => {
    setErr('');
    try {
      const r = await cancel.mutateAsync({ id: banquet.id, reason: reason.trim() });
      onDone(`Банкет отменён${r.returned ? ` · чеков возврата: ${r.returned}` : ''}${r.pending ? ` · ждёт подтверждения кухни: ${r.pending}` : ''}`);
    } catch (e) { setErr(apiError(e)); }
  };
  return (
    <Dialog open onClose={() => !cancel.isPending && onClose()} maxWidth="xs" fullWidth>
      <DialogTitle>Отменить банкет №{banquet.id}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, pt: '8px !important' }}>
        {prepaid.length > 0 && <Alert severity="warning">Предоплата {money(prepaid.reduce((s, p) => s + p.amount, 0))} отметится как возвращённая — деньги гостю возвращаете вы.</Alert>}
        {banquet.items.some(i => i.sent) && <Typography variant="body2" color="text.secondary">Блюда на кухне отменятся по обычным правилам: начатые — с подтверждением кухни, готовые — со списанием.</Typography>}
        <TextField label="Причина" value={reason} onChange={e => setReason(e.target.value)} autoFocus />
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button onClick={onClose} disabled={cancel.isPending} sx={{ color: 'text.secondary' }}>Назад</Button>
        <Button variant="contained" color="error" onClick={submit} disabled={cancel.isPending || !reason.trim()}>Отменить банкет</Button>
      </DialogActions>
    </Dialog>
  );
};

const Row: React.FC<{ label: string; value: string; bold?: boolean; big?: boolean; action?: React.ReactNode }> = ({ label, value, bold, big, action }) => (
  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, py: 0.25 }}>
    <Box sx={{ flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', flexWrap: 'wrap', columnGap: 0.5 }}>
      <Typography sx={{ fontWeight: bold ? 700 : 400, fontSize: big ? '1.15rem' : undefined }}>{label}</Typography>
      {action}
    </Box>
    <Typography sx={{ fontWeight: bold ? 800 : 500, fontSize: big ? '1.15rem' : undefined, whiteSpace: 'nowrap' }}>{value}</Typography>
  </Box>
);

const BanquetView: React.FC<{ id: number }> = ({ id }) => {
  const navigate = useNavigate();
  const { data: b, isLoading, error } = useBanquet(id);
  const addItems = useAddBanquetItems();
  const setQty = useSetBanquetItemQty();
  const removeItem = useRemoveBanquetItem();
  const sendNow = useSendBanquetToKitchen();
  const print = usePrintBanquet();
  const discount = useBanquetDiscount();
  const refund = useRefundBanquetPayment();
  const retry = useRetryBanquetReceipt();
  const [dialog, setDialog] = React.useState<'edit' | 'menu' | 'prepay' | 'close' | 'cancel' | 'discount' | null>(null);
  const [toast, setToast] = React.useState('');
  const [printing, setPrinting] = React.useState(false);

  if (isLoading) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;
  if (error || !b) return <Alert severity="error">{apiError(error, 'Банкет не найден')}</Alert>;
  const open = b.status === 'open';
  const t = b.totals;
  const unsent = b.items.filter(i => !i.sent);

  const run = async (fn: () => Promise<unknown>, ok?: string) => {
    try { await fn(); if (ok) setToast(ok); } catch (e) { setToast(apiError(e)); }
  };

  const doPrint = async () => {
    setPrinting(true);
    setToast('Печатаем пречек…');
    try {
      const { jobId } = await print.mutateAsync(b.id);
      const started = Date.now();
      while (Date.now() - started < 25000) {
        await new Promise(r => setTimeout(r, 1500));
        const job = await fetchPrintJob(jobId);
        if (job.status === 'done') { setToast('Пречек напечатан'); return; }
        if (job.status === 'error') { setToast(`Не напечатан: ${job.error ?? 'ошибка принтера'}`); return; }
        if (job.status === 'pending' && !job.agentOnline && Date.now() - started > 6000) { setToast('Программа печати на кассовом ПК не запущена'); return; }
      }
      setToast('Принтер долго не отвечает');
    } catch (e) { setToast(apiError(e)); } finally { setPrinting(false); }
  };

  const onAdd = async (lines: DraftLine[]) => {
    await addItems.mutateAsync({
      id: b.id,
      items: lines.map(l => ({ dishId: l.dishId, quantity: l.quantity, weight: l.weight, comment: l.comment || undefined, addons: l.addons.map(a => ({ addonId: a.addonId, quantity: a.quantity })) })),
    });
    setToast('Блюда добавлены — на кухню уйдут в назначенное время');
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, maxWidth: 900, mx: 'auto' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <IconButton onClick={() => navigate('/admin/banquets')} aria-label="К банкетам" sx={{ ml: -1 }}><BackIcon /></IconButton>
        <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }}>Банкет №{b.id}</Typography>
        <Chip size="small" color={STATUS[b.status].color} label={STATUS[b.status].text} />
        {open && <IconButton onClick={() => setDialog('edit')} aria-label="Изменить"><EditIcon /></IconButton>}
      </Box>
      <Box sx={{ mt: -1 }}>
        <Typography variant="h5" sx={{ fontWeight: 800, wordBreak: 'break-word' }}>{b.customerName}</Typography>
        <Typography variant="body2" color="text.secondary">
          {dt(b.eventAt)}{b.guests ? ` · гостей ${b.guests}` : ''}{b.phone ? ` · ${b.phone}` : ''}{b.table ? ` · стол ${b.table}` : ''}
        </Typography>
      </Box>
      {b.comment && <Typography variant="body2" sx={{ whiteSpace: 'pre-line' }}>{b.comment}</Typography>}

      {open && (
        <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2, display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
          <KitchenIcon color="action" />
          <Box sx={{ flex: 1, minWidth: 200 }}>
            <Typography sx={{ fontWeight: 700 }}>
              {b.kitchenAt ? `На кухню: ${dt(b.kitchenAt)}` : 'Время отправки на кухню не задано'}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {unsent.length > 0 ? `Не отправлено блюд: ${unsent.reduce((s, i) => s + i.quantity, 0)}${b.kitchenAt ? ' — уйдут автоматически' : ' — задайте время или отправьте кнопкой'}` : b.items.length ? 'Все блюда на кухне' : 'Блюд пока нет'}
            </Typography>
          </Box>
          {!b.kitchenAt && <Button onClick={() => setDialog('edit')}>Задать время</Button>}
          <Button variant="outlined" disabled={unsent.length === 0 || sendNow.isPending}
            onClick={() => run(() => sendNow.mutateAsync(b.id), 'Блюда отправлены на кухню')}>Отправить сейчас</Button>
        </Paper>
      )}

      <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', mb: 1 }}>
          <Typography sx={{ fontWeight: 800, flex: 1 }}>Блюда</Typography>
          {open && <Button startIcon={<AddIcon />} variant="contained" color="secondary" onClick={() => setDialog('menu')}>Добавить</Button>}
        </Box>
        {b.items.length === 0 && <Typography color="text.secondary">Пока пусто</Typography>}
        {b.items.map(i => (
          <Box key={i.id} sx={{ display: 'flex', alignItems: 'center', columnGap: 1, rowGap: 0.25, py: 0.75, borderTop: 1, borderColor: 'divider', flexWrap: { xs: 'wrap', sm: 'nowrap' } }}>
            <Box sx={{ flex: { xs: '1 1 100%', sm: 1 }, minWidth: 0 }}>
              <Typography sx={{ fontWeight: 600 }}>{i.name}{i.addons.length ? <Typography component="span" variant="body2" color="text.secondary"> + {i.addons.map(a => a.name).join(', ')}</Typography> : null}</Typography>
              <Typography variant="caption" color={i.sent ? 'success.main' : 'text.secondary'}>
                {i.sent ? (KITCHEN[i.kitchenStatus ?? ''] ?? 'на кухне') : 'ещё не на кухне'}{i.comment ? ` · ${i.comment}` : ''} · {money(i.price)}
              </Typography>
            </Box>
            {open && !i.sent ? (
              <Box sx={{ display: 'flex', alignItems: 'center', ml: { xs: -1, sm: 0 }, flex: { xs: 1, sm: 'none' } }}>
                <IconButton size="small" disabled={i.quantity <= 1} onClick={() => run(() => setQty.mutateAsync({ id: b.id, itemId: i.id, quantity: i.quantity - 1 }))}><RemoveIcon fontSize="small" /></IconButton>
                <Typography sx={{ minWidth: 28, textAlign: 'center', fontWeight: 700 }}>{i.quantity}</Typography>
                <IconButton size="small" onClick={() => run(() => setQty.mutateAsync({ id: b.id, itemId: i.id, quantity: i.quantity + 1 }))}><AddIcon fontSize="small" /></IconButton>
                <IconButton size="small" onClick={() => run(() => removeItem.mutateAsync({ id: b.id, itemId: i.id }))} aria-label="Убрать"><DeleteIcon fontSize="small" /></IconButton>
              </Box>
            ) : <Typography sx={{ fontWeight: 700, flex: { xs: 1, sm: 'none' } }}>× {i.quantity}</Typography>}
            <Typography sx={{ fontWeight: 700, minWidth: 80, textAlign: 'right' }}>{money(i.price * i.quantity)}</Typography>
          </Box>
        ))}
      </Paper>

      <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2 }}>
        <Row label="Блюда" value={money(t.dishes)} />
        {(t.discount > 0 || b.canDiscount) && (
          <Row label={t.discount > 0 ? `Скидка${b.discountType === 'percent' ? ` ${b.discountValue}%` : ''}${b.discountBy ? ` (${b.discountBy})` : ''}` : 'Скидка'}
            value={t.discount > 0 ? `−${money(t.discount)}` : '—'}
            action={b.canDiscount ? <Button size="small" onClick={() => setDialog('discount')} sx={{ minWidth: 0, px: 1, py: 0 }}>{t.discount > 0 ? 'Изменить' : 'Добавить'}</Button> : undefined} />
        )}
        <Row label={`Обслуживание${b.serviceType === 'percent' && b.serviceValue ? ` ${b.serviceValue}%` : ''}`} value={t.service > 0 ? money(t.service) : '—'}
          action={open ? <Button size="small" onClick={() => setDialog('edit')} sx={{ minWidth: 0, px: 1, py: 0 }}>Изменить</Button> : undefined} />
        <Divider sx={{ my: 0.75 }} />
        <Row label="Итого" value={money(t.total)} bold big />
        {t.prepaid > 0 && <Row label="Предоплата" value={`−${money(t.prepaid)}`} />}
        {open && t.prepaid > 0 && <Row label="К оплате" value={money(t.due)} bold />}
      </Paper>

      <Paper variant="outlined" sx={{ borderRadius: '16px', p: 2 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', mb: 0.5 }}>
          <Typography sx={{ fontWeight: 800, flex: 1 }}>Оплаты</Typography>
          {open && <Button variant="outlined" onClick={() => setDialog('prepay')}>Предоплата</Button>}
        </Box>
        {b.payments.length === 0 && <Typography color="text.secondary">Оплат пока нет</Typography>}
        {b.payments.map(p => {
          const f = p.fiscal;
          const bad = f && f.status !== 'done';
          return (
            <Box key={p.id} sx={{ display: 'flex', alignItems: 'center', gap: 1, py: 0.5, borderTop: 1, borderColor: 'divider', flexWrap: 'wrap', opacity: p.refundedAt ? 0.55 : 1 }}>
              <Box sx={{ flex: 1, minWidth: 200 }}>
                <Typography sx={{ fontWeight: 600 }}>
                  {p.kind === 'prepay' ? 'Предоплата' : 'Расчёт'} · {p.method === 'prepaid' ? 'зачёт предоплаты' : payLabel(p.method)}{p.staffName ? ` (${p.staffName})` : ''}
                  {p.refundedAt ? ' · возвращена' : ''}
                </Typography>
                <Typography variant="caption" color={bad ? 'error.main' : 'text.secondary'}>
                  {parseUtc(p.createdAt).toLocaleString('ru-RU', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })}{p.createdBy ? ` · ${p.createdBy}` : ''}
                  {f ? ` · ${f.kind === 'return' ? 'возврат' : 'чек'} ${f.status === 'done' ? `ФД ${f.fiscalDocumentNumber ?? '—'}` : f.status === 'processing' ? 'касса не ответила' : `не пробит: ${f.error ?? ''}`}` : ''}
                </Typography>
              </Box>
              <Typography sx={{ fontWeight: 700 }}>{money(p.amount)}</Typography>
              {bad && <Button size="small" variant="outlined" disabled={retry.isPending}
                onClick={() => run(async () => { const r = await retry.mutateAsync(f!.id); setToast(atolText(r)); })}>{f!.status === 'processing' ? 'Проверить' : 'Пробить'}</Button>}
              {open && p.kind === 'prepay' && !p.refundedAt && (
                <Button size="small" color="inherit" disabled={refund.isPending}
                  onClick={() => window.confirm(`Вернуть предоплату ${money(p.amount)}? Деньги гостю возвращаете вы.${p.fiscal?.sold ? ' В АТОЛ уйдёт чек возврата.' : ''}`)
                    && run(() => refund.mutateAsync({ id: b.id, paymentId: p.id }), 'Предоплата возвращена')}>Вернуть</Button>
              )}
            </Box>
          );
        })}
      </Paper>

      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
        {b.printEnabled && b.items.length > 0 && (
          <Button variant="outlined" startIcon={printing ? <CircularProgress size={16} /> : <PrintIcon />} disabled={printing} onClick={doPrint}>Печать пречека</Button>
        )}
        <Box sx={{ flex: 1 }} />
        {open && <Button color="error" onClick={() => setDialog('cancel')}>Отменить банкет</Button>}
        {open && <Button variant="contained" color="secondary" size="large" disabled={b.items.length === 0} onClick={() => setDialog('close')}>Закрыть банкет</Button>}
      </Box>
      {b.closedAt && <Typography variant="body2" color="text.secondary">{b.status === 'closed' ? 'Закрыт' : 'Отменён'} {dt(b.closedAt)}{b.closedBy ? ` · ${b.closedBy}` : ''}</Typography>}

      {dialog === 'edit' && <BanquetForm banquet={b} onClose={() => setDialog(null)} onSaved={() => { setDialog(null); setToast('Сохранено'); }} />}
      <MenuPicker open={dialog === 'menu'} tableName="" title={`Банкет №${b.id} · добавить`} sendLabel="Добавить" onClose={() => setDialog(null)} onSend={onAdd} />
      {dialog === 'prepay' && <PrepayDialog banquet={b} onClose={() => setDialog(null)} onDone={x => { setDialog(null); setToast(x); }} />}
      {dialog === 'close' && <CloseDialog banquet={b} onClose={() => setDialog(null)} onDone={x => { setDialog(null); setToast(x); }} />}
      {dialog === 'cancel' && <CancelDialog banquet={b} onClose={() => setDialog(null)} onDone={x => { setDialog(null); setToast(x); }} />}
      <DiscountDialog open={dialog === 'discount'} base={t.dishes} type={b.discountType} value={b.discountValue} onClose={() => setDialog(null)}
        onSave={(type, value) => discount.mutateAsync({ id: b.id, type, value })} />
      <Snackbar open={!!toast} autoHideDuration={toast.length > 60 ? 7000 : 3500} onClose={() => setToast('')} message={toast} anchorOrigin={{ vertical: 'top', horizontal: 'center' }} />
    </Box>
  );
};

// ==================== Список ====================

const BanquetList: React.FC = () => {
  const navigate = useNavigate();
  const today = todayMsk();
  const [from, setFrom] = React.useState(addDays(today, -7));
  const [to, setTo] = React.useState(addDays(today, 90));
  const { data, isLoading, error } = useBanquets(from, to);
  const [creating, setCreating] = React.useState(false);
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, maxWidth: 900, mx: 'auto' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
        <Typography variant="h4" sx={{ fontWeight: 800, flex: 1 }}>Банкеты</Typography>
        <Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={() => setCreating(true)}>Новый</Button>
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: '180px 180px' }, gap: 1 }}>
        <TextField type="date" size="small" label="С" value={from} onChange={e => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
        <TextField type="date" size="small" label="По" value={to} onChange={e => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
      </Box>
      {error && <Alert severity="error">{apiError(error, 'Не удалось загрузить банкеты')}</Alert>}
      {isLoading && <Box sx={{ display: 'grid', placeItems: 'center', py: 4 }}><CircularProgress /></Box>}
      {data?.length === 0 && <Typography color="text.secondary" sx={{ textAlign: 'center', py: 4 }}>Банкетов нет</Typography>}
      {data?.map(x => (
        <Paper key={x.id} variant="outlined" sx={{ borderRadius: '16px' }}>
          <ButtonBase onClick={() => navigate(`/admin/banquets/${x.id}`)} sx={{ width: '100%', textAlign: 'left', p: 2, borderRadius: '16px', display: 'flex', gap: 1.5, alignItems: 'flex-start' }}>
            <Box sx={{ flex: 1, minWidth: 0 }}>
              <Typography sx={{ fontWeight: 800 }}>{dt(x.eventAt)}</Typography>
              <Typography variant="body2" color="text.secondary">
                №{x.id} · {x.customerName}{x.guests ? ` · гостей ${x.guests}` : ''}{x.table ? ` · стол ${x.table}` : ''}
                {x.status === 'open' && x.unsent > 0 ? ` · не на кухне: ${x.unsent}${x.kitchenAt ? '' : ' (время не задано)'}` : ''}
              </Typography>
            </Box>
            <Box sx={{ textAlign: 'right', display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 0.5 }}>
              <Chip size="small" color={STATUS[x.status].color} label={STATUS[x.status].text} />
              <Typography sx={{ fontWeight: 800, whiteSpace: 'nowrap' }}>{money(x.total)}</Typography>
              {x.prepaid > 0 && <Typography variant="body2" color="text.secondary">предоплата {money(x.prepaid)}</Typography>}
            </Box>
          </ButtonBase>
        </Paper>
      ))}
      {creating && <BanquetForm onClose={() => setCreating(false)} onSaved={id => { setCreating(false); navigate(`/admin/banquets/${id}`); }} />}
    </Box>
  );
};

/** Банкеты: список и карточка банкета */
const BanquetsPage: React.FC = () => {
  const { id } = useParams();
  return (
    <Box sx={{ p: { xs: 2, md: 3 } }}>
      {id ? <BanquetView id={Number(id)} /> : <BanquetList />}
    </Box>
  );
};

export default BanquetsPage;
