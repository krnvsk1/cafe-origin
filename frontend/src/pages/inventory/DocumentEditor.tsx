import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, TextField, Autocomplete, Box, Typography,
  IconButton, Alert, CircularProgress, InputAdornment, MenuItem, useMediaQuery, useTheme, createFilterOptions,
  Tooltip,
} from '@mui/material';
import {
  Close as CloseIcon, Add as AddIcon, DeleteOutline as DeleteIcon, AttachFile as AttachIcon,
  PlaylistAddCheck as FillIcon, OpenInNew as OpenIcon,
} from '@mui/icons-material';
import {
  useStockDocument, useStockItems, useSuppliers, useSaveStockDocument, usePostStockDocument,
  useCancelStockDocument, useDeleteStockDocument, uploadDocumentAttachment, deleteDocumentAttachment,
  fetchInventorySheet, useSemiBreakdowns, apiError, fmtQty, fmtMoney, fmtDate, toDateInput, UNIT_LABELS, DOCUMENT_TITLES, WRITEOFF_REASONS,
  type StockDocumentType, type StockItem, type Supplier, type StockDocumentInput,
} from '../../api/inventory';
import { useQueryClient } from '@tanstack/react-query';
import { resolveMediaUrl } from '../../utils/media';
import { tokens } from '../../theme';
import { StatusChip } from './ui';
import StockItemDialog from './StockItemDialog';
import { SupplierDialog } from './SuppliersTab';

interface Props {
  open: boolean;
  type: StockDocumentType;
  /** null — новый документ */
  documentId: number | null;
  onClose: () => void;
  /** После создания черновика — чтобы список знал его id */
  onCreated?: (id: number) => void;
}

interface LineState {
  key: string;
  stockItemId: number | null;
  name: string;
  unit: StockItem['unit'] | null;
  quantity: string;
  price: string;
  sum: string;
  /** Инвентаризация: учётный остаток */
  expected: number | null;
  /** Инвентаризация: строка полуфабриката — его вес разложится на товары по техкарте */
  isSemi?: boolean;
  /** Проведённая инвентаризация: сколько товара было в п/ф */
  semiQuantity?: number | null;
}

type CreateOption = { create: true; inputValue: string };
type ItemOption = StockItem | CreateOption;

const isCreate = (o: unknown): o is CreateOption => !!o && typeof o === 'object' && 'create' in (o as object);
const num = (s: string) => {
  const v = parseFloat(s.replace(',', '.'));
  return Number.isFinite(v) ? v : 0;
};
const round = (v: number, d = 2) => Math.round(v * 10 ** d) / 10 ** d;
let keySeq = 0;
const newKey = () => `l${++keySeq}`;
const emptyLine = (): LineState => ({ key: newKey(), stockItemId: null, name: '', unit: null, quantity: '', price: '', sum: '', expected: null });

const itemFilter = createFilterOptions<StockItem>({ stringify: o => `${o.name} ${o.category ?? ''}` });
const supplierFilter = createFilterOptions<Supplier>({ stringify: o => `${o.name} ${o.inn ?? ''}` });

