import * as React from 'react';
import {
  Box, Typography, Button, IconButton, Chip, TextField, InputAdornment, CircularProgress, Alert, Drawer,
  Dialog, DialogTitle, DialogContent, DialogActions, ToggleButtonGroup, ToggleButton, Switch, alpha, useTheme,
} from '@mui/material';
import {
  ArrowBack as BackIcon, Add as AddIcon, Remove as RemoveIcon, Search as SearchIcon, Close as CloseIcon,
  TableRestaurant as TableIcon, CheckCircle as ReadyIcon, Schedule as CookingIcon, DoneAll as ServedIcon,
  SettingsOutlined as SettingsIcon, DeleteOutline as DeleteIcon,
} from '@mui/icons-material';
import { useNavigate, useParams } from 'react-router-dom';
import { useShawarmas } from '../api/hooks';
import {
  useWaiterTables, useTableCheck, useOpenCheck, useAddRound, useCancelRound, useCloseCheck, useCancelCheck,
  useAllTables, useSaveTables, type WaiterTable, type CheckOrder, type TableSetting,
} from '../api/waiter';
import { apiError } from '../api/inventory';
import { useEventStream, useInvalidateOnOrderEvent } from '../hooks/useOrderEvents';
import { useQueryClient } from '@tanstack/react-query';
import ProductModal from '../components/ProductModal';
import { useCan, P } from '../auth/permissions';
import { resolveMediaUrl } from '../utils/media';
import { allowedOrderTypes, isAllowedFor } from '../utils/orderTypes';
import { plural } from '../components/CartPanel';
import type { Shawarma, SelectedAddon } from '../types';
import { tokens } from '../theme';

const money = (v: number) => `${Math.round(v * 100) / 100} ₽`.replace('.', ',');
const DINE_IN = 'В зале';
const GUEST_FORMS: [string, string, string] = ['гость', 'гостя', 'гостей'];

const sinceText = (iso: string) => {
  const min = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 60000));
  return min < 60 ? `${min} мин` : `${Math.floor(min / 60)} ч ${min % 60} мин`;
};

/** Обновлять экран официанта по событиям заказов (кухня отметила готовность) */
const useLiveWaiter = () => {
  const qc = useQueryClient();
  const invalidate = useInvalidateOnOrderEvent();
  useEventStream('/api/events/orders', evt => {
    invalidate(evt);
    qc.invalidateQueries({ queryKey: ['waiter'] });
  });
};

// ==================== Столы ====================

