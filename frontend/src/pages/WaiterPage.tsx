import * as React from 'react';
import {
  Box, Typography, Button, IconButton, Chip, TextField, InputAdornment, CircularProgress, Alert, Drawer,
  Dialog, DialogTitle, DialogContent, DialogActions, ToggleButtonGroup, ToggleButton, Switch, alpha, useTheme,
  Menu, MenuItem, ListItemIcon, ListItemText, Snackbar,
} from '@mui/material';
import {
  ArrowBack as BackIcon, Add as AddIcon, Remove as RemoveIcon, Search as SearchIcon, Close as CloseIcon,
  TableRestaurant as TableIcon, CheckCircle as ReadyIcon, Schedule as CookingIcon, DoneAll as ServedIcon,
  SettingsOutlined as SettingsIcon, DeleteOutline as DeleteIcon, MoreVert as MoreIcon,
} from '@mui/icons-material';
import { useNavigate, useParams } from 'react-router-dom';
import { useShawarmas } from '../api/hooks';
import {
  useWaiterTables, useTableCheck, useOpenCheck, useAddRound, useCancelRound, useCloseCheck, useCancelCheck,
  useAllTables, useSaveTables, useUpdateCheck, useMoveCheck, useSplitCheck, useVoidItem, useFloorPlan, parseUtc,
  type WaiterTable, type CheckOrder, type TableSetting, type TableCheck, type TableCheckSummary, type CheckItem, type FloorPlan,
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
  const min = Math.max(0, Math.floor((Date.now() - parseUtc(iso).getTime()) / 60000));
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

/** Схема зала для официанта: столы на своих местах, цвет — занят, мой, готово нести */
export const FloorView: React.FC<{ plan: FloorPlan; tables: WaiterTable[]; highlightMine?: boolean; onTap: (t: WaiterTable) => void }> = ({ plan, tables, highlightMine, onTap }) => {
  const theme = useTheme();
  const byId = new Map(tables.map(t => [t.id, t]));
  return (
    <Box sx={{ position: 'relative', width: '100%', aspectRatio: `${plan.width} / ${plan.height}`, bgcolor: 'background.paper', borderRadius: '16px',
      border: '1px solid', borderColor: 'divider', overflow: 'hidden' }}>
      {plan.walls.map(w => (
        <Box key={w.id} sx={{ position: 'absolute', left: `${(w.x / plan.width) * 100}%`, top: `${(w.y / plan.height) * 100}%`,
          width: `${(w.w / plan.width) * 100}%`, height: `${(w.h / plan.height) * 100}%`, borderRadius: w.kind === 'plant' ? '50%' : '3px',
          bgcolor: w.kind === 'bar' ? alpha(tokens.saffron, 0.5) : w.kind === 'plant' ? alpha(tokens.herb, 0.35) : theme.palette.text.secondary }} />
      ))}
      {plan.tables.map(pt => {
        const t = byId.get(pt.tableId);
        if (!t) return null;
        const cs = t.checks;
        const ready = cs.some(c => c.ready > 0);
        const mine = cs.some(c => c.isMine);
        const color = cs.length === 0 ? theme.palette.divider : ready ? tokens.herb : mine ? tokens.saffron : theme.palette.text.secondary;
        const dim = highlightMine && !mine;
        return (
          <Box key={pt.tableId} component="button" type="button" onClick={() => onTap(t)}
            sx={{ position: 'absolute', left: `${(pt.x / plan.width) * 100}%`, top: `${(pt.y / plan.height) * 100}%`,
              width: `${(pt.w / plan.width) * 100}%`, height: `${(pt.h / plan.height) * 100}%`,
              borderRadius: pt.shape === 'round' ? '50%' : '10px', border: '2px solid', borderColor: color, p: 0,
              bgcolor: cs.length ? alpha(color, 0.18) : 'background.default', color: 'text.primary', font: 'inherit', cursor: 'pointer',
              display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', opacity: dim ? 0.35 : 1, overflow: 'hidden' }}>
            <Typography sx={{ fontWeight: 800, fontSize: { xs: '0.85rem', sm: '1.1rem' }, lineHeight: 1 }}>{t.name}</Typography>
            {cs.length > 0 && (
              <Typography sx={{ fontSize: { xs: '0.6rem', sm: '0.75rem' }, fontWeight: 700, lineHeight: 1.1 }}>
                {money(cs.reduce((x, c) => x + c.total, 0))}
              </Typography>
            )}
          </Box>
        );
      })}
    </Box>
  );
};

const TablesView: React.FC = () => {
  const theme = useTheme();
  const navigate = useNavigate();
  const can = useCan();
  const { data: tables = [], isLoading, error } = useWaiterTables();
  const open = useOpenCheck();
  const [mine, setMine] = React.useState(false);
  const { data: floor } = useFloorPlan();
  const hasPlan = !!floor && floor.tables.length > 0;
  const [view, setView] = React.useState<'plan' | 'tiles'>(() => {
    try { return (localStorage.getItem('waiter-view') as 'plan' | 'tiles') || 'plan'; } catch { return 'plan'; }
  });
  const chooseView = (v: 'plan' | 'tiles') => { setView(v); try { localStorage.setItem('waiter-view', v); } catch { /* ignore */ } };
  const [opening, setOpening] = React.useState<WaiterTable | null>(null);
  const [guests, setGuests] = React.useState(2);
  const [settings, setSettings] = React.useState(false);
  const [err, setErr] = React.useState('');

  const [choosing, setChoosing] = React.useState<WaiterTable | null>(null);
  const shown = mine ? tables.filter(t => t.checks.some(c => c.isMine)) : tables;
  const busy = tables.filter(t => t.checks.length > 0).length;

  const tap = (t: WaiterTable) => {
    if (t.checks.length === 1) navigate(`/waiter/${t.checks[0].id}`);
    else if (t.checks.length > 1) setChoosing(t);
    else { setGuests(2); setErr(''); setOpening(t); }
  };
  const checkTitle = (c: TableCheckSummary, i: number) => c.label ?? (i === 0 ? 'Счёт 1' : `Счёт ${i + 1}`);

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
        {can(P.TablesManage) && <IconButton onClick={() => setSettings(true)} aria-label="Настроить столы"><SettingsIcon /></IconButton>}
      </Box>
      <Box sx={{ display: 'flex', gap: 1 }}>
        <Chip label="Все" color={!mine ? 'primary' : 'default'} onClick={() => setMine(false)} />
        <Chip label="Мои" color={mine ? 'primary' : 'default'} onClick={() => setMine(true)} />
        <Box sx={{ flex: 1 }} />
        {hasPlan && (
          <ToggleButtonGroup size="small" exclusive value={view} onChange={(_, v) => v && chooseView(v)}>
            <ToggleButton value="plan">Зал</ToggleButton>
            <ToggleButton value="tiles">Плитки</ToggleButton>
          </ToggleButtonGroup>
        )}
      </Box>
      {error && <Alert severity="error">{apiError(error, 'Не удалось загрузить столы')}</Alert>}
      {!isLoading && hasPlan && view === 'plan' && <FloorView plan={floor!} tables={tables} highlightMine={mine} onTap={tap} />}
      {isLoading ? <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box> : (hasPlan && view === 'plan') ? null : (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(3, minmax(0, 1fr))', sm: 'repeat(4, minmax(0, 1fr))', md: 'repeat(6, minmax(0, 1fr))' }, gap: 1 }}>
          {shown.map(t => {
            const cs = t.checks;
            const total = cs.reduce((x, c) => x + c.total, 0);
            const ready = cs.some(c => c.ready > 0);
            const mineHere = cs.some(c => c.isMine);
            const color = cs.length === 0 ? theme.palette.divider : ready ? tokens.herb : mineHere ? tokens.saffron : theme.palette.text.secondary;
            const first = cs[0];
            return (
              <Box key={t.id} component="button" type="button" onClick={() => tap(t)}
                sx={{
                  border: '2px solid', borderColor: color, borderRadius: '16px', p: 1.25, minHeight: 92,
                  bgcolor: cs.length ? alpha(color, 0.1) : 'background.paper', color: 'text.primary', font: 'inherit', textAlign: 'left',
                  display: 'flex', flexDirection: 'column', gap: 0.25, cursor: 'pointer', '&:active': { transform: 'scale(0.97)' },
                }}>
                <Typography sx={{ fontWeight: 800, fontSize: '1.4rem', lineHeight: 1.1 }}>{t.name}</Typography>
                {first ? (
                  <>
                    <Typography sx={{ fontWeight: 700, fontSize: '0.9rem' }}>{money(total)}</Typography>
                    <Typography variant="caption" color="text.secondary" noWrap>
                      {cs.length > 1 ? `${cs.length} ${plural(cs.length, ['счёт', 'счёта', 'счетов'])}` : first.isMine ? 'мой' : first.waiter} · {sinceText(first.openedAt)}
                    </Typography>
                    {ready && <Typography variant="caption" sx={{ color: tokens.herb, fontWeight: 800 }}>готово — нести</Typography>}
                  </>
                ) : (
                  <Typography variant="caption" color="text.secondary">свободен</Typography>
                )}
              </Box>
            );
          })}
        </Box>
      )}

      {/* За столом несколько счетов — выбрать или открыть ещё один */}
      <Dialog open={!!choosing} onClose={() => setChoosing(null)} maxWidth="xs" fullWidth>
        <DialogTitle>Стол {choosing?.name}</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
          {choosing?.checks.map((c, i) => (
            <Button key={c.id} variant="outlined" onClick={() => navigate(`/waiter/${c.id}`)} sx={{ justifyContent: 'space-between', py: 1.25 }}>
              <span>{checkTitle(c, i)}{c.isMine ? '' : ` · ${c.waiter ?? ''}`}</span>
              <b>{money(c.total)}</b>
            </Button>
          ))}
          <Button startIcon={<AddIcon />} onClick={() => { const t = choosing; setChoosing(null); if (t) { setGuests(1); setErr(''); setOpening(t); } }}>
            Открыть ещё счёт
          </Button>
        </DialogContent>
      </Dialog>

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
export const TablesSettings: React.FC<{ onClose: () => void }> = ({ onClose }) => {
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
  const open = useOpenCheck();
  const [picker, setPicker] = React.useState(false);
  const [menu, setMenu] = React.useState<HTMLElement | null>(null);
  const [dialog, setDialog] = React.useState<'move' | 'split' | 'edit' | null>(null);
  const [toast, setToast] = React.useState('');
  const [voiding, setVoiding] = React.useState<CheckItem | null>(null);
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

  const newCheckHere = async () => {
    setMenu(null);
    try {
      const r = await open.mutateAsync({ tableId: check.table.id, guests: 1 });
      navigate(`/waiter/${r.id}`);
    } catch (e) { setErr(apiError(e)); }
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
        {isOpen && <IconButton onClick={e => setMenu(e.currentTarget)} aria-label="Действия со счётом"><MoreIcon /></IconButton>}
      </Box>

      {/* Счета этого стола: переключение */}
      {(check.siblings.length > 0 || check.label) && (
        <Box sx={{ display: 'flex', gap: 0.75, overflowX: 'auto', pb: 0.5 }}>
          <Chip color="primary" label={check.label ?? 'Этот счёт'} sx={{ flexShrink: 0 }} />
          {check.siblings.map(sb => (
            <Chip key={sb.id} label={sb.label ?? `Счёт №${sb.id}`} onClick={() => navigate(`/waiter/${sb.id}`)} sx={{ flexShrink: 0 }} />
          ))}
        </Box>
      )}

      <Menu anchorEl={menu} open={!!menu} onClose={() => setMenu(null)}>
        <MenuItem onClick={() => { setMenu(null); setDialog('split'); }} disabled={active.length === 0}>
          <ListItemText primary="Разделить счёт" secondary="Перенести блюда в другой счёт" />
        </MenuItem>
        <MenuItem onClick={() => { setMenu(null); setDialog('move'); }}>
          <ListItemText primary="Перенести на другой стол" />
        </MenuItem>
        <MenuItem onClick={newCheckHere}>
          <ListItemIcon><AddIcon fontSize="small" /></ListItemIcon>
          <ListItemText primary="Ещё счёт за этим столом" />
        </MenuItem>
        <MenuItem onClick={() => { setMenu(null); setDialog('edit'); }}>
          <ListItemText primary="Подпись счёта и гости" />
        </MenuItem>
      </Menu>

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
                {idx + 1}-я подача <Typography component="span" variant="body2" color="text.secondary">· №{o.id} · {parseUtc(o.createdAt).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })}</Typography>
              </Typography>
              <Chip size="small" label={st.text} sx={{ bgcolor: alpha(st.color, 0.14), color: st.color, fontWeight: 700 }} />
            </Box>
            {o.items.map(i => (
              <Box key={i.id} onClick={() => isOpen && !cancelled && i.quantity - i.pendingVoid > 0 && setVoiding(i)}
                sx={{ display: 'flex', alignItems: 'center', gap: 1, py: 0.25, cursor: isOpen && !cancelled ? 'pointer' : 'default', borderRadius: 1, '&:active': { bgcolor: 'action.hover' } }}>
                <ItemState ready={i.readyQuantity} served={i.servedQuantity} qty={i.quantity} cancelled={cancelled} />
                <Typography sx={{ flex: 1, fontSize: '0.95rem', textDecoration: cancelled ? 'line-through' : 'none' }}>
                  {i.quantity} × {i.name}
                  {i.addons.length > 0 && <Typography component="span" variant="body2" color="text.secondary"> + {i.addons.map(a => a.addonName).join(', ')}</Typography>}
                </Typography>
                {i.moved && <Chip size="small" label="перенесено" sx={{ height: 20, fontSize: '0.7rem' }} />}
                {i.pendingVoid > 0 && <Chip size="small" color="error" variant="outlined" label={`убрать ${i.pendingVoid} — ждёт кухню`} sx={{ height: 20, fontSize: '0.7rem' }} />}
                {i.quantity > 1 && i.readyQuantity > 0 && i.servedQuantity < i.quantity && (
                  <Typography variant="caption" sx={{ color: tokens.herb, fontWeight: 700 }}>готово {i.readyQuantity}/{i.quantity}</Typography>
                )}
              </Box>
            ))}
            {o.notes && <Typography variant="body2" color="text.secondary" sx={{ fontStyle: 'italic', mt: 0.5 }}>«{o.notes}»</Typography>}
            {isOpen && o.status === 'Новый' && (
              <Button size="small" color="error" onClick={() => cancelRound.mutate({ checkId: check.id, orderId: o.id })} disabled={cancelRound.isPending} sx={{ mt: 0.5 }}>
                Отменить подачу
              </Button>
            )}
          </Box>
        );
      })}

      {check.voids.length > 0 && (
        <Box sx={{ bgcolor: 'background.paper', borderRadius: '16px', p: 1.5 }}>
          <Typography sx={{ fontWeight: 700, mb: 0.5 }}>Убрано из счёта</Typography>
          {check.voids.map(v => (
            <Typography key={v.id} variant="body2" color="text.secondary">
              {v.quantity} × {v.name} — {v.reason} · {v.status === 'pending' ? 'ждёт кухню' : v.status === 'rejected' ? 'кухня отказала' : v.writeOff ? 'списано' : 'без списания'}
            </Typography>
          ))}
        </Box>
      )}

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
      {dialog === 'split' && <SplitDialog check={check} onClose={() => setDialog(null)} onDone={(target, isNew) => {
        setDialog(null);
        setToast(isNew ? 'Блюда перенесены в новый счёт — он в списке счетов стола' : 'Блюда перенесены');
        if (isNew) navigate(`/waiter/${target}`);
      }} />}
      {dialog === 'move' && <MoveDialog check={check} onClose={() => setDialog(null)} onDone={name => { setDialog(null); setToast(`Счёт перенесён на стол ${name}`); }} />}
      {dialog === 'edit' && <EditCheckDialog check={check} onClose={() => setDialog(null)} />}
      {voiding && <VoidDialog item={voiding} onClose={() => setVoiding(null)} onDone={needsKitchen => {
        setVoiding(null);
        setToast(needsKitchen ? 'Блюдо уже готовят — кухня подтвердит отмену' : 'Блюдо убрано из счёта');
      }} />}
      <Snackbar open={!!toast} autoHideDuration={3000} onClose={() => setToast('')} message={toast} anchorOrigin={{ vertical: 'top', horizontal: 'center' }} />

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

