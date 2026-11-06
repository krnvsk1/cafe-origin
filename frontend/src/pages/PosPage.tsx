import * as React from 'react';
import {
  Box, Typography, Button, TextField, InputAdornment, ToggleButtonGroup, ToggleButton, IconButton, Chip,
  CircularProgress, Alert, Dialog, DialogContent, DialogActions, Switch, FormControlLabel, Drawer, Collapse,
  useMediaQuery, useTheme, Snackbar, Menu, MenuItem, Checkbox,
} from '@mui/material';
import {
  Search as SearchIcon, Add as AddIcon, Remove as RemoveIcon, DeleteOutline as DeleteIcon,
  ReceiptLongOutlined as ReceiptIcon, CheckCircleRounded as CheckIcon, PersonOutline as PersonIcon,
  ExpandMore as ExpandMoreIcon, ExpandLess as ExpandLessIcon, KeyboardArrowDown as ArrowDownIcon,
  PrintOutlined as PrintIcon,
} from '@mui/icons-material';
import { useDishes } from '../api/hooks';
import {
  useCreatePosOrder, usePosOrders, usePosSummary, useSetOrderStatus, lookupPosCustomer, type PosOrder,
} from '../api/pos';
import { apiError } from '../api/inventory';
import { useQueryClient } from '@tanstack/react-query';
import {
  useFiscalState, fiscalShift, fiscalSell, fiscalReturn, SHIFT_TEXT, type FiscalReceiptResult,
} from '../api/fiscal';
import ProductModal from '../components/ProductModal';
import DiscountDialog, { discountOf, type DiscountType } from '../components/DiscountDialog';
import { useCan, P } from '../auth/permissions';
import FullscreenButton from '../components/FullscreenButton';
import { useIsFullscreen, useWorkScreen } from '../hooks/useFullscreen';
import type { Dish, SelectedAddon } from '../types';
import { resolveMediaUrl } from '../utils/media';
import { tokens } from '../theme';
import { isWeightedDish, priceForWeight, weightLabel } from '../utils/weight';
import { allowedOrderTypes, isAllowedFor, describeOrderTypes } from '../utils/orderTypes';

interface TicketLine {
  key: string;
  dishId: number;
  title: string;
  variant: string | null;
  unitPrice: number;
  quantity: number;
  addons: SelectedAddon[];
  comment: string;
  /** Где можно продать (null — везде), например пиво — только в зале */
  allowed: string[] | null;
  /** Вес весового блюда, г (unitPrice — уже за этот вес) */
  weight?: number;
}

const money = (v: number) => `${Math.round(v * 100) / 100} ₽`.replace('.', ',');
const lineTotal = (l: TicketLine) => (l.unitPrice + l.addons.reduce((s, a) => s + a.price * a.quantity, 0)) * l.quantity;
const lineKey = (id: number, addons: SelectedAddon[], comment: string, weight?: number) =>
  `${id}|${weight ?? ''}|${addons.map(a => `${a.addonId}x${a.quantity}`).sort().join(',')}|${comment}`;

/** Есть ли у позиции добавки (тогда нужно окно выбора) */
const hasAddons = (s: Dish) => ((s as unknown as { addons?: unknown[] }).addons?.length ?? 0) > 0;

const STATUS_COLORS: Record<string, string> = {
  'Новый': '#2563EB',
  'Принят': '#C77700',
  'Готовится': '#C77700',
  'Готов': tokens.herb,
  'Выполнен': '#7B776F',
  'Отменён': tokens.chili,
};

// ============ Меню кассы ============