const DocumentEditor: React.FC<Props> = ({ open, type: initialType, documentId, onClose, onCreated }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const fullScreen = useMediaQuery(theme.breakpoints.down('md'));

  const [id, setId] = React.useState<number | null>(documentId);
  const { data: doc, isLoading } = useStockDocument(open ? id : null);
  // Тип берём из документа, если он уже загружен (открыт из истории позиции)
  const type: StockDocumentType = doc?.type ?? initialType;
  // В инвентаризации можно пересчитать и полуфабрикаты (с техкартой): их вес разложится на товары
  const { data: stockItems = [] } = useStockItems(type === 'inventory' ? {} : { kind: 'raw' });
  const allItems = React.useMemo(
    () => stockItems.filter(i => i.kind === 'raw' || (type === 'inventory' && i.hasTechCard)),
    [stockItems, type]);
  const { data: suppliers = [] } = useSuppliers();
  const save = useSaveStockDocument();
  const post = usePostStockDocument();
  const cancel = useCancelStockDocument();
  const remove = useDeleteStockDocument();
  const qc = useQueryClient();
  const refreshDocument = () => qc.invalidateQueries({ queryKey: ['inventory'] });

  const [number, setNumber] = React.useState('');
  const [date, setDate] = React.useState(toDateInput());
  const [supplier, setSupplier] = React.useState<Supplier | null>(null);
  const [reason, setReason] = React.useState('spoilage');
  const [comment, setComment] = React.useState('');
  const [lines, setLines] = React.useState<LineState[]>([emptyLine()]);
  const [error, setError] = React.useState('');
  const [info, setInfo] = React.useState('');
  const [dirty, setDirty] = React.useState(false);
  const [confirm, setConfirm] = React.useState<'cancel' | 'delete' | 'close' | null>(null);
  const [newItem, setNewItem] = React.useState<{ lineKey: string; name: string } | null>(null);
  const [newSupplier, setNewSupplier] = React.useState<string | null>(null);
  const [uploading, setUploading] = React.useState(false);
  const [filling, setFilling] = React.useState(false);
  const fileRef = React.useRef<HTMLInputElement | null>(null);

  const titles = DOCUMENT_TITLES[type];
  const isDraft = !doc || doc.status === 'draft';
  const busy = save.isPending || post.isPending || cancel.isPending || remove.isPending || uploading;

  // Открытие окна: сброс к пустому документу или к загруженному
  React.useEffect(() => {
    if (!open) return;
    setId(documentId);
    setError('');
    setInfo('');
    setDirty(false);
    if (!documentId) {
      setNumber('');
      setDate(toDateInput());
      setSupplier(null);
      setReason('spoilage');
      setComment('');
      setLines([emptyLine()]);
    }
  }, [open, documentId]);

  React.useEffect(() => {
    if (!doc || !open || dirty) return;
    setNumber(doc.number ?? '');
    setDate(toDateInput(doc.date));
    setReason(doc.reason ?? 'spoilage');
    setComment(doc.comment ?? '');
    setSupplier(doc.supplierId ? suppliers.find(s => s.id === doc.supplierId) ?? { id: doc.supplierId, name: doc.supplierName ?? '' } as Supplier : null);
    setLines(doc.lines.map(l => ({
      key: newKey(),
      stockItemId: l.stockItemId,
      name: l.stockItemName ?? '',
      unit: l.unit,
      quantity: String(l.quantity),
      price: l.price ? String(l.price) : '',
      sum: l.sum ? String(l.sum) : '',
      expected: doc.status === 'draft' ? l.currentQuantity : l.expectedQuantity,
      isSemi: !!l.isSemi,
      semiQuantity: l.semiQuantity ?? null,
    })));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [doc, open]);

  const updateLine = (key: string, patch: Partial<LineState>) => {
    setDirty(true);
    setLines(ls => ls.map(l => (l.key === key ? { ...l, ...patch } : l)));
  };

  const setQuantity = (l: LineState, value: string) => {
    const patch: Partial<LineState> = { quantity: value };
    if (type === 'receipt' && l.price) patch.sum = String(round(num(value) * num(l.price)));
    updateLine(l.key, patch);
  };
  const setPrice = (l: LineState, value: string) =>
    updateLine(l.key, { price: value, sum: l.quantity ? String(round(num(l.quantity) * num(value))) : l.sum });
  const setSum = (l: LineState, value: string) =>
    updateLine(l.key, { sum: value, price: num(l.quantity) > 0 ? String(round(num(value) / num(l.quantity), 4)) : l.price });

  const chooseItem = (l: LineState, item: StockItem) => {
    // Одна и та же позиция дважды в инвентаризации не нужна
    if (type === 'inventory' && lines.some(x => x.key !== l.key && x.stockItemId === item.id)) {
      setError(`«${item.name}» уже есть в документе`);
      return;
    }
    const semi = item.kind === 'semi';
    const patch: Partial<LineState> = { stockItemId: item.id, name: item.name, unit: item.unit, expected: semi ? null : item.quantity, isSemi: semi };
    if (type === 'receipt' && !l.price && item.lastPrice > 0) {
      patch.price = String(item.lastPrice);
      if (l.quantity) patch.sum = String(round(num(l.quantity) * item.lastPrice));
    }
    updateLine(l.key, patch);
  };

  const addLine = () => {
    setDirty(true);
    setLines(ls => [...ls, emptyLine()]);
  };

  const removeLine = (key: string) => {
    setDirty(true);
    setLines(ls => (ls.length > 1 ? ls.filter(l => l.key !== key) : [emptyLine()]));
  };

  const fillInventory = async () => {
    setFilling(true);
    try {
      const sheet = await fetchInventorySheet();
      const existing = new Map(lines.filter(l => l.stockItemId).map(l => [l.stockItemId!, l]));
      setLines(sheet.map(r => existing.get(r.id) ?? ({
        key: newKey(), stockItemId: r.id, name: r.name, unit: r.unit, quantity: '', price: '', sum: '', expected: r.quantity,
      })));
      setDirty(true);
      setInfo(`Добавлено ${sheet.length} поз. Введите фактические остатки — пустые строки не попадут в документ.`);
    } catch (e) {
      setError(apiError(e));
    } finally {
      setFilling(false);
    }
  };

  const buildPayload = (postNow: boolean): StockDocumentInput | null => {
    const filled = lines.filter(l => l.stockItemId);
    const used = type === 'inventory' ? filled.filter(l => l.quantity.trim() !== '') : filled;
    if (used.length === 0) {
      setError(type === 'inventory' ? 'Введите фактический остаток хотя бы по одной позиции' : 'Добавьте хотя бы одну позицию');
      return null;
    }
    for (const l of used) {
      const q = num(l.quantity);
      if (type === 'inventory' ? q < 0 : q <= 0) {
        setError(`Проверьте количество у «${l.name}»`);
        return null;
      }
    }
    if (filled.length !== lines.filter(l => l.stockItemId || l.quantity || l.price).length) {
      setError('В одной из строк не выбрана позиция');
      return null;
    }
    return {
      type,
      number: number.trim() || null,
      date,
      supplierId: type === 'receipt' ? supplier?.id ?? null : null,
      reason: type === 'writeoff' ? reason : null,
      comment: comment.trim() || null,
      lines: used.map(l => ({
        stockItemId: l.stockItemId!,
        quantity: num(l.quantity),
        price: type === 'receipt' ? num(l.price) : 0,
        sum: type === 'receipt' && l.sum ? num(l.sum) : null,
      })),
      post: postNow,
    };
  };

  const handleSave = async (postNow: boolean) => {
    setError('');
    const payload = buildPayload(postNow);
    if (!payload) return;
    try {
      const saved = await save.mutateAsync({ id: id ?? undefined, data: payload });
      if (!id) {
        setId(saved.id);
        onCreated?.(saved.id);
      }
      setDirty(false);
      if (postNow) onClose();
      else setInfo('Черновик сохранён');
    } catch (e) {
      // Черновик мог сохраниться, а проведение — нет
      const created = (e as { response?: { data?: { id?: number } } })?.response?.data?.id;
      if (created && !id) {
        setId(created);
        onCreated?.(created);
        setDirty(false);
      }
      setError(apiError(e));
    }
  };

  const handleCancelDoc = async () => {
    if (!id) return;
    setConfirm(null);
    try {
      await cancel.mutateAsync(id);
      setInfo('Документ отменён, остатки возвращены');
    } catch (e) {
      setError(apiError(e));
    }
  };

  const handleDelete = async () => {
    if (!id) return;
    setConfirm(null);
    try {
      await remove.mutateAsync(id);
      onClose();
    } catch (e) {
      setError(apiError(e));
    }
  };

  const handleAttach = async (file: File) => {
    setError('');
    setUploading(true);
    try {
      let docId = id;
      if (!docId) {
        const payload = buildPayload(false);
        if (!payload) return;
        const saved = await save.mutateAsync({ data: payload });
        docId = saved.id;
        setId(saved.id);
        onCreated?.(saved.id);
        setDirty(false);
      }
      await uploadDocumentAttachment(docId, file);
      await refreshDocument();
      setInfo('Файл прикреплён');
    } catch (e) {
      setError(apiError(e));
    } finally {
      setUploading(false);
    }
  };

  const requestClose = () => {
    if (busy) return;
    if (dirty && isDraft) setConfirm('close');
    else onClose();
  };

  // Полуфабрикаты в черновике инвентаризации: из каких товаров состоит их вес
  const semiLines = type === 'inventory' && isDraft
    ? lines.filter(l => l.isSemi && l.stockItemId && num(l.quantity) > 0).map(l => ({ key: l.key, stockItemId: l.stockItemId!, quantity: num(l.quantity) }))
    : [];
  const semis = useSemiBreakdowns(semiLines);
  const inSemi = (l: LineState) => (isDraft ? semis.total.get(l.stockItemId ?? 0) ?? 0 : l.semiQuantity ?? 0);
  // Товары из п/ф, которых нет в списке: добавятся при проведении
  const missingFromSemis = [...semis.total.keys()].filter(id => !lines.some(l => l.stockItemId === id && !l.isSemi));

  const total = lines.reduce((s, l) => s + (type === 'receipt' ? num(l.sum) : 0), 0);
  const inventoryDiffValue = type === 'inventory'
    ? lines.reduce((s, l) => {
        if (!l.stockItemId || l.isSemi || l.expected === null || (l.quantity.trim() === '' && !inSemi(l))) return s;
        const item = allItems.find(i => i.id === l.stockItemId);
        return s + (num(l.quantity) + inSemi(l) - l.expected) * (item?.unitCost ?? 0);
      }, 0)
    : 0;

  const readOnly = !isDraft;
  const attachment = doc?.attachmentPath;

  // ---------- Строка позиции ----------
  const renderLine = (l: LineState, index: number) => {
    const unit = l.unit ? UNIT_LABELS[l.unit] : '';
    // Только что созданной позиции ещё может не быть в списке — показываем её по данным строки
    const selected = l.stockItemId
      ? allItems.find(i => i.id === l.stockItemId) ?? ({ id: l.stockItemId, name: l.name, unit: l.unit ?? 'kg' } as StockItem)
      : null;
    const fromSemi = type === 'inventory' && !l.isSemi ? inSemi(l) : 0;
    const diff = type === 'inventory' && !l.isSemi && (l.quantity.trim() !== '' || fromSemi > 0) && l.expected !== null
      ? round(num(l.quantity) + fromSemi - l.expected, 3) : null;
    const breakdown = l.isSemi ? semis.byLine.get(l.key) : undefined;
    const semiNote = breakdown && (breakdown.error
      ? <Typography variant="caption" color="error" sx={{ display: 'block', mt: 0.5, ml: 0.5 }}>{breakdown.error}</Typography>
      : breakdown.rows.length > 0 && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5, ml: 0.5 }}>
          Это ≈ {breakdown.rows.map(r => `${r.name} ${fmtQty(r.quantity, r.unit ?? undefined)}`).join(', ')}
        </Typography>
      ));

    if (readOnly && l.isSemi) {
      return (
        <Box key={l.key} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, py: 1.25, borderTop: index ? '1px solid' : 0, borderColor: 'divider' }}>
          <Typography sx={{ flex: 1, fontWeight: 500 }}>{l.name} <Typography component="span" variant="caption" color="text.secondary">полуфабрикат</Typography></Typography>
          <Typography sx={{ fontVariantNumeric: 'tabular-nums' }}>{fmtQty(num(l.quantity), l.unit)} → в товарах</Typography>
        </Box>
      );
    }

    if (readOnly) {
      return (
        <Box key={l.key} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, py: 1.25, borderTop: index ? '1px solid' : 0, borderColor: 'divider' }}>
          <Typography sx={{ flex: 1, fontWeight: 500 }}>{l.name}</Typography>
          {type === 'inventory' ? (
            <Box sx={{ textAlign: 'right' }}>
              <Typography sx={{ fontVariantNumeric: 'tabular-nums' }}>
                {fmtQty(l.expected)} → <b>{fmtQty(round(num(l.quantity) + fromSemi, 3), l.unit)}</b>
              </Typography>
              {fromSemi > 0 && (
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>в т. ч. в полуфабрикатах {fmtQty(fromSemi, l.unit)}</Typography>
              )}
              {diff !== null && diff !== 0 && (
                <Typography variant="body2" sx={{ color: diff < 0 ? tokens.chili : tokens.herb, fontWeight: 600 }}>
                  {diff > 0 ? '+' : ''}{fmtQty(diff, l.unit)}
                </Typography>
              )}
            </Box>
          ) : (
            <Box sx={{ textAlign: 'right' }}>
              <Typography sx={{ fontVariantNumeric: 'tabular-nums' }}>{fmtQty(num(l.quantity), l.unit)} × {fmtMoney(num(l.price))}</Typography>
              <Typography variant="body2" sx={{ fontWeight: 700 }}>{fmtMoney(num(l.sum))}</Typography>
            </Box>
          )}
        </Box>
      );
    }

    return (
      <Box
        key={l.key}
        sx={{
          display: 'grid',
          gridTemplateColumns: {
            xs: '1fr 1fr 1fr 40px',
            md: type === 'receipt' ? 'minmax(0,1fr) 120px 130px 130px 40px' : type === 'inventory' ? 'minmax(0,1fr) 110px 130px 110px 40px' : 'minmax(0,1fr) 140px 40px',
          },
          gridTemplateAreas: {
            xs: type === 'writeoff' ? '"item item item del" "qty qty qty del"' : '"item item item del" "a b c del"',
            md: 'none',
          },
          gap: 1,
          alignItems: 'start',
          py: 1.25,
          borderTop: index ? '1px solid' : 0,
          borderColor: 'divider',
        }}
      >
        <Box sx={{ gridArea: { xs: 'item', md: 'auto' } }}>
          <Autocomplete<ItemOption, false, false, false>
            size="small"
            options={allItems}
            // Ключ — по id: у одноимённых позиций одинаковое название, и список путался
            getOptionKey={o => (isCreate(o) ? `new:${o.inputValue}` : o.id)}
            value={selected}
            onChange={(_, v) => {
              if (!v) return;
              if (isCreate(v)) setNewItem({ lineKey: l.key, name: v.inputValue });
              else chooseItem(l, v);
            }}
            filterOptions={(options, params) => {
              const filtered = itemFilter(options as StockItem[], params) as ItemOption[];
              const q = params.inputValue.trim();
              if (q && !(options as StockItem[]).some(o => o.name.toLowerCase() === q.toLowerCase()))
                filtered.push({ create: true, inputValue: q });
              return filtered;
            }}
            getOptionLabel={o => (isCreate(o) ? o.inputValue : o.name)}
            isOptionEqualToValue={(a, b) => !isCreate(a) && !isCreate(b) && a.id === b.id}
            renderOption={(props, o) => {
              const { key, ...rest } = props as typeof props & { key: string };
              return isCreate(o) ? (
                <li key={key} {...rest}>
                  <AddIcon fontSize="small" sx={{ mr: 1 }} /> Создать «{o.inputValue}»
                </li>
              ) : (
                <li key={key} {...rest}>
                  <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Typography noWrap>{o.name}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {o.kind === 'semi' ? 'Полуфабрикат · вес разложится на товары по техкарте' : `${o.category || 'Без категории'} · остаток ${fmtQty(o.quantity, o.unit)}`}
                    </Typography>
                  </Box>
                </li>
              );
            }}
            noOptionsText="Начните вводить название"
            renderInput={params => <TextField {...params} placeholder="Позиция" />}
          />
          {type === 'inventory' && l.expected !== null && (
            <Typography variant="caption" color="text.secondary" sx={{ display: { md: 'none' }, mt: 0.5, ml: 0.5 }}>
              По учёту: {fmtQty(l.expected, l.unit)}
            </Typography>
          )}
          {fromSemi > 0 && (
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5, ml: 0.5 }}>
              + {fmtQty(fromSemi, l.unit)} в полуфабрикатах
            </Typography>
          )}
          {semiNote}
        </Box>

        {type === 'inventory' && (
          <Box sx={{ gridArea: { xs: 'a', md: 'auto' }, display: { xs: 'none', md: 'flex' }, alignItems: 'center', height: 40, justifyContent: 'flex-end', color: 'text.secondary', fontVariantNumeric: 'tabular-nums' }}>
            {l.isSemi ? 'п/ф' : l.expected !== null ? fmtQty(l.expected, l.unit) : '—'}
          </Box>
        )}

        <TextField
          size="small"
          type="number"
          placeholder={type === 'inventory' ? 'Факт' : 'Кол-во'}
          value={l.quantity}
          onChange={e => setQuantity(l, e.target.value)}
          sx={{ gridArea: { xs: type === 'writeoff' ? 'qty' : type === 'inventory' ? 'b' : 'a', md: 'auto' } }}
          slotProps={{
            input: { endAdornment: unit ? <InputAdornment position="end">{unit}</InputAdornment> : undefined },
            htmlInput: { min: 0, step: 'any', inputMode: 'decimal', 'aria-label': 'Количество' },
          }}
        />

        {type === 'receipt' && (
          <>
            <TextField
              size="small"
              type="number"
              placeholder="Цена"
              value={l.price}
              onChange={e => setPrice(l, e.target.value)}
              sx={{ gridArea: { xs: 'b', md: 'auto' } }}
              slotProps={{
                input: { endAdornment: <InputAdornment position="end">₽{unit ? `/${unit}` : ''}</InputAdornment> },
                htmlInput: { min: 0, step: 'any', inputMode: 'decimal', 'aria-label': 'Цена за единицу' },
              }}
            />
            <TextField
              size="small"
              type="number"
              placeholder="Сумма"
              value={l.sum}
              onChange={e => setSum(l, e.target.value)}
              sx={{ gridArea: { xs: 'c', md: 'auto' } }}
              slotProps={{
                input: { endAdornment: <InputAdornment position="end">₽</InputAdornment> },
                htmlInput: { min: 0, step: 'any', inputMode: 'decimal', 'aria-label': 'Сумма' },
              }}
            />
          </>
        )}

        {type === 'inventory' && (
          <Box sx={{ gridArea: { xs: 'c', md: 'auto' }, display: 'flex', alignItems: 'center', height: 40, justifyContent: 'flex-end', fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: diff === null || diff === 0 ? 'text.secondary' : diff < 0 ? tokens.chili : tokens.herb }}>
            {diff === null ? '' : diff === 0 ? 'сходится' : `${diff > 0 ? '+' : ''}${fmtQty(diff)}`}
          </Box>
        )}

        <IconButton aria-label="Удалить строку" onClick={() => removeLine(l.key)} sx={{ gridArea: { xs: 'del', md: 'auto' }, color: 'text.secondary' }}>
          <DeleteIcon fontSize="small" />
        </IconButton>
      </Box>
    );
  };

  const headerTitle = id
    ? `${titles.one}${doc?.number ? ` № ${doc.number}` : ` #${id}`}`
    : titles.create;

  return (
    <Dialog open={open} onClose={requestClose} fullScreen={fullScreen} maxWidth="md" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1.5, pr: 1.5 }}>
        <Box sx={{ flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', gap: 1.25, flexWrap: 'wrap' }}>
          {headerTitle}
          {doc && <StatusChip status={doc.status} />}
        </Box>
        <IconButton aria-label="Закрыть" onClick={requestClose}><CloseIcon /></IconButton>
      </DialogTitle>

      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
        {isLoading && id ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
        ) : (
          <>
            {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
            {info && !error && <Alert severity="success" onClose={() => setInfo('')}>{info}</Alert>}
            {doc?.status === 'cancelled' && (
              <Alert severity="warning">Документ отменён {fmtDate(doc.cancelledAt)} — его движения по складу сторнированы.</Alert>
            )}

            {/* Шапка документа */}
            {readOnly ? (
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: 'repeat(4, 1fr)' }, gap: 1.5 }}>
                {[
                  { label: 'Дата', value: fmtDate(doc?.date) },
                  type === 'receipt' && { label: 'Поставщик', value: doc?.supplierName || '—' },
                  type === 'writeoff' && { label: 'Причина', value: WRITEOFF_REASONS[doc?.reason ?? ''] ?? '—' },
                  { label: 'Проведён', value: fmtDate(doc?.postedAt) },
                  { label: type === 'inventory' ? 'Расхождение' : 'Сумма', value: fmtMoney(doc?.total) },
                ].filter(Boolean).map(f => (
                  <Box key={(f as { label: string }).label} sx={{ bgcolor: t.surfaceMuted, borderRadius: '14px', p: 1.5 }}>
                    <Typography variant="caption" color="text.secondary">{(f as { label: string }).label}</Typography>
                    <Typography sx={{ fontWeight: 600 }}>{(f as { value: string }).value}</Typography>
                  </Box>
                ))}
              </Box>
            ) : (
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: type === 'receipt' ? '160px 170px 1fr' : '170px 1fr' }, gap: 1.5 }}>
                {type === 'receipt' && (
                  <TextField label="№ накладной" value={number} onChange={e => { setNumber(e.target.value); setDirty(true); }} slotProps={{ htmlInput: { maxLength: 50 } }} />
                )}
                <TextField
                  label="Дата"
                  type="date"
                  value={date}
                  onChange={e => { setDate(e.target.value); setDirty(true); }}
                  slotProps={{ inputLabel: { shrink: true } }}
                />
                {type === 'receipt' && (
                  <Autocomplete<Supplier | CreateOption, false, false, false>
                    options={suppliers}
                    // Ключ — по id: у одноимённых позиций одинаковое название, и список путался
                    getOptionKey={o => (isCreate(o) ? `new:${o.inputValue}` : o.id)}
                    value={supplier}
                    onChange={(_, v) => {
                      if (isCreate(v)) setNewSupplier(v.inputValue);
                      else { setSupplier(v); setDirty(true); }
                    }}
                    filterOptions={(options, params) => {
                      const filtered = supplierFilter(options as Supplier[], params) as (Supplier | CreateOption)[];
                      const q = params.inputValue.trim();
                      if (q && !(options as Supplier[]).some(o => o.name.toLowerCase() === q.toLowerCase()))
                        filtered.push({ create: true, inputValue: q });
                      return filtered;
                    }}
                    getOptionLabel={o => (isCreate(o) ? o.inputValue : o.name)}
                    isOptionEqualToValue={(a, b) => !isCreate(a) && !isCreate(b) && a.id === b.id}
                    renderOption={(props, o) => {
                      const { key, ...rest } = props as typeof props & { key: string };
                      return <li key={key} {...rest}>{isCreate(o) ? <><AddIcon fontSize="small" sx={{ mr: 1 }} />Добавить «{o.inputValue}»</> : o.name}</li>;
                    }}
                    sx={{ gridColumn: { xs: '1 / -1', md: 'auto' } }}
                    renderInput={params => <TextField {...params} label="Поставщик" />}
                  />
                )}
                {type === 'writeoff' && (
                  <TextField select label="Причина" value={reason} onChange={e => { setReason(e.target.value); setDirty(true); }}>
                    {Object.entries(WRITEOFF_REASONS).map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}
                  </TextField>
                )}
                {type === 'inventory' && (
                  <Button
                    onClick={fillInventory}
                    disabled={filling}
                    startIcon={filling ? <CircularProgress size={16} /> : <FillIcon />}
                    sx={{ bgcolor: t.surfaceMuted, justifySelf: 'start', alignSelf: 'center' }}
                  >
                    Заполнить все позиции
                  </Button>
                )}
              </Box>
            )}

            {/* Строки */}
            <Box>
              {!readOnly && (
                <Box
                  sx={{
                    display: { xs: 'none', md: 'grid' },
                    gridTemplateColumns: type === 'receipt' ? 'minmax(0,1fr) 120px 130px 130px 40px' : type === 'inventory' ? 'minmax(0,1fr) 110px 130px 110px 40px' : 'minmax(0,1fr) 140px 40px',
                    gap: 1,
                    color: 'text.secondary',
                    fontSize: '0.8rem',
                    fontWeight: 600,
                    pb: 0.5,
                  }}
                >
                  <span>Позиция</span>
                  {type === 'receipt' && <><span>Количество</span><span>Цена</span><span>Сумма</span></>}
                  {type === 'writeoff' && <span>Количество</span>}
                  {type === 'inventory' && <><span style={{ textAlign: 'right' }}>По учёту</span><span>Факт</span><span style={{ textAlign: 'right' }}>Разница</span></>}
                  <span />
                </Box>
              )}
              {lines.map(renderLine)}
              {!readOnly && missingFromSemis.length > 0 && (
                <Alert severity="info" sx={{ mt: 1 }}>
                  В полуфабрикатах есть товары, которых нет в списке: {missingFromSemis.map(id => allItems.find(i => i.id === id)?.name ?? `#${id}`).join(', ')}.
                  При проведении они добавятся — их остаток будет равен количеству в полуфабрикатах. Если они лежат и отдельно, добавьте строку и укажите, сколько.
                </Alert>
              )}
              {!readOnly && type === 'inventory' && (
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
                  Можно добавить полуфабрикат (маринованное мясо, соус) и указать его вес — товары посчитаются по техкарте и прибавятся к факту.
                </Typography>
              )}
              {!readOnly && (
                <Button startIcon={<AddIcon />} onClick={addLine} sx={{ mt: 1, bgcolor: t.surfaceMuted }}>
                  Добавить строку
                </Button>
              )}
            </Box>

            {/* Итог */}
            {(type === 'receipt' || type === 'inventory') && !readOnly && (
              <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1, alignItems: 'baseline' }}>
                <Typography color="text.secondary">{type === 'receipt' ? 'Итого по накладной' : 'Расхождение примерно'}</Typography>
                <Typography sx={{ fontWeight: 800, fontSize: '1.3rem', color: type === 'inventory' && inventoryDiffValue < 0 ? tokens.chili : 'text.primary' }}>
                  {fmtMoney(round(type === 'receipt' ? total : inventoryDiffValue))}
                </Typography>
              </Box>
            )}

            <TextField
              label="Комментарий"
              value={comment}
              onChange={e => { setComment(e.target.value); setDirty(true); }}
              multiline
              minRows={1}
              disabled={readOnly}
              slotProps={{ htmlInput: { maxLength: 1000 } }}
            />

            {/* Фото накладной */}
            {type === 'receipt' && (
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
                <input
                  ref={fileRef}
                  type="file"
                  hidden
                  accept="image/*,application/pdf"
                  onChange={e => { const f = e.target.files?.[0]; if (f) void handleAttach(f); e.target.value = ''; }}
                />
                {attachment ? (
                  <>
                    <Button
                      component="a"
                      href={resolveMediaUrl(attachment)}
                      target="_blank"
                      rel="noopener"
                      startIcon={<OpenIcon />}
                      sx={{ bgcolor: t.surfaceMuted }}
                    >
                      Фото накладной
                    </Button>
                    {isDraft && (
                      <Button
                        onClick={async () => { if (id) { await deleteDocumentAttachment(id); setInfo('Файл удалён'); await refreshDocument(); } }}
                        sx={{ color: 'text.secondary' }}
                      >
                        Убрать
                      </Button>
                    )}
                  </>
                ) : (
                  <Tooltip title="JPG, PNG, HEIC или PDF до 15 МБ">
                    <span>
                      <Button
                        onClick={() => fileRef.current?.click()}
                        disabled={busy}
                        startIcon={uploading ? <CircularProgress size={16} /> : <AttachIcon />}
                        sx={{ bgcolor: t.surfaceMuted }}
                      >
                        Прикрепить фото накладной
                      </Button>
                    </span>
                  </Tooltip>
                )}
              </Box>
            )}

            {/* Подтверждения */}
            {confirm && (
              <Alert
                severity={confirm === 'close' ? 'info' : 'warning'}
                action={
                  <Box sx={{ display: 'flex', gap: 1 }}>
                    <Button size="small" color="inherit" onClick={() => setConfirm(null)}>Нет</Button>
                    <Button
                      size="small"
                      variant="contained"
                      color={confirm === 'close' ? 'primary' : 'error'}
                      onClick={confirm === 'cancel' ? handleCancelDoc : confirm === 'delete' ? handleDelete : () => { setConfirm(null); onClose(); }}
                    >
                      {confirm === 'cancel' ? 'Отменить документ' : confirm === 'delete' ? 'Удалить' : 'Закрыть'}
                    </Button>
                  </Box>
                }
              >
                {confirm === 'cancel' && 'Остатки вернутся к состоянию до проведения. Продолжить?'}
                {confirm === 'delete' && 'Удалить черновик без возможности восстановления?'}
                {confirm === 'close' && 'Есть несохранённые изменения. Закрыть без сохранения?'}
              </Alert>
            )}
          </>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 3, pt: 1.5, gap: 1, flexWrap: 'wrap', borderTop: '1px solid', borderColor: 'divider' }}>
        {isDraft ? (
          <>
            {id && (
              <Button onClick={() => setConfirm('delete')} disabled={busy} sx={{ color: tokens.chili, mr: 'auto' }}>
                Удалить
              </Button>
            )}
            <Button onClick={() => handleSave(false)} disabled={busy} sx={{ bgcolor: t.surfaceMuted, ml: id ? 0 : 'auto' }}>
              Сохранить черновик
            </Button>
            <Button
              variant="contained"
              color="secondary"
              onClick={() => handleSave(true)}
              disabled={busy}
              startIcon={post.isPending || save.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}
            >
              Провести
            </Button>
          </>
        ) : (
          <>
            {doc?.status === 'posted' && (
              <Button onClick={() => setConfirm('cancel')} disabled={busy} sx={{ color: tokens.chili, mr: 'auto' }}>
                Отменить документ
              </Button>
            )}
            <Button variant="contained" onClick={onClose} sx={{ ml: 'auto' }}>Готово</Button>
          </>
        )}
      </DialogActions>

      <StockItemDialog
        open={!!newItem}
        initialName={newItem?.name}
        allowSemi={false}
        onClose={() => setNewItem(null)}
        onSaved={(item) => {
          const line = lines.find(l => l.key === newItem?.lineKey);
          if (line) chooseItem(line, item);
        }}
      />
      <SupplierDialog
        open={newSupplier !== null}
        initialName={newSupplier ?? ''}
        onClose={() => setNewSupplier(null)}
        onSaved={(s) => { setSupplier(s); setDirty(true); }}
      />
    </Dialog>
  );
};

export default DocumentEditor;