// ==================== Отмена блюда ====================

const VOID_REASONS = ['Гость передумал', 'Ошибка официанта', 'Долго ждали', 'Гостю не понравилось', 'Нет в наличии'];

/** Убрать блюдо из счёта: сколько штук и почему. Если блюдо уже готовят — решает кухня */
const VoidDialog: React.FC<{ item: CheckItem; onClose: () => void; onDone: (needsKitchen: boolean) => void }> = ({ item, onClose, onDone }) => {
  const voidItem = useVoidItem();
  const max = item.quantity - item.pendingVoid;
  const [qty, setQty] = React.useState(1);
  const [reason, setReason] = React.useState('');
  const [err, setErr] = React.useState('');
  const submit = async () => {
    setErr('');
    try {
      const r = await voidItem.mutateAsync({ itemId: item.id, quantity: qty, reason: reason.trim() });
      onDone(r.needsKitchen);
    } catch (e) { setErr(apiError(e)); }
  };
  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>Убрать из счёта</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        <Typography sx={{ fontWeight: 700 }}>{item.name}</Typography>
        {max > 1 && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            <IconButton onClick={() => setQty(q => Math.max(1, q - 1))} sx={{ bgcolor: 'action.hover' }} aria-label="Меньше"><RemoveIcon /></IconButton>
            <Typography sx={{ fontWeight: 800, fontSize: '1.4rem' }}>{qty} из {max}</Typography>
            <IconButton onClick={() => setQty(q => Math.min(max, q + 1))} sx={{ bgcolor: 'action.hover' }} aria-label="Больше"><AddIcon /></IconButton>
          </Box>
        )}
        <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap' }}>
          {VOID_REASONS.map(r => <Chip key={r} label={r} color={reason === r ? 'primary' : 'default'} onClick={() => setReason(r)} />)}
        </Box>
        <TextField size="small" label="Причина" value={reason} onChange={e => setReason(e.target.value)} slotProps={{ htmlInput: { maxLength: 300 } }} />
        <Typography variant="caption" color="text.secondary">Если кухня уже готовит — она подтвердит отмену. Всё видно администратору в «Отменённых блюдах».</Typography>
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button onClick={onClose} sx={{ color: 'text.secondary' }}>Назад</Button>
        <Button variant="contained" color="error" onClick={submit} disabled={!reason.trim() || voidItem.isPending}>Убрать</Button>
      </DialogActions>
    </Dialog>
  );
};