const TablesView: React.FC = () => {
  const theme = useTheme();
  const navigate = useNavigate();
  const can = useCan();
  const { data: tables = [], isLoading, error } = useWaiterTables();
  const open = useOpenCheck();
  const [mine, setMine] = React.useState(false);
  const [opening, setOpening] = React.useState<WaiterTable | null>(null);
  const [guests, setGuests] = React.useState(2);
  const [settings, setSettings] = React.useState(false);
  const [err, setErr] = React.useState('');

  const shown = mine ? tables.filter(t => t.check?.isMine) : tables;
  const busy = tables.filter(t => t.check).length;

  const tap = (t: WaiterTable) => {
    if (t.check) navigate(`/waiter/${t.check.id}`);
    else { setGuests(2); setErr(''); setOpening(t); }
  };

  const confirmOpen = async () => {
    if (!opening) return;
    try {
      const r = await open.mutateAsync({ tableId: opening.id, guests });
      setOpening(null);
      navigate(`/waiter/${r.id}`);
    } catch (e) { setErr(apiError(e)); }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <Typography variant="h5" sx={{ fontWeight: 800, flex: 1 }}>Столы</Typography>
        <Typography variant="body2" color="text.secondary">занято {busy} из {tables.length}</Typography>
        {can(P.Settings) && <IconButton onClick={() => setSettings(true)} aria-label="Настроить столы"><SettingsIcon /></IconButton>}
      </Box>
      <Box sx={{ display: 'flex', gap: 1 }}>
        <Chip label="Все" color={!mine ? 'primary' : 'default'} onClick={() => setMine(false)} />
        <Chip label="Мои" color={mine ? 'primary' : 'default'} onClick={() => setMine(true)} />
      </Box>
      {error && <Alert severity="error">{apiError(error, 'Не удалось загрузить столы')}</Alert>}
      {isLoading ? <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box> : (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(3, minmax(0, 1fr))', sm: 'repeat(4, minmax(0, 1fr))', md: 'repeat(6, minmax(0, 1fr))' }, gap: 1 }}>
          {shown.map(t => {
            const c = t.check;
            const color = !c ? theme.palette.divider : c.ready > 0 ? tokens.herb : c.isMine ? tokens.saffron : theme.palette.text.secondary;
            return (
              <Box key={t.id} component="button" type="button" onClick={() => tap(t)}
                sx={{
                  border: '2px solid', borderColor: color, borderRadius: '16px', p: 1.25, minHeight: 92,
                  bgcolor: c ? alpha(color, 0.1) : 'background.paper', color: 'text.primary', font: 'inherit', textAlign: 'left',
                  display: 'flex', flexDirection: 'column', gap: 0.25, cursor: 'pointer', '&:active': { transform: 'scale(0.97)' },
                }}>
                <Typography sx={{ fontWeight: 800, fontSize: '1.4rem', lineHeight: 1.1 }}>{t.name}</Typography>
                {c ? (
                  <>
                    <Typography sx={{ fontWeight: 700, fontSize: '0.9rem' }}>{money(c.total)}</Typography>
                    <Typography variant="caption" color="text.secondary" noWrap>{c.isMine ? 'мой' : c.waiter} · {sinceText(c.openedAt)}</Typography>
                    {c.ready > 0 && <Typography variant="caption" sx={{ color: tokens.herb, fontWeight: 800 }}>готово — нести</Typography>}
                  </>
                ) : (
                  <Typography variant="caption" color="text.secondary">свободен</Typography>
                )}
              </Box>
            );
          })}
        </Box>
      )}

      <Dialog open={!!opening} onClose={() => setOpening(null)} maxWidth="xs" fullWidth>
        <DialogTitle>Стол {opening?.name}</DialogTitle>
        <DialogContent>
          <Typography color="text.secondary" sx={{ mb: 1.5 }}>Сколько гостей?</Typography>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 2 }}>
            <IconButton onClick={() => setGuests(g => Math.max(1, g - 1))} sx={{ bgcolor: 'action.hover' }} aria-label="Меньше"><RemoveIcon /></IconButton>
            <Typography sx={{ fontWeight: 800, fontSize: '2rem', minWidth: 48, textAlign: 'center' }}>{guests}</Typography>
            <IconButton onClick={() => setGuests(g => Math.min(50, g + 1))} sx={{ bgcolor: 'action.hover' }} aria-label="Больше"><AddIcon /></IconButton>
          </Box>
          {err && <Alert severity="error" sx={{ mt: 2 }}>{err}</Alert>}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3 }}>
          <Button onClick={() => setOpening(null)} sx={{ color: 'text.secondary' }}>Отмена</Button>
          <Button variant="contained" color="secondary" size="large" onClick={confirmOpen} disabled={open.isPending}>Открыть чек</Button>
        </DialogActions>
      </Dialog>
      {settings && <TablesSettings onClose={() => setSettings(false)} />}
    </Box>
  );
};

/** Настройка столов (администратор): названия, порядок, выключить */
const TablesSettings: React.FC<{ onClose: () => void }> = ({ onClose }) => {
  const { data } = useAllTables(true);
  const save = useSaveTables();
  const [list, setList] = React.useState<TableSetting[]>([]);
  const [err, setErr] = React.useState('');
  React.useEffect(() => { if (data) setList(data.map(t => ({ ...t }))); }, [data]);

  const submit = async () => {
    setErr('');
    try { await save.mutateAsync(list.map(t => ({ ...t, name: t.name.trim() }))); onClose(); } catch (e) { setErr(apiError(e)); }
  };

  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>Столы</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1, pt: '8px !important' }}>
        {list.map((t, i) => (
          <Box key={t.id ?? `new-${i}`} sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
            <TextField size="small" value={t.name} onChange={e => setList(l => l.map((x, k) => (k === i ? { ...x, name: e.target.value } : x)))}
              sx={{ flex: 1 }} slotProps={{ htmlInput: { maxLength: 40 } }} />
            <Switch checked={t.isActive} onChange={e => setList(l => l.map((x, k) => (k === i ? { ...x, isActive: e.target.checked } : x)))} />
            {!t.id && <IconButton size="small" onClick={() => setList(l => l.filter((_, k) => k !== i))} aria-label="Убрать"><DeleteIcon fontSize="small" /></IconButton>}
          </Box>
        ))}
        <Button startIcon={<AddIcon />} onClick={() => setList(l => [...l, { name: String(l.length + 1), isActive: true }])} sx={{ alignSelf: 'flex-start' }}>
          Добавить стол
        </Button>
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button onClick={onClose} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" onClick={submit} disabled={save.isPending}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