const PosMenu: React.FC<{ onPick: (card: Dish) => void }> = ({ onPick }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const { data: menu = [], isLoading } = useDishes();
  const [search, setSearch] = React.useState('');
  const [section, setSection] = React.useState('');

  const cards = React.useMemo(() =>
    menu
      .filter(i => i.parentId === null || i.parentId === undefined)
      .map(i => ({ ...i, children: (i.children ?? []).filter(c => c.isAvailable) }))
      .filter(i => i.isAvailable && i.children.length > 0),
  [menu]);

  const sections = React.useMemo(() => [...new Set(cards.map(c => c.category?.trim() || 'Меню'))], [cards]);
  const q = search.trim().toLowerCase();
  const visible = cards.filter(c =>
    (!section || (c.category?.trim() || 'Меню') === section) &&
    (!q || c.name.toLowerCase().includes(q) || c.children.some(ch => ch.name.toLowerCase().includes(q))));

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, minWidth: 0 }}>
      <TextField
        placeholder="Поиск по меню"
        value={search}
        onChange={e => setSearch(e.target.value)}
        size="small"
        sx={{ '& .MuiOutlinedInput-root': { bgcolor: 'background.paper' } }}
        slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon sx={{ color: 'text.secondary' }} /></InputAdornment> } }}
      />
      <Box sx={{ display: 'flex', gap: 0.75, overflowX: 'auto', pb: 0.5, scrollbarWidth: 'none', '&::-webkit-scrollbar': { display: 'none' } }}>
        <Chip label="Все" color={section === '' ? 'primary' : 'default'} onClick={() => setSection('')} sx={{ bgcolor: section === '' ? undefined : 'background.paper' }} />
        {sections.map(s => (
          <Chip key={s} label={s} color={section === s ? 'primary' : 'default'} onClick={() => setSection(section === s ? '' : s)}
            sx={{ bgcolor: section === s ? undefined : 'background.paper', flexShrink: 0 }} />
        ))}
      </Box>

      {isLoading ? (
        <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
      ) : (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', sm: 'repeat(3, minmax(0, 1fr))', xl: 'repeat(4, minmax(0, 1fr))' }, gap: 1 }}>
          {visible.map(card => {
            const img = card.primaryImage || card.images?.find(i => i.isPrimary)?.filePath || card.images?.[0]?.filePath;
            const prices = card.children.map(c => c.price);
            const min = Math.min(...prices);
            return (
              <Box
                key={card.id}
                component="button"
                type="button"
                onClick={() => onPick(card)}
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 1.25,
                  p: 1,
                  border: 0,
                  borderRadius: '16px',
                  bgcolor: 'background.paper',
                  color: 'text.primary',
                  font: 'inherit',
                  textAlign: 'left',
                  cursor: 'pointer',
                  minHeight: 72,
                  transition: 'transform 0.08s ease, box-shadow 0.15s ease',
                  '&:hover': { boxShadow: '0 4px 14px rgba(33,32,31,0.08)' },
                  '&:active': { transform: 'scale(0.98)' },
                }}
              >
                <Box sx={{ width: 56, height: 56, borderRadius: '12px', overflow: 'hidden', bgcolor: t.surfaceMuted, flexShrink: 0, display: 'grid', placeItems: 'center' }}>
                  {img ? <Box component="img" src={resolveMediaUrl(img)} alt="" loading="lazy" sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                    : <Typography aria-hidden sx={{ fontSize: 26 }}>🍽️</Typography>}
                </Box>
                <Box sx={{ minWidth: 0 }}>
                  <Typography sx={{ fontWeight: 600, fontSize: '0.92rem', lineHeight: 1.25, display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                    {card.name}
                  </Typography>
                  <Typography sx={{ fontWeight: 800, fontSize: '0.92rem', mt: 0.25 }}>
                    {card.children.length > 1 && min !== Math.max(...prices) ? 'от ' : ''}{money(min)}{card.isWeighted ? ' / 100 г' : ''}
                  </Typography>
                </Box>
              </Box>
            );
          })}
        </Box>
      )}
    </Box>
  );
};

// ============ Чек ============

// Строка чека считается скрытой, если ниже видимой области осталось больше этого
const HIDDEN_LINE_EPSILON_PX = 4;
// Кнопка «ещё N поз.» прокручивает чек на такую долю его высоты
const SCROLL_PAGE_SHARE = 0.8;

/** Сколько строк в прокручиваемом блоке скрыто ниже видимой области (пересчёт при прокрутке, изменении размера и строк) */
const useHiddenBelow = (deps: unknown) => {
  const ref = React.useRef<HTMLDivElement>(null);
  const [hidden, setHidden] = React.useState(0);

  const update = React.useCallback(() => {
    const el = ref.current;
    if (!el) return;
    const visibleBottom = el.scrollTop + el.clientHeight + HIDDEN_LINE_EPSILON_PX;
    const rows = Array.from(el.children) as HTMLElement[];
    setHidden(rows.filter(row => row.offsetTop + row.offsetHeight > visibleBottom).length);
  }, []);

  React.useEffect(() => {
    update();
    const el = ref.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(update);
    observer.observe(el);
    return () => observer.disconnect();
  }, [update, deps]);

  const scrollDown = () => ref.current?.scrollBy({ top: ref.current.clientHeight * SCROLL_PAGE_SHARE, behavior: 'smooth' });

  return { ref, hidden, update, scrollDown };
};