// ==================== Деление, перенос, подпись ====================

/** Разделить счёт: сколько штук каждого блюда перенести и куда — в новый счёт или другой счёт стола */
const SplitDialog: React.FC<{ check: TableCheck; onClose: () => void; onDone: (targetId: number, isNew: boolean) => void }> = ({ check, onClose, onDone }) => {
  const split = useSplitCheck();
  const [qty, setQty] = React.useState<Record<number, number>>({});
  const [target, setTarget] = React.useState<number | 'new'>('new');
  const [label, setLabel] = React.useState('');
  const [err, setErr] = React.useState('');
  const items = check.orders.filter(o => o.status !== 'Отменён').flatMap(o => o.items);
  const moving = items.filter(i => (qty[i.id] ?? 0) > 0);
  const sum = moving.reduce((s, i) => s + (i.lineTotal / i.quantity) * (qty[i.id] ?? 0), 0);

  const submit = async () => {
    setErr('');
    try {
      const r = await split.mutateAsync({
        checkId: check.id,
        items: moving.map(i => ({ itemId: i.id, quantity: qty[i.id] })),
        targetCheckId: target === 'new' ? null : target,
        label: target === 'new' ? label.trim() || null : null,
      });
      onDone(r.targetCheckId, target === 'new');
    } catch (e) { setErr(apiError(e)); }
  };

  return (
    <Dialog open onClose={onClose} fullScreen>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, px: 1.5, pt: 1.5 }}>
        <IconButton onClick={onClose} aria-label="Закрыть"><CloseIcon /></IconButton>
        <Typography sx={{ fontWeight: 800, fontSize: '1.15rem' }}>Разделить счёт</Typography>
      </Box>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        <Typography variant="body2" color="text.secondary">Отметьте, что перенести. Можно часть штук: из 3 шаурм перенести 1.</Typography>
        {items.map(i => {
          const n = qty[i.id] ?? 0;
          return (
            <Box key={i.id} sx={{ display: 'flex', alignItems: 'center', gap: 1, p: 1, borderRadius: '12px', bgcolor: n > 0 ? 'action.selected' : 'background.paper' }}>
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography sx={{ fontWeight: 600 }} noWrap>{i.name}</Typography>
                <Typography variant="body2" color="text.secondary">{i.quantity} шт · {money(i.lineTotal)}</Typography>
              </Box>
              <IconButton size="small" onClick={() => setQty(q => ({ ...q, [i.id]: Math.max(0, n - 1) }))} disabled={n === 0} aria-label="Меньше"><RemoveIcon /></IconButton>
              <Typography sx={{ fontWeight: 800, minWidth: 22, textAlign: 'center' }}>{n}</Typography>
              <IconButton size="small" onClick={() => setQty(q => ({ ...q, [i.id]: Math.min(i.quantity, n + 1) }))} disabled={n >= i.quantity} aria-label="Больше"><AddIcon /></IconButton>
            </Box>
          );
        })}
        <Typography sx={{ fontWeight: 700, mt: 1 }}>Куда</Typography>
        <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap' }}>
          <Chip label="Новый счёт" color={target === 'new' ? 'primary' : 'default'} onClick={() => setTarget('new')} />
          {check.siblings.map(sb => (
            <Chip key={sb.id} label={sb.label ?? `Счёт №${sb.id}`} color={target === sb.id ? 'primary' : 'default'} onClick={() => setTarget(sb.id)} />
          ))}
        </Box>
        {target === 'new' && (
          <TextField size="small" label="Подпись нового счёта (необязательно)" value={label} onChange={e => setLabel(e.target.value)} placeholder="Анна, Счёт 2…"
            slotProps={{ htmlInput: { maxLength: 40 } }} />
        )}
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 2, pb: 'calc(16px + env(safe-area-inset-bottom))' }}>
        <Button fullWidth variant="contained" color="secondary" size="large" onClick={submit} disabled={moving.length === 0 || split.isPending}>
          Перенести {moving.length ? `· ${money(sum)}` : ''}
        </Button>
      </DialogActions>
    </Dialog>
  );
};