// ==================== Выбор блюд ====================

interface DraftLine {
  key: string;
  shawarmaId: number;
  title: string;
  unitPrice: number;
  quantity: number;
  addons: SelectedAddon[];
  comment: string;
}

const lineTotal = (l: DraftLine) => (l.unitPrice + l.addons.reduce((s, a) => s + a.price * a.quantity, 0)) * l.quantity;
const hasAddons = (s: Shawarma) => ((s as unknown as { addons?: unknown[] }).addons?.length ?? 0) > 0;

const MenuPicker: React.FC<{ open: boolean; tableName: string; onClose: () => void; onSend: (lines: DraftLine[], comment: string) => Promise<void> }> = ({ open, tableName, onClose, onSend }) => {
  const { data: menu = [] } = useShawarmas();
  const [search, setSearch] = React.useState('');
  const [section, setSection] = React.useState('');
  const [lines, setLines] = React.useState<DraftLine[]>([]);
  const [comment, setComment] = React.useState('');
  const [picking, setPicking] = React.useState<Shawarma | null>(null);
  const [showDraft, setShowDraft] = React.useState(false);
  const [sending, setSending] = React.useState(false);
  const [err, setErr] = React.useState('');

  React.useEffect(() => { if (open) { setLines([]); setComment(''); setErr(''); setShowDraft(false); } }, [open]);

  const cards = React.useMemo(() => menu
    .filter(i => i.parentId == null && i.isAvailable)
    .map(i => ({ ...i, children: (i.children ?? []).filter(c => c.isAvailable && isAllowedFor(allowedOrderTypes(i, c), DINE_IN)) }))
    .filter(i => i.children.length > 0), [menu]);
  const sections = React.useMemo(() => [...new Set(cards.map(c => c.category?.trim() || 'Меню'))], [cards]);
  const q = search.trim().toLowerCase();
  const visible = cards.filter(c => (!section || (c.category?.trim() || 'Меню') === section)
    && (!q || c.name.toLowerCase().includes(q) || c.children.some(ch => ch.name.toLowerCase().includes(q))));

  const add = (card: Shawarma, child: Shawarma, quantity: number, addons: SelectedAddon[], cmt: string) => {
    const key = `${child.id}|${addons.map(a => `${a.addonId}x${a.quantity}`).sort().join(',')}|${cmt}`;
    const title = card.children && card.children.length > 1 && child.name !== card.name ? `${card.name} ${child.name}` : card.name;
    setLines(prev => prev.some(l => l.key === key)
      ? prev.map(l => (l.key === key ? { ...l, quantity: l.quantity + quantity } : l))
      : [...prev, { key, shawarmaId: child.id, title, unitPrice: child.price, quantity, addons, comment: cmt }]);
  };

  const pick = (card: Shawarma & { children: Shawarma[] }) => {
    if (card.children.length === 1 && !hasAddons(card) && !hasAddons(card.children[0])) add(card, card.children[0], 1, [], '');
    else setPicking(card);
  };

  const count = lines.reduce((s, l) => s + l.quantity, 0);
  const total = lines.reduce((s, l) => s + lineTotal(l), 0);
  const qtyOf = (cardId: number) => lines.filter(l => cards.find(c => c.id === cardId)?.children.some(ch => ch.id === l.shawarmaId)).reduce((s, l) => s + l.quantity, 0);

  const send = async () => {
    setSending(true);
    setErr('');
    try { await onSend(lines, comment); onClose(); } catch (e) { setErr(apiError(e)); setShowDraft(true); } finally { setSending(false); }
  };

  return (
    <Drawer anchor="bottom" open={open} onClose={onClose} PaperProps={{ sx: { height: '100dvh', bgcolor: 'background.default' } }}>
      <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, px: 1.5, pt: 1.5, pb: 1 }}>
          <IconButton onClick={onClose} aria-label="Закрыть"><CloseIcon /></IconButton>
          <Typography sx={{ fontWeight: 800, fontSize: '1.15rem', flex: 1 }}>Стол {tableName} · добавить</Typography>
        </Box>
        <Box sx={{ px: 1.5, display: 'flex', flexDirection: 'column', gap: 1 }}>
          <TextField size="small" placeholder="Поиск" value={search} onChange={e => setSearch(e.target.value)}
            sx={{ '& .MuiOutlinedInput-root': { bgcolor: 'background.paper' } }}
            slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon sx={{ color: 'text.secondary' }} /></InputAdornment> } }} />
          <Box sx={{ display: 'flex', gap: 0.75, overflowX: 'auto', pb: 0.5, scrollbarWidth: 'none', '&::-webkit-scrollbar': { display: 'none' } }}>
            <Chip label="Все" color={!section ? 'primary' : 'default'} onClick={() => setSection('')} sx={{ flexShrink: 0 }} />
            {sections.map(s => <Chip key={s} label={s} color={section === s ? 'primary' : 'default'} onClick={() => setSection(section === s ? '' : s)} sx={{ flexShrink: 0 }} />)}
          </Box>
        </Box>
        <Box sx={{ flex: 1, overflowY: 'auto', px: 1.5, pb: 12 }}>
          {visible.map(card => {
            const img = card.primaryImage || card.images?.find(i => i.isPrimary)?.filePath || card.images?.[0]?.filePath;
            const min = Math.min(...card.children.map(c => c.price));
            const n = qtyOf(card.id);
            return (
              <Box key={card.id} component="button" type="button" onClick={() => pick(card)}
                sx={{ display: 'flex', alignItems: 'center', gap: 1.25, width: '100%', p: 1, mb: 0.75, border: 0, borderRadius: '14px',
                  bgcolor: 'background.paper', color: 'text.primary', font: 'inherit', textAlign: 'left', cursor: 'pointer', '&:active': { transform: 'scale(0.99)' } }}>
                <Box sx={{ width: 44, height: 44, borderRadius: '10px', overflow: 'hidden', bgcolor: 'action.hover', flexShrink: 0 }}>
                  {img && <Box component="img" src={resolveMediaUrl(img)} alt="" loading="lazy" sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />}
                </Box>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography sx={{ fontWeight: 600, fontSize: '0.95rem', lineHeight: 1.2 }} noWrap>{card.name}</Typography>
                  <Typography variant="body2" color="text.secondary">{card.children.length > 1 ? 'от ' : ''}{money(min)}</Typography>
                </Box>
                {n > 0 ? <Chip size="small" color="secondary" label={n} sx={{ fontWeight: 800 }} /> : <AddIcon sx={{ color: 'text.secondary' }} />}
              </Box>
            );
          })}
        </Box>

        {/* Набранное — снизу, можно раскрыть */}
        {lines.length > 0 && (
          <Box sx={{ position: 'absolute', left: 0, right: 0, bottom: 0, bgcolor: 'background.paper', borderTop: '1px solid', borderColor: 'divider',
            p: 1.5, pb: 'calc(12px + env(safe-area-inset-bottom))', maxHeight: '70dvh', display: 'flex', flexDirection: 'column', gap: 1, borderTopLeftRadius: 20, borderTopRightRadius: 20 }}>
            {showDraft && (
              <Box sx={{ overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 0.75 }}>
                {lines.map(l => (
                  <Box key={l.key} sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Box sx={{ flex: 1, minWidth: 0 }}>
                      <Typography sx={{ fontWeight: 600, fontSize: '0.92rem' }} noWrap>{l.title}</Typography>
                      {(l.addons.length > 0 || l.comment) && (
                        <Typography variant="caption" color="text.secondary" noWrap component="div">
                          {[l.addons.map(a => a.addonName).join(', '), l.comment].filter(Boolean).join(' · ')}
                        </Typography>
                      )}
                    </Box>
                    <IconButton size="small" onClick={() => setLines(p => p.flatMap(x => (x.key === l.key ? (x.quantity > 1 ? [{ ...x, quantity: x.quantity - 1 }] : []) : [x])))} aria-label="Меньше"><RemoveIcon fontSize="small" /></IconButton>
                    <Typography sx={{ fontWeight: 800, minWidth: 18, textAlign: 'center' }}>{l.quantity}</Typography>
                    <IconButton size="small" onClick={() => setLines(p => p.map(x => (x.key === l.key ? { ...x, quantity: x.quantity + 1 } : x)))} aria-label="Больше"><AddIcon fontSize="small" /></IconButton>
                  </Box>
                ))}
                <TextField size="small" placeholder="Комментарий для кухни" value={comment} onChange={e => setComment(e.target.value)} />
              </Box>
            )}
            {err && <Alert severity="error">{err}</Alert>}
            <Box sx={{ display: 'flex', gap: 1 }}>
              <Button onClick={() => setShowDraft(s => !s)} sx={{ color: 'text.secondary', whiteSpace: 'nowrap' }}>
                {showDraft ? 'Скрыть' : `${count} ${plural(count, ['позиция', 'позиции', 'позиций'])}`}
              </Button>
              <Button fullWidth variant="contained" color="secondary" size="large" onClick={send} disabled={sending}
                startIcon={sending ? <CircularProgress size={18} color="inherit" /> : undefined}>
                На кухню · {money(total)}
              </Button>
            </Box>
          </Box>
        )}
      </Box>
      <ProductModal open={!!picking} product={picking} onClose={() => setPicking(null)}
        onAddToCart={(product, quantity, addons, instructions, child) => add(product, child ?? product, quantity, addons, instructions)} />
    </Drawer>
  );
};