const Ticket: React.FC<{
  lines: TicketLine[];
  onChange: (lines: TicketLine[]) => void;
  onDone: (orderId: number, change: number | null, fiscal: FiscalReceiptResult | null) => void;
}> = ({ lines, onChange, onDone }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const create = useCreatePosOrder();
  const [orderType, setOrderType] = React.useState<'В зале' | 'Самовывоз'>('В зале');
  const [payment, setPayment] = React.useState<'cash' | 'card'>('card');
  const [cash, setCash] = React.useState('');
  const [showGuest, setShowGuest] = React.useState(false);
  const [phone, setPhone] = React.useState('');
  const [name, setName] = React.useState('');
  const [guest, setGuest] = React.useState<{ found: boolean; name?: string; points?: number } | null>(null);
  const [comment, setComment] = React.useState('');
  const [toKitchen, setToKitchen] = React.useState(true);
  const [error, setError] = React.useState('');
  // Скидка (право «Скидки»)
  const canDiscount = useCan()(P.Discounts);
  const [disc, setDisc] = React.useState<{ type: DiscountType; value: number } | null>(null);
  const [discOpen, setDiscOpen] = React.useState(false);

  const more = useHiddenBelow(lines);

  const full = lines.reduce((s, l) => s + lineTotal(l), 0);
  const discount = disc ? discountOf(disc.type, disc.value, full) : 0;
  const total = full - discount;
  const received = parseFloat(cash.replace(',', '.')) || 0;
  const change = payment === 'cash' && received > 0 ? received - total : null;
  const count = lines.reduce((s, l) => s + l.quantity, 0);
  const blocked = lines.filter(l => !isAllowedFor(l.allowed, orderType));

  React.useEffect(() => {
    const digits = phone.replace(/\D/g, '');
    if (digits.length < 10) { setGuest(null); return; }
    const id = setTimeout(() => {
      lookupPosCustomer(phone).then(r => {
        setGuest(r);
        if (r.found && r.name && !name) setName(r.name);
      }).catch(() => setGuest(null));
    }, 400);
    return () => clearTimeout(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phone]);

  const setQty = (key: string, delta: number) =>
    onChange(lines.flatMap(l => (l.key !== key ? [l] : l.quantity + delta <= 0 ? [] : [{ ...l, quantity: l.quantity + delta }])));

  const reset = () => {
    setCash(''); setPhone(''); setName(''); setGuest(null); setComment(''); setShowGuest(false); setError(''); setDisc(null);
  };

  const submit = async () => {
    setError('');
    if (lines.length === 0) return;
    if (payment === 'cash' && cash && received < total) {
      setError('Получено меньше суммы чека');
      return;
    }
    try {
      const res = await create.mutateAsync({
        items: lines.map(l => ({
          dishId: l.dishId,
          quantity: l.quantity,
          weight: l.weight,
          addons: l.addons.map(a => ({ addonId: a.addonId, quantity: a.quantity })),
          comment: l.comment || undefined,
        })),
        orderType,
        paymentMethod: payment,
        cashReceived: payment === 'cash' && cash ? received : null,
        customerName: name.trim() || null,
        phone: phone.trim() || null,
        comment: comment.trim() || null,
        sendToKitchen: toKitchen,
        discountType: disc?.type ?? null,
        discountValue: disc?.value ?? null,
      });
      onDone(res.id, res.change, res.fiscal ?? null);
      reset();
    } catch (e) {
      setError(apiError(e));
    }
  };

  const quickCash = [...new Set([Math.ceil(total / 100) * 100, Math.ceil(total / 500) * 500, 1000, 2000, 5000])]
    .filter(v => v >= total && v > 0).slice(0, 4);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}>
      <Box sx={{ display: 'flex', alignItems: 'baseline', px: 2, pt: 2, pb: 1 }}>
        <Typography sx={{ fontWeight: 800, fontSize: '1.25rem', flex: 1 }}>Чек</Typography>
        {lines.length > 0 && (
          <Button size="small" onClick={() => onChange([])} sx={{ color: 'text.secondary' }}>Очистить</Button>
        )}
      </Box>

      <Box sx={{ flex: 1, minHeight: 80, position: 'relative', display: 'flex', flexDirection: 'column' }}>
      {/* position: relative — чтобы offsetTop строк считался от начала списка */}
      <Box ref={more.ref} onScroll={more.update} sx={{ flex: 1, minHeight: 0, overflowY: 'auto', px: 2, position: 'relative' }}>
        {lines.length === 0 ? (
          <Box sx={{ textAlign: 'center', color: 'text.secondary', py: 5 }}>
            <ReceiptIcon sx={{ fontSize: 40, mb: 1 }} />
            <Typography>Нажмите на блюдо, чтобы добавить его в чек</Typography>
          </Box>
        ) : lines.map(l => (
          <Box key={l.key} sx={{ py: 1.25, borderTop: '1px solid', borderColor: 'divider', '&:first-of-type': { borderTop: 0 } }}>
            <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start' }}>
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography sx={{ fontWeight: 600, lineHeight: 1.3 }}>
                  {l.title}{l.variant ? <Box component="span" sx={{ color: 'text.secondary', fontWeight: 500 }}> · {l.variant}</Box> : null}
                </Typography>
                {l.addons.length > 0 && (
                  <Typography variant="body2" color="text.secondary">
                    {l.addons.map(a => `${a.addonName}${a.quantity > 1 ? ` ×${a.quantity}` : ''}`).join(', ')}
                  </Typography>
                )}
                {l.comment && <Typography variant="body2" color="text.secondary">«{l.comment}»</Typography>}
              </Box>
              <Typography sx={{ fontWeight: 700, whiteSpace: 'nowrap' }}>{money(lineTotal(l))}</Typography>
            </Box>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, mt: 0.75 }}>
              <IconButton size="small" aria-label="Меньше" onClick={() => setQty(l.key, -1)} sx={{ bgcolor: t.surfaceMuted }}>
                {l.quantity === 1 ? <DeleteIcon fontSize="small" /> : <RemoveIcon fontSize="small" />}
              </IconButton>
              <Typography sx={{ minWidth: 28, textAlign: 'center', fontWeight: 700 }}>{l.quantity}</Typography>
              <IconButton size="small" aria-label="Больше" onClick={() => setQty(l.key, 1)} sx={{ bgcolor: t.surfaceMuted }}>
                <AddIcon fontSize="small" />
              </IconButton>
            </Box>
          </Box>
        ))}
      </Box>
      {/* Позиции ниже края чека: стрелка, чтобы кассир не пропустил их */}
      {more.hidden > 0 && (
        <Box
          sx={{
            position: 'absolute', left: 0, right: 0, bottom: 0, pt: 4, pb: 1,
            display: 'flex', justifyContent: 'center', pointerEvents: 'none',
            background: `linear-gradient(to bottom, transparent, ${theme.palette.background.paper} 65%)`,
          }}
        >
          <Button
            size="small"
            onClick={more.scrollDown}
            startIcon={<ArrowDownIcon />}
            aria-label={`Ниже ещё ${more.hidden} поз. — прокрутить чек`}
            sx={{
              pointerEvents: 'auto',
              borderRadius: 999,
              px: 1.75,
              fontWeight: 700,
              bgcolor: 'text.primary',
              color: 'background.paper',
              boxShadow: '0 4px 14px rgba(33,32,31,0.25)',
              '&:hover': { bgcolor: 'text.primary', opacity: 0.9 },
              '& .MuiButton-startIcon': { animation: 'pos-more-bounce 1.2s ease-in-out infinite' },
              '@keyframes pos-more-bounce': { '0%, 100%': { transform: 'translateY(0)' }, '50%': { transform: 'translateY(3px)' } },
            }}
          >
            ещё {more.hidden} поз.
          </Button>
        </Box>
      )}
      </Box>

      {/* Оформление */}
      <Box sx={{ px: 2, pt: 1.5, pb: 2, borderTop: '1px solid', borderColor: 'divider', display: 'flex', flexDirection: 'column', gap: 1.25 }}>
        {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
        {blocked.length > 0 && (
          <Alert severity="warning">
            {blocked.map(l => l.title).join(', ')} — {describeOrderTypes(blocked[0].allowed ?? [])}. Переключите «В зале» или уберите из чека.
          </Alert>
        )}

        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1 }}>
          <ToggleButtonGroup exclusive size="small" value={orderType} onChange={(_, v) => v && setOrderType(v)} sx={{ display: 'flex', '& .MuiToggleButton-root': { flex: 1 } }}>
            <ToggleButton value="В зале">В зале</ToggleButton>
            <ToggleButton value="Самовывоз">С собой</ToggleButton>
          </ToggleButtonGroup>
          <ToggleButtonGroup exclusive size="small" value={payment} onChange={(_, v) => v && setPayment(v)} sx={{ display: 'flex', '& .MuiToggleButton-root': { flex: 1 } }}>
            <ToggleButton value="card">Карта</ToggleButton>
            <ToggleButton value="cash">Наличные</ToggleButton>
          </ToggleButtonGroup>
        </Box>

        {payment === 'cash' && lines.length > 0 && (
          <Box>
            <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
              <TextField
                size="small"
                type="number"
                placeholder="Получено"
                value={cash}
                onChange={e => setCash(e.target.value)}
                sx={{ flex: 1 }}
                slotProps={{ input: { endAdornment: <InputAdornment position="end">₽</InputAdornment> }, htmlInput: { min: 0, inputMode: 'decimal' } }}
              />
              <Typography sx={{ fontWeight: 700, minWidth: 110, textAlign: 'right', color: change !== null && change < 0 ? tokens.chili : 'text.primary' }}>
                {change === null ? 'без сдачи' : change < 0 ? `не хватает ${money(-change)}` : `сдача ${money(change)}`}
              </Typography>
            </Box>
            <Box sx={{ display: 'flex', gap: 0.75, mt: 0.75, flexWrap: 'wrap' }}>
              {quickCash.map(v => (
                <Chip key={v} label={money(v)} size="small" onClick={() => setCash(String(v))} />
              ))}
            </Box>
          </Box>
        )}

        {canDiscount && lines.length > 0 && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Button size="small" onClick={() => setDiscOpen(true)} sx={{ px: 1 }}>{disc ? 'Изменить скидку' : 'Добавить скидку'}</Button>
            {discount > 0 && (
              <Typography variant="body2" color="text.secondary">
                {money(full)} − {money(discount)}{disc?.type === 'percent' ? ` (${disc.value}%)` : ''}
              </Typography>
            )}
          </Box>
        )}
        <DiscountDialog open={discOpen} base={full} type={disc?.type} value={disc?.value} onClose={() => setDiscOpen(false)}
          onSave={(type, value) => setDisc(type && value ? { type, value } : null)} />

        <Button
          size="small"
          onClick={() => setShowGuest(s => !s)}
          startIcon={<PersonIcon />}
          endIcon={showGuest ? <ExpandLessIcon /> : <ExpandMoreIcon />}
          sx={{ alignSelf: 'flex-start', color: 'text.secondary', px: 1 }}
        >
          {guest?.found ? `${guest.name || 'Гость'} · ${guest.points ?? 0} баллов` : 'Гость, комментарий'}
        </Button>
        <Collapse in={showGuest}>
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1 }}>
              <TextField size="small" label="Телефон" value={phone} onChange={e => setPhone(e.target.value)} slotProps={{ htmlInput: { inputMode: 'decimal' } }}
                helperText={guest ? (guest.found ? 'Гость найден — начислим баллы' : 'Не зарегистрирован') : ' '} />
              <TextField size="small" label="Имя" value={name} onChange={e => setName(e.target.value)} helperText=" " />
            </Box>
            <TextField size="small" label="Комментарий для кухни" value={comment} onChange={e => setComment(e.target.value)} />
            <FormControlLabel control={<Switch checked={toKitchen} onChange={e => setToKitchen(e.target.checked)} />} label="Сразу отправить на кухню" />
          </Box>
        </Collapse>

        <Button
          variant="contained"
          color="secondary"
          size="large"
          disabled={lines.length === 0 || create.isPending || (change !== null && change < 0) || blocked.length > 0}
          onClick={submit}
          startIcon={create.isPending ? <CircularProgress size={18} color="inherit" /> : undefined}
          sx={{ minHeight: 56, fontSize: '1.05rem' }}
        >
          {lines.length === 0 ? 'Чек пуст' : `Принять оплату · ${money(total)}`}
        </Button>
        {lines.length > 0 && (
          <Typography variant="caption" color="text.secondary" sx={{ textAlign: 'center' }}>
            {count} поз. · {payment === 'card' ? 'оплата картой' : 'наличными'} · {orderType === 'В зале' ? 'в зале' : 'с собой'}
          </Typography>
        )}
      </Box>
    </Box>
  );
};