/** Пересадить гостей: счёт переходит на другой стол (там может быть и свой счёт) */
const MoveDialog: React.FC<{ check: TableCheck; onClose: () => void; onDone: (tableName: string) => void }> = ({ check, onClose, onDone }) => {
  const { data: tables = [] } = useWaiterTables();
  const move = useMoveCheck();
  const [err, setErr] = React.useState('');
  const go = async (t: WaiterTable) => {
    setErr('');
    try { await move.mutateAsync({ checkId: check.id, tableId: t.id }); onDone(t.name); } catch (e) { setErr(apiError(e)); }
  };
  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>Перенести на стол</DialogTitle>
      <DialogContent>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 1 }}>
          {tables.filter(t => t.id !== check.table.id).map(t => (
            <Button key={t.id} variant={t.checks.length ? 'outlined' : 'contained'} color="secondary" onClick={() => go(t)} disabled={move.isPending}
              sx={{ minHeight: 56, flexDirection: 'column', lineHeight: 1.1 }}>
              <b style={{ fontSize: '1.2rem' }}>{t.name}</b>
              {t.checks.length > 0 && <span style={{ fontSize: '0.65rem' }}>занят</span>}
            </Button>
          ))}
        </Box>
        {err && <Alert severity="error" sx={{ mt: 2 }}>{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button onClick={onClose} sx={{ color: 'text.secondary' }}>Отмена</Button>
      </DialogActions>
    </Dialog>
  );
};