// ==================== Чек ====================

const roundStatus = (o: CheckOrder) => {
  if (o.status === 'Отменён') return { text: 'отменён', color: tokens.chili };
  if (o.status === 'Новый') return { text: 'отправлен на кухню', color: '#2563EB' };
  if (o.status === 'Принят' || o.status === 'Готовится') return { text: 'готовится', color: '#C77700' };
  if (o.status === 'Готов') return { text: 'готово — нести', color: tokens.herb };
  return { text: 'отдано', color: '#7B776F' };
};

const ItemState: React.FC<{ ready: number; served: number; qty: number; cancelled: boolean }> = ({ ready, served, qty, cancelled }) => {
  if (cancelled) return null;
  if (served >= qty) return <ServedIcon fontSize="small" sx={{ color: 'text.disabled' }} titleAccess="отдано" />;
  if (ready > served) return <ReadyIcon fontSize="small" sx={{ color: tokens.herb }} titleAccess="готово" />;
  return <CookingIcon fontSize="small" sx={{ color: 'text.disabled' }} titleAccess="готовится" />;
};

const CheckView: React.FC<{ id: number }> = ({ id }) => {
  const navigate = useNavigate();
  const { data: check, isLoading, error } = useTableCheck(id);
  const addRound = useAddRound();
  const cancelRound = useCancelRound();
  const close = useCloseCheck();
  const cancelCheck = useCancelCheck();
  const [picker, setPicker] = React.useState(false);
  const [closing, setClosing] = React.useState(false);
  const [method, setMethod] = React.useState<'cash' | 'card'>('card');
  const [cash, setCash] = React.useState('');
  const [done, setDone] = React.useState<{ total: number; change: number | null } | null>(null);
  const [err, setErr] = React.useState('');

  if (isLoading) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;
  if (error || !check) return <Alert severity="error">{apiError(error, 'Чек не найден')}</Alert>;

  const isOpen = check.status === 'open';
  const active = check.orders.filter(o => o.status !== 'Отменён');
  const received = Number(cash.replace(',', '.')) || 0;
  const change = method === 'cash' && received > 0 ? received - check.total : null;

  const send = async (lines: DraftLine[], comment: string) => {
    await addRound.mutateAsync({
      checkId: check.id,
      comment: comment.trim() || null,
      items: lines.map(l => ({ shawarmaId: l.shawarmaId, quantity: l.quantity, addons: l.addons.map(a => ({ addonId: a.addonId, quantity: a.quantity })), comment: l.comment || undefined })),
    });
  };

  const doClose = async () => {
    setErr('');
    try {
      const r = await close.mutateAsync({ checkId: check.id, paymentMethod: method, cashReceived: method === 'cash' && received > 0 ? received : null });
      setClosing(false);
      setDone({ total: r.total, change: r.change });
    } catch (e) { setErr(apiError(e)); }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, pb: isOpen ? 12 : 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <IconButton onClick={() => navigate('/waiter')} aria-label="К столам"><BackIcon /></IconButton>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography sx={{ fontWeight: 800, fontSize: '1.4rem', lineHeight: 1.1 }}>Стол {check.table.name}</Typography>
          <Typography variant="body2" color="text.secondary" noWrap>
            {check.guests ? `${check.guests} ${plural(check.guests, GUEST_FORMS)} · ` : ''}{check.isMine ? 'ваш стол' : check.waiter} · {sinceText(check.openedAt)}
          </Typography>
        </Box>
        <Typography sx={{ fontWeight: 800, fontSize: '1.3rem' }}>{money(check.total)}</Typography>
      </Box>

      {!isOpen && <Alert severity={check.status === 'closed' ? 'success' : 'info'}>{check.status === 'closed' ? `Чек закрыт · ${check.paymentMethod === 'cash' ? 'наличные' : 'карта'}` : 'Чек отменён'}</Alert>}

      {check.orders.length === 0 && (
        <Box sx={{ bgcolor: 'background.paper', borderRadius: '16px', p: 3, textAlign: 'center' }}>
          <TableIcon sx={{ fontSize: 40, color: 'text.secondary' }} />
          <Typography sx={{ fontWeight: 700 }}>Пока пусто</Typography>
          <Typography variant="body2" color="text.secondary">Нажмите «Добавить блюда» — заказ уйдёт на кухню</Typography>
        </Box>
      )}

      {check.orders.map((o, idx) => {
        const st = roundStatus(o);
        const cancelled = o.status === 'Отменён';
        return (
          <Box key={o.id} sx={{ bgcolor: 'background.paper', borderRadius: '16px', p: 1.5, opacity: cancelled ? 0.5 : 1 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.75 }}>
              <Typography sx={{ fontWeight: 700, flex: 1 }}>
                {idx + 1}-я подача <Typography component="span" variant="body2" color="text.secondary">· №{o.id} · {new Date(o.createdAt).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })}</Typography>
              </Typography>
              <Chip size="small" label={st.text} sx={{ bgcolor: alpha(st.color, 0.14), color: st.color, fontWeight: 700 }} />
            </Box>
            {o.items.map(i => (
              <Box key={i.id} sx={{ display: 'flex', alignItems: 'center', gap: 1, py: 0.25 }}>
                <ItemState ready={i.readyQuantity} served={i.servedQuantity} qty={i.quantity} cancelled={cancelled} />
                <Typography sx={{ flex: 1, fontSize: '0.95rem', textDecoration: cancelled ? 'line-through' : 'none' }}>
                  {i.quantity} × {i.name}
                  {i.addons.length > 0 && <Typography component="span" variant="body2" color="text.secondary"> + {i.addons.map(a => a.addonName).join(', ')}</Typography>}
                </Typography>
                {i.quantity > 1 && i.readyQuantity > 0 && i.servedQuantity < i.quantity && (
                  <Typography variant="caption" sx={{ color: tokens.herb, fontWeight: 700 }}>готово {i.readyQuantity}/{i.quantity}</Typography>
                )}
              </Box>
            ))}
            {o.notes && <Typography variant="body2" color="text.secondary" sx={{ fontStyle: 'italic', mt: 0.5 }}>«{o.notes}»</Typography>}
            {isOpen && (o.status === 'Новый' || o.status === 'Принят') && (
              <Button size="small" color="error" onClick={() => cancelRound.mutate({ checkId: check.id, orderId: o.id })} disabled={cancelRound.isPending} sx={{ mt: 0.5 }}>
                Отменить подачу
              </Button>
            )}
          </Box>
        );
      })}

      {isOpen && (
        <Box sx={{ position: 'fixed', left: 0, right: 0, bottom: 0, p: 1.5, pb: 'calc(12px + env(safe-area-inset-bottom))', bgcolor: 'background.paper',
          borderTop: '1px solid', borderColor: 'divider', display: 'flex', gap: 1, zIndex: 10 }}>
          <Button fullWidth variant="contained" color="secondary" size="large" startIcon={<AddIcon />} onClick={() => setPicker(true)}>Добавить блюда</Button>
          {active.length > 0 ? (
            <Button fullWidth variant="outlined" size="large" onClick={() => { setErr(''); setCash(''); setClosing(true); }}>Закрыть чек</Button>
          ) : (
            <Button variant="text" color="error" onClick={async () => { try { await cancelCheck.mutateAsync(check.id); navigate('/waiter'); } catch (e) { setErr(apiError(e)); } }}>
              Отменить
            </Button>
          )}
        </Box>
      )}
      {err && !closing && <Alert severity="error">{err}</Alert>}

      <MenuPicker open={picker} tableName={check.table.name} onClose={() => setPicker(false)} onSend={send} />

      <Dialog open={closing} onClose={() => setClosing(false)} maxWidth="xs" fullWidth>
        <DialogTitle>Оплата · стол {check.table.name}</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <Typography sx={{ fontWeight: 800, fontSize: '2rem', textAlign: 'center' }}>{money(check.total)}</Typography>
          {check.notServed > 0 && (
            <Alert severity="warning">Ещё не отдано {check.notServed} {plural(check.notServed, ['блюдо', 'блюда', 'блюд'])} — проверьте перед оплатой</Alert>
          )}
          <ToggleButtonGroup exclusive fullWidth value={method} onChange={(_, v) => v && setMethod(v)}>
            <ToggleButton value="card">Карта</ToggleButton>
            <ToggleButton value="cash">Наличные</ToggleButton>
          </ToggleButtonGroup>
          {method === 'cash' && (
            <TextField label="Получено" value={cash} onChange={e => setCash(e.target.value)} inputMode="decimal"
              helperText={change === null ? 'Можно не заполнять' : change < 0 ? `Не хватает ${money(-change)}` : `Сдача ${money(change)}`}
              error={change !== null && change < 0}
              slotProps={{ input: { endAdornment: <InputAdornment position="end">₽</InputAdornment> } }} />
          )}
          {err && <Alert severity="error">{err}</Alert>}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3 }}>
          <Button onClick={() => setClosing(false)} sx={{ color: 'text.secondary' }}>Назад</Button>
          <Button variant="contained" color="secondary" size="large" onClick={doClose} disabled={close.isPending || (change !== null && change < 0)}>Оплачено</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={!!done} onClose={() => navigate('/waiter')} maxWidth="xs" fullWidth>
        <DialogContent sx={{ textAlign: 'center', pt: 4 }}>
          <ReadyIcon sx={{ fontSize: 56, color: tokens.herb }} />
          <Typography sx={{ fontWeight: 800, fontSize: '1.4rem' }}>Чек закрыт</Typography>
          <Typography color="text.secondary">{money(done?.total ?? 0)}</Typography>
          {done?.change != null && done.change > 0 && (
            <Typography sx={{ fontWeight: 800, fontSize: '1.6rem', mt: 1.5 }}>Сдача {money(done.change)}</Typography>
          )}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3 }}>
          <Button fullWidth variant="contained" color="secondary" size="large" onClick={() => navigate('/waiter')}>К столам</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};

/** Официант: столы и чеки — удобно с телефона */
const WaiterPage: React.FC = () => {
  const { checkId } = useParams();
  useLiveWaiter();
  return (
    <Box sx={{ py: { xs: 1.5, sm: 2 }, maxWidth: 900, mx: 'auto' }}>
      {checkId ? <CheckView id={Number(checkId)} /> : <TablesView />}
    </Box>
  );
};

export default WaiterPage;