// ============ Заказы за смену ============

const NEXT_STATUS: Record<string, { status: string; label: string } | undefined> = {
  'Новый': { status: 'Готовится', label: 'На кухню' },
  'Принят': { status: 'Готовится', label: 'Начать готовить' },
  'Готовится': { status: 'Готов', label: 'Готов' },
  'Готов': { status: 'Выполнен', label: 'Выдан' },
};

/** Строка «чек» в карточке заказа смены: пробит / ошибка / возврат, с кнопками */
const FiscalLine: React.FC<{ order: PosOrder; busy: boolean; onPrint: (kind: 'sell' | 'return') => void }> = ({ order: o, busy, onPrint }) => {
  const f = o.fiscal;
  const cancelled = o.status === 'Отменён';
  const returned = f?.kind === 'sellReturn' && f.status === 'done';
  let text: string;
  let color: string;
  if (returned) { text = `Чек возврата пробит${f?.fiscalDocumentNumber ? ` · ФД № ${f.fiscalDocumentNumber}` : ''}`; color = '#7B776F'; }
  else if (o.sold && f?.kind === 'sellReturn') { text = `Чек возврата не пробит${f.error ? `: ${f.error}` : ''}`; color = tokens.chili; }
  else if (o.sold) { text = `Чек пробит${f?.kind === 'sell' && f.fiscalDocumentNumber ? ` · ФД № ${f.fiscalDocumentNumber}` : ''}`; color = tokens.herb; }
  else if (f?.status === 'processing') { text = 'Чек печатается…'; color = '#C77700'; }
  else if (f) { text = `Чек не пробит${f.error ? `: ${f.error}` : ''}`; color = tokens.chili; }
  else { text = 'Чек не пробивался'; color = '#7B776F'; }

  const action = !o.sold && !cancelled ? { kind: 'sell' as const, label: f?.status === 'processing' ? 'Проверить' : 'Пробить чек' }
    : o.sold && !returned && cancelled ? { kind: 'return' as const, label: 'Чек возврата' }
      : null;

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
      <Typography variant="body2" sx={{ color, fontWeight: 600, flex: 1, minWidth: 0 }}>{text}</Typography>
      {action && (
        <Button size="small" onClick={() => onPrint(action.kind)} disabled={busy}
          startIcon={busy ? <CircularProgress size={14} color="inherit" /> : <PrintIcon />}>
          {action.label}
        </Button>
      )}
    </Box>
  );
};