const EditCheckDialog: React.FC<{ check: TableCheck; onClose: () => void }> = ({ check, onClose }) => {
  const update = useUpdateCheck();
  const [label, setLabel] = React.useState(check.label ?? '');
  const [guests, setGuests] = React.useState(check.guests ?? 1);
  const [err, setErr] = React.useState('');
  const submit = async () => {
    setErr('');
    try { await update.mutateAsync({ checkId: check.id, label: label.trim() || null, guests }); onClose(); } catch (e) { setErr(apiError(e)); }
  };
  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>Счёт · стол {check.table.name}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
        <TextField label="Подпись счёта" value={label} onChange={e => setLabel(e.target.value)} placeholder="Анна, Счёт 2…" slotProps={{ htmlInput: { maxLength: 40 } }} />
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 2 }}>
          <IconButton onClick={() => setGuests(g => Math.max(1, g - 1))} sx={{ bgcolor: 'action.hover' }} aria-label="Меньше гостей"><RemoveIcon /></IconButton>
          <Typography sx={{ fontWeight: 800, fontSize: '1.6rem', minWidth: 120, textAlign: 'center' }}>{guests} {plural(guests, GUEST_FORMS)}</Typography>
          <IconButton onClick={() => setGuests(g => Math.min(50, g + 1))} sx={{ bgcolor: 'action.hover' }} aria-label="Больше гостей"><AddIcon /></IconButton>
        </Box>
        {err && <Alert severity="error">{err}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3 }}>
        <Button onClick={onClose} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" onClick={submit} disabled={update.isPending}>Сохранить</Button>
      </DialogActions>
    </Dialog>
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