const PosOrders: React.FC = () => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const [all, setAll] = React.useState(false);
  const [active, setActive] = React.useState(true);
  const { data: orders = [], isLoading } = usePosOrders(all);
  const setStatus = useSetOrderStatus();
  const qc = useQueryClient();
  const { data: fiscal } = useFiscalState(false);
  const [confirmCancel, setConfirmCancel] = React.useState<PosOrder | null>(null);
  const [withReturn, setWithReturn] = React.useState(true);
  const [printing, setPrinting] = React.useState<number | null>(null);
  const [error, setError] = React.useState('');

  const print = async (id: number, kind: 'sell' | 'return') => {
    setPrinting(id);
    setError('');
    try {
      const r = await (kind === 'sell' ? fiscalSell(id) : fiscalReturn(id));
      if (r.status !== 'done') setError(`Чек по заказу № ${id} не пробит${r.error ? `: ${r.error}` : ''}`);
    } catch (e) {
      setError(apiError(e));
    } finally {
      setPrinting(null);
      qc.invalidateQueries({ queryKey: ['pos'] });
      qc.invalidateQueries({ queryKey: ['fiscal'] });
    }
  };

  const cancel = async (o: PosOrder, refund: boolean) => {
    setConfirmCancel(null);
    if (await change(o.id, 'Отменён') && refund) await print(o.id, 'return');
  };

  const list = active ? orders.filter(o => !['Выполнен', 'Доставлен', 'Отменён'].includes(o.status)) : orders;

  const change = async (id: number, status: string) => {
    try { await setStatus.mutateAsync({ id, status }); return true; } catch (e) { setError(apiError(e)); return false; }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
        <ToggleButtonGroup exclusive size="small" value={active} onChange={(_, v) => v !== null && setActive(v)}>
          <ToggleButton value={true}>В работе</ToggleButton>
          <ToggleButton value={false}>Все за сегодня</ToggleButton>
        </ToggleButtonGroup>
        <FormControlLabel control={<Switch checked={all} onChange={e => setAll(e.target.checked)} />} label="И заказы с сайта" />
      </Box>
      {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}

      {isLoading ? (
        <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>
      ) : list.length === 0 ? (
        <Box sx={{ bgcolor: 'background.paper', borderRadius: '20px', py: 6, textAlign: 'center', color: 'text.secondary' }}>
          {active ? 'Нет заказов в работе' : 'Сегодня заказов ещё не было'}
        </Box>
      ) : (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(3, minmax(0, 1fr))' }, gap: 1.5 }}>
          {list.map(o => {
            const next = NEXT_STATUS[o.status];
            const color = STATUS_COLORS[o.status] ?? '#7B776F';
            return (
              <Box key={o.id} sx={{ bgcolor: 'background.paper', borderRadius: '20px', p: 2, display: 'flex', flexDirection: 'column', gap: 1 }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  <Typography sx={{ fontWeight: 800, fontSize: '1.3rem', flex: 1 }}>№ {o.id}</Typography>
                  <Chip size="small" label={o.status} sx={{ bgcolor: `${color}1f`, color, fontWeight: 700 }} />
                </Box>
                <Typography variant="body2" color="text.secondary">
                  {new Date(o.createdAt).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })}
                  {' · '}{o.deliveryType === 'Самовывоз' ? 'с собой' : o.deliveryType === 'В зале' ? 'в зале' : o.deliveryType.toLowerCase()}
                  {o.source !== 'pos' ? ' · сайт' : ''}
                  {o.customerName && o.customerName !== 'Гость' ? ` · ${o.customerName}` : ''}
                </Typography>
                {fiscal?.enabled && (o.paymentMethod === 'cash' || o.paymentMethod === 'card') && (
                  <FiscalLine order={o} busy={printing === o.id} onPrint={kind => print(o.id, kind)} />
                )}
                <Box sx={{ bgcolor: t.surfaceMuted, borderRadius: '12px', px: 1.5, py: 1 }}>
                  {o.items.map((i, idx) => (
                    <Typography key={idx} variant="body2">
                      <b>{i.quantity}×</b> {i.name}
                      {i.addons.length > 0 && <Box component="span" sx={{ color: 'text.secondary' }}> + {i.addons.map(a => a.addonName).join(', ')}</Box>}
                    </Typography>
                  ))}
                  {o.notes && <Typography variant="body2" sx={{ mt: 0.5, fontStyle: 'italic' }}>«{o.notes}»</Typography>}
                </Box>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 'auto' }}>
                  <Typography sx={{ fontWeight: 800, flex: 1 }}>
                    {money(o.total)}
                    <Typography component="span" variant="body2" color="text.secondary">
                      {' '}{o.paymentMethod === 'cash' ? 'нал' : o.paymentMethod === 'card' ? 'карта' : o.paymentMethod === 'check' ? 'стол, не оплачен' : o.paymentMethod === 'mixed' ? 'стол, раздельно' : 'онлайн'}
                    </Typography>
                  </Typography>
                  {!['Выполнен', 'Доставлен', 'Отменён'].includes(o.status) && (
                    <Button size="small" onClick={() => { setWithReturn(true); setConfirmCancel(o); }} sx={{ color: 'text.secondary' }}>Отменить</Button>
                  )}
                  {next && (
                    <Button variant="contained" size="small" onClick={() => change(o.id, next.status)} disabled={setStatus.isPending}>
                      {next.label}
                    </Button>
                  )}
                </Box>
              </Box>
            );
          })}
        </Box>
      )}

      <Dialog open={!!confirmCancel} onClose={() => setConfirmCancel(null)} maxWidth="xs" fullWidth>
        <DialogContent>
          <Typography sx={{ fontWeight: 700, fontSize: '1.1rem', mb: 1 }}>Отменить заказ № {confirmCancel?.id}?</Typography>
          <Typography color="text.secondary">
            Продукты вернутся на склад. Деньги гостю нужно вернуть вручную{confirmCancel?.paymentMethod === 'card' ? ' через терминал' : ''}.
          </Typography>
          {confirmCancel?.sold && (
            <FormControlLabel sx={{ mt: 1 }} control={<Checkbox checked={withReturn} onChange={e => setWithReturn(e.target.checked)} />} label="Пробить чек возврата" />
          )}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3 }}>
          <Button onClick={() => setConfirmCancel(null)} sx={{ color: 'text.secondary' }}>Нет</Button>
          <Button variant="contained" color="error" onClick={() => { if (confirmCancel) void cancel(confirmCancel, confirmCancel.sold && withReturn); }}>
            Отменить заказ
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};

// ============ Онлайн-касса ============

/** Итог чека после оплаты: пробит (ФД №) или ошибка с кнопкой «Повторить» */
const ReceiptState: React.FC<{ receipt: FiscalReceiptResult; busy: boolean; onRetry: () => void }> = ({ receipt, busy, onRetry }) => {
  if (receipt.status === 'done') {
    return (
      <Typography sx={{ mt: 1.5, color: tokens.herb, fontWeight: 600 }}>
        Чек пробит{receipt.fiscalDocumentNumber ? ` · ФД № ${receipt.fiscalDocumentNumber}` : ''}
        {receipt.clientContact ? ` · копия на ${receipt.clientContact}` : ''}
      </Typography>
    );
  }
  return (
    <Alert
      severity={receipt.status === 'processing' ? 'warning' : 'error'}
      sx={{ mt: 2, textAlign: 'left' }}
      action={
        <Button color="inherit" size="small" onClick={onRetry} disabled={busy} startIcon={busy ? <CircularProgress size={14} color="inherit" /> : <PrintIcon />}>
          {receipt.status === 'processing' ? 'Проверить' : 'Повторить'}
        </Button>
      }
    >
      {receipt.status === 'processing' ? 'Касса ещё печатает чек' : 'Чек не пробит'}{receipt.error ? `: ${receipt.error}` : ''}
    </Alert>
  );
};

/** Смена на кассе АТОЛ: состояние, открыть, X-отчёт, закрыть (Z-отчёт) */
const ShiftControl: React.FC = () => {
  const qc = useQueryClient();
  const { data } = useFiscalState(true);
  const [anchor, setAnchor] = React.useState<HTMLElement | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [confirmClose, setConfirmClose] = React.useState(false);
  const [message, setMessage] = React.useState<{ ok: boolean; text: string } | null>(null);

  if (!data?.enabled) return null;
  const state = data.shiftError ? 'error' : data.shiftState ?? 'unknown';
  const color = state === 'opened' ? tokens.herb : state === 'closed' ? '#7B776F' : tokens.chili;
  const label = data.shiftError ? 'Касса недоступна' : `${SHIFT_TEXT[state] ?? 'Смена'}${state === 'opened' && data.shiftNumber ? ` № ${data.shiftNumber}` : ''}`;

  const run = async (op: 'open' | 'close' | 'x') => {
    setAnchor(null);
    setConfirmClose(false);
    setBusy(true);
    setMessage(null);
    try {
      const r = await fiscalShift(op);
      qc.setQueryData(['fiscal', 'status', true], r);
      setMessage({ ok: true, text: op === 'open' ? 'Смена открыта' : op === 'close' ? 'Смена закрыта, Z-отчёт напечатан' : 'X-отчёт напечатан' });
    } catch (e) {
      setMessage({ ok: false, text: apiError(e) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <Chip
        icon={busy ? <CircularProgress size={14} /> : <PrintIcon />}
        label={label}
        onClick={e => setAnchor(e.currentTarget)}
        sx={{ bgcolor: `${color}1f`, color, fontWeight: 700, '& .MuiChip-icon': { color } }}
      />
      <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}>
        {data.shiftError && <MenuItem disabled sx={{ whiteSpace: 'normal', maxWidth: 320 }}>{data.shiftError}</MenuItem>}
        <MenuItem onClick={() => run('open')} disabled={state === 'opened'}>Открыть смену</MenuItem>
        <MenuItem onClick={() => run('x')}>X-отчёт</MenuItem>
        <MenuItem onClick={() => { setAnchor(null); setConfirmClose(true); }} disabled={state === 'closed'}>Закрыть смену (Z-отчёт)</MenuItem>
        <MenuItem onClick={() => { setAnchor(null); qc.invalidateQueries({ queryKey: ['fiscal'] }); }}>Обновить состояние</MenuItem>
      </Menu>
      <Dialog open={confirmClose} onClose={() => setConfirmClose(false)} maxWidth="xs" fullWidth>
        <DialogContent>
          <Typography sx={{ fontWeight: 700, fontSize: '1.1rem', mb: 1 }}>Закрыть смену на кассе?</Typography>
          <Typography color="text.secondary">Касса напечатает Z-отчёт. Новые чеки откроют следующую смену.</Typography>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3 }}>
          <Button onClick={() => setConfirmClose(false)} sx={{ color: 'text.secondary' }}>Нет</Button>
          <Button variant="contained" color="secondary" onClick={() => run('close')}>Закрыть смену</Button>
        </DialogActions>
      </Dialog>
      <Snackbar open={!!message} autoHideDuration={5000} onClose={() => setMessage(null)} anchorOrigin={{ vertical: 'top', horizontal: 'center' }}>
        <Alert severity={message?.ok ? 'success' : 'error'} onClose={() => setMessage(null)}>{message?.text}</Alert>
      </Snackbar>
    </>
  );
};

// ============ Страница ============

/**
 * Касса: заказы у стойки — в зале и с собой. Чек → оплата → заказ сразу на кухне.
 */
const PosPage: React.FC = () => {
  useWorkScreen();
  const theme = useTheme();
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'));
  const isFullscreen = useIsFullscreen();
  const { data: summary } = usePosSummary();
  const [tab, setTab] = React.useState<'new' | 'orders'>('new');
  const [lines, setLines] = React.useState<TicketLine[]>([]);
  const [picking, setPicking] = React.useState<Dish | null>(null);
  const [ticketOpen, setTicketOpen] = React.useState(false);
  const [done, setDone] = React.useState<{ id: number; change: number | null; fiscal: FiscalReceiptResult | null } | null>(null);
  const [retrying, setRetrying] = React.useState(false);
  const qc = useQueryClient();
  const [toast, setToast] = React.useState('');

  const addLine = (card: Dish, child: Dish, quantity: number, addons: SelectedAddon[], comment: string, weight?: number) => {
    const key = lineKey(child.id, addons, comment, weight);
    const baseVariant = card.children && card.children.length > 1 ? child.name : child.name !== 'Стандарт' && child.name !== card.name ? child.name : null;
    setLines(prev => {
      const existing = prev.find(l => l.key === key);
      if (existing) return prev.map(l => (l.key === key ? { ...l, quantity: l.quantity + quantity } : l));
      return [...prev, {
        key,
        dishId: child.id,
        title: card.name,
        variant: weight ? [baseVariant, weightLabel(weight)].filter(Boolean).join(', ') : baseVariant,
        unitPrice: weight ? priceForWeight(child.price, weight) : child.price,
        weight,
        quantity,
        addons,
        comment,
        allowed: allowedOrderTypes(card, child),
      }];
    });
    setToast(`${card.name} — в чеке`);
  };

  const pick = (card: Dish) => {
    const children = card.children ?? [];
    // Один вариант и нет добавок — сразу в чек, без лишних окон
    if (children.length === 1 && !hasAddons(card) && !hasAddons(children[0]) && !isWeightedDish(card, children[0])) {
      addLine(card, children[0], 1, [], '');
      return;
    }
    setPicking(card);
  };

  const total = lines.reduce((s, l) => s + lineTotal(l), 0);
  const count = lines.reduce((s, l) => s + l.quantity, 0);

  const ticket = (
    <Ticket
      lines={lines}
      onChange={setLines}
      onDone={(id, change, fiscal) => { setDone({ id, change, fiscal }); setLines([]); setTicketOpen(false); }}
    />
  );

  return (
    <Box sx={{ py: { xs: 1.5, sm: 2 }, pb: { xs: lines.length ? 12 : 2, md: 2 } }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 2, flexWrap: 'wrap' }}>
        <Typography variant="h4" component="h1" sx={{ fontSize: { xs: '1.6rem', sm: '2rem' } }}>Касса</Typography>
        <ToggleButtonGroup exclusive size="small" value={tab} onChange={(_, v) => v && setTab(v)}>
          <ToggleButton value="new">Новый заказ</ToggleButton>
          <ToggleButton value="orders">Заказы смены</ToggleButton>
        </ToggleButtonGroup>
        {summary && (
          <Box sx={{ ml: { md: 'auto' }, display: 'flex', gap: 2, color: 'text.secondary', fontSize: '0.9rem', flexWrap: 'wrap' }}>
            <span>Сегодня: <b style={{ color: theme.palette.text.primary }}>{summary.count}</b> заказов</span>
            <span>Выручка <b style={{ color: theme.palette.text.primary }}>{money(summary.total)}</b></span>
            <span>нал {money(summary.cash)} · карта {money(summary.card)}</span>
          </Box>
        )}
        <Box sx={{ ml: summary ? 0 : 'auto', display: 'flex', gap: 1, alignItems: 'center' }}><ShiftControl /><FullscreenButton /></Box>
      </Box>

      {tab === 'orders' ? <PosOrders /> : (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'minmax(0, 1fr) 380px' }, gap: 2, alignItems: 'start' }}>
          <PosMenu onPick={pick} />
          {isDesktop && (
            <Box sx={{ position: 'sticky', top: isFullscreen ? 16 : 84, height: isFullscreen ? 'calc(100vh - 32px)' : 'calc(100vh - 100px)', bgcolor: 'background.paper', borderRadius: '24px', overflow: 'hidden' }}>
              {ticket}
            </Box>
          )}
        </Box>
      )}

      {/* Телефон/планшет: чек снизу */}
      {!isDesktop && tab === 'new' && lines.length > 0 && (
        <Box sx={{ position: 'fixed', left: 0, right: 0, bottom: 0, p: 1.5, pb: 'calc(12px + env(safe-area-inset-bottom))', zIndex: theme.zIndex.appBar, bgcolor: 'background.paper', borderTop: '1px solid', borderColor: 'divider' }}>
          <Button fullWidth variant="contained" color="secondary" size="large" onClick={() => setTicketOpen(true)} sx={{ minHeight: 52 }}>
            Чек · {count} поз. · {money(total)}
          </Button>
        </Box>
      )}
      {!isDesktop && (
        <Drawer anchor="bottom" open={ticketOpen} onClose={() => setTicketOpen(false)} PaperProps={{ sx: { height: '92vh', borderTopLeftRadius: 24, borderTopRightRadius: 24 } }}>
          {ticket}
        </Drawer>
      )}

      <ProductModal
        open={!!picking}
        product={picking}
        onClose={() => setPicking(null)}
        onAddToCart={(product, quantity, addons, instructions, child, weight) => {
          addLine(product, child ?? product, quantity, addons, instructions, weight);
        }}
      />

      {/* Заказ принят */}
      <Dialog open={!!done} onClose={() => setDone(null)} maxWidth="xs" fullWidth>
        <DialogContent sx={{ textAlign: 'center', pt: 4 }}>
          <CheckIcon sx={{ fontSize: 64, color: tokens.herb }} />
          <Typography sx={{ fontWeight: 800, fontSize: '1.6rem', mt: 1 }}>Заказ № {done?.id}</Typography>
          <Typography color="text.secondary">принят и передан на кухню</Typography>
          {done?.fiscal && <ReceiptState
            receipt={done.fiscal}
            busy={retrying}
            onRetry={async () => {
              if (!done) return;
              setRetrying(true);
              try {
                const r = await fiscalSell(done.id);
                setDone({ ...done, fiscal: r });
              } catch (e) {
                setDone({ ...done, fiscal: { status: 'error', error: apiError(e) } });
              } finally {
                setRetrying(false);
                qc.invalidateQueries({ queryKey: ['pos'] });
              }
            }}
          />}
          {done?.change !== null && done?.change !== undefined && done.change > 0 && (
            <Box sx={{ mt: 2.5, bgcolor: 'rgba(255,201,51,0.25)', borderRadius: '16px', py: 2 }}>
              <Typography color="text.secondary">Сдача</Typography>
              <Typography sx={{ fontWeight: 800, fontSize: '2rem' }}>{money(done.change)}</Typography>
            </Box>
          )}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3 }}>
          <Button fullWidth variant="contained" color="secondary" size="large" autoFocus onClick={() => setDone(null)}>
            Новый заказ
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar
        open={!!toast}
        autoHideDuration={1200}
        onClose={() => setToast('')}
        message={toast}
        anchorOrigin={{ vertical: 'top', horizontal: 'center' }}
      />
    </Box>
  );
};

export default PosPage;
