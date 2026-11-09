import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, TextField, Autocomplete, Box, Typography, IconButton,
  Alert, CircularProgress, InputAdornment, useMediaQuery, useTheme, createFilterOptions, ToggleButtonGroup, ToggleButton,
} from '@mui/material';
import {
  Close as CloseIcon, Add as AddIcon, DeleteOutline as DeleteIcon, ContentCopy as CopyIcon, EditOutlined as EditIcon,
  RestaurantMenuOutlined as DishIcon,
} from '@mui/icons-material';
import {
  useTechCard, useStockItems, useSaveTechCard, useDeleteTechCard, useDishes, fetchTechCard, apiError,
  fmtMoney, fmtPortions, UNIT_LABELS, type TechCardTarget, type StockItem, type TechCardData, type DishRow,
} from '../../api/inventory';
import { tokens } from '../../theme';
import { FoodCost, Pill } from './ui';
import StockItemDialog from './StockItemDialog';

interface Props {
  open: boolean;
  target: TechCardTarget;
  id: number;
  onClose: () => void;
}

interface LineState {
  key: string;
  stockItemId: number | null;
  /** Блюдо меню в составе (kind = 'dish'), amount — доля порции */
  subDishId: number | null;
  name: string;
  unit: StockItem['unit'] | null;
  kind: StockItem['kind'] | 'dish' | null;
  /** В граммах/миллилитрах для кг/л, в штуках для шт, в порциях для блюда */
  amount: string;
  loss: string;
}

type CreateOption = { create: true; inputValue: string };
type ItemOption = StockItem | CreateOption;
const isCreate = (o: unknown): o is CreateOption => !!o && typeof o === 'object' && 'create' in (o as object);

const num = (s: string) => {
  const v = parseFloat(s.replace(',', '.'));
  return Number.isFinite(v) ? v : 0;
};
/** Множитель между вводом и единицей учёта: граммы → кг, мл → л */
const factor = (unit: StockItem['unit'] | null) => (unit === 'kg' || unit === 'l' ? 1000 : 1);
const smallUnit = (unit: StockItem['unit'] | null) => (unit === 'kg' ? 'г' : unit === 'l' ? 'мл' : unit === 'pcs' ? 'шт' : '');
let seq = 0;
const newKey = () => `t${++seq}`;
const emptyLine = (): LineState => ({ key: newKey(), stockItemId: null, subDishId: null, name: '', unit: null, kind: null, amount: '', loss: '' });
const emptyDishLine = (): LineState => ({ key: newKey(), stockItemId: null, subDishId: null, name: '', unit: null, kind: 'dish', amount: '1', loss: '' });
/** Частые доли порции: целое блюдо, половина, треть, четверть */
const PORTIONS: [string, string][] = [['1', '1'], ['0.5', '½'], ['0.3333', '⅓'], ['0.25', '¼']];
const itemFilter = createFilterOptions<StockItem>({ stringify: o => `${o.name} ${o.category ?? ''}` });

const fromCard = (card: TechCardData): LineState[] =>
  card.lines.map(l => l.subDishId
    ? { key: newKey(), stockItemId: null, subDishId: l.subDishId, name: l.stockItemName ?? '', unit: null, kind: 'dish' as const, amount: String(Math.round(l.gross * 10000) / 10000), loss: '' }
    : {
        key: newKey(),
        stockItemId: l.stockItemId,
        subDishId: null,
        name: l.stockItemName ?? '',
        unit: l.unit === 'portion' ? null : l.unit,
        kind: l.kind,
        amount: String(Math.round(l.gross * factor(l.unit === 'portion' ? null : l.unit) * 1000) / 1000),
        loss: l.lossPercent ? String(l.lossPercent) : '',
      });

const TechCardEditor: React.FC<Props> = ({ open, target, id, onClose }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const fullScreen = useMediaQuery(theme.breakpoints.down('md'));

  const { data, isLoading } = useTechCard(open ? target : null, open ? id : null);
  const { data: allItems = [] } = useStockItems({});
  const { data: dishesData } = useDishes();
  const save = useSaveTechCard();
  const remove = useDeleteTechCard();

  const [lines, setLines] = React.useState<LineState[]>([emptyLine()]);
  const [outputWeight, setOutputWeight] = React.useState('');
  const [yieldQuantity, setYieldQuantity] = React.useState('1');
  const [instructions, setInstructions] = React.useState('');
  const [error, setError] = React.useState('');
  const [copyOpen, setCopyOpen] = React.useState(false);
  const [newItem, setNewItem] = React.useState<{ lineKey: string; name: string } | null>(null);
  const [editSemi, setEditSemi] = React.useState(false);
  const [confirmDelete, setConfirmDelete] = React.useState(false);
  const [loadedFor, setLoadedFor] = React.useState<string>('');

  const loadKey = `${target}:${id}`;

  // Заполняем форму один раз на открытие
  React.useEffect(() => {
    if (!open) { setLoadedFor(''); return; }
    if (!data || loadedFor === loadKey) return;
    const card = data.card;
    setLines(card ? fromCard(card) : [emptyLine()]);
    setOutputWeight(card?.outputWeight ? String(card.outputWeight) : '');
    setYieldQuantity(card ? String(card.yieldQuantity) : '1');
    setInstructions(card?.instructions ?? '');
    setError('');
    setCopyOpen(false);
    setConfirmDelete(false);
    setLoadedFor(loadKey);
  }, [open, data, loadKey, loadedFor]);

  const itemsById = React.useMemo(() => new Map(allItems.map(i => [i.id, i])), [allItems]);
  const options = React.useMemo(
    () => allItems.filter(i => !(target === 'semi' && i.id === id)),
    [allItems, target, id]
  );

  const update = (key: string, patch: Partial<LineState>) => setLines(ls => ls.map(l => (l.key === key ? { ...l, ...patch } : l)));
  const choose = (l: LineState, item: StockItem) => update(l.key, { stockItemId: item.id, name: item.name, unit: item.unit, kind: item.kind });

  const dishOptions = React.useMemo(
    () => (dishesData?.dishes ?? []).filter(d => !(target === 'dish' && d.id === id)),
    [dishesData, target, id]
  );
  const dishesById = React.useMemo(() => new Map((dishesData?.dishes ?? []).map(d => [d.id, d])), [dishesData]);

  const lineGross = (l: LineState) => (l.kind === 'dish' ? num(l.amount) : num(l.amount) / factor(l.unit));
  const lineCost = (l: LineState) =>
    l.subDishId ? num(l.amount) * (dishesById.get(l.subDishId)?.cost ?? 0)
      : l.stockItemId ? lineGross(l) * (itemsById.get(l.stockItemId)?.unitCost ?? 0) : 0;
  const totalCost = lines.reduce((s, l) => s + lineCost(l), 0);
  const yieldNum = num(yieldQuantity) || 1;
  const portionCost = target === 'semi' ? totalCost / yieldNum : totalCost;
  const price = data?.price ?? null;
  const foodCost = price && price > 0 ? Math.round((totalCost / price) * 1000) / 10 : null;
  const netWeight = lines.reduce((s, l) => s + (l.unit === 'kg' ? num(l.amount) * (1 - num(l.loss) / 100) : 0), 0);

  const handleSave = async () => {
    setError('');
    const filled = lines.filter(l => l.stockItemId || l.subDishId);
    if (filled.length === 0) { setError('Добавьте хотя бы один ингредиент'); return; }
    const bad = filled.find(l => num(l.amount) <= 0);
    if (bad) { setError(`Укажите количество для «${bad.name}»`); return; }
    if (lines.some(l => l.kind !== 'dish' && !l.stockItemId && l.amount)) { setError('В одной из строк не выбран ингредиент'); return; }
    if (target === 'semi' && num(yieldQuantity) <= 0) { setError('Укажите выход полуфабриката'); return; }

    try {
      await save.mutateAsync({
        target,
        id,
        data: {
          outputWeight: outputWeight ? num(outputWeight) : null,
          yieldQuantity: target === 'semi' ? num(yieldQuantity) : 1,
          instructions: instructions.trim() || null,
          lines: filled.map(l => (l.subDishId
            ? { subDishId: l.subDishId, gross: num(l.amount), lossPercent: 0 }
            : { stockItemId: l.stockItemId!, gross: lineGross(l), lossPercent: num(l.loss) })),
        },
      });
      onClose();
    } catch (e) {
      setError(apiError(e));
    }
  };

  const handleDelete = async () => {
    try {
      await remove.mutateAsync({ target, id });
      onClose();
    } catch (e) {
      setError(apiError(e));
      setConfirmDelete(false);
    }
  };

  const copyFrom = async (dish: DishRow) => {
    try {
      const res = await fetchTechCard('dish', dish.id);
      const source = res.card ?? res.inherited;
      if (!source) { setError('У этого блюда нет техкарты'); return; }
      setLines(fromCard(source));
      if (source.outputWeight) setOutputWeight(String(source.outputWeight));
      if (source.instructions && !instructions) setInstructions(source.instructions);
      setCopyOpen(false);
    } catch (e) {
      setError(apiError(e));
    }
  };

  const semiItem = target === 'semi' ? itemsById.get(id) : undefined;
  const sourceDishes = (dishesData?.dishes ?? []).filter(d => d.techCard === 'own' && !(target === 'dish' && d.id === id));
  const busy = save.isPending || remove.isPending;
  const subtitle = target === 'dish' ? 'Техкарта блюда' : target === 'addon' ? 'Техкарта добавки' : 'Техкарта полуфабриката';

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="md" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, pr: 1.5 }}>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant="body2" color="text.secondary">{subtitle}</Typography>
          {data?.ownerName ?? '…'}
        </Box>
        {semiItem && (
          <IconButton aria-label="Изменить позицию" onClick={() => setEditSemi(true)}><EditIcon /></IconButton>
        )}
        <IconButton aria-label="Закрыть" onClick={onClose} disabled={busy}><CloseIcon /></IconButton>
      </DialogTitle>

      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
        {isLoading || !data ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
        ) : (
          <>
            {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}

            {!data.exists && data.inherited && loadedFor === loadKey && lines.every(l => !l.stockItemId) && (
              <Alert
                severity="info"
                action={<Button color="inherit" size="small" onClick={() => setLines(fromCard(data.inherited!))}>Взять за основу</Button>}
              >
                Сейчас для этого варианта списывается общая техкарта карточки ({fmtMoney(data.inherited.cost)}). Заполните отдельную, если расход отличается.
              </Alert>
            )}

            {/* Итоги */}
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: target === 'semi' ? 'repeat(2, 1fr)' : 'repeat(4, 1fr)' }, gap: 1 }}>
              {(target === 'semi'
                ? [
                    { label: 'Себестоимость партии', value: fmtMoney(Math.round(totalCost * 100) / 100) },
                    { label: `За 1 ${semiItem ? UNIT_LABELS[semiItem.unit] : 'ед.'}`, value: fmtMoney(Math.round(portionCost * 100) / 100) },
                  ]
                : [
                    { label: 'Себестоимость', value: fmtMoney(Math.round(totalCost * 100) / 100) },
                    { label: 'Цена продажи', value: fmtMoney(price) },
                    { label: 'Маржа', value: price ? fmtMoney(Math.round((price - totalCost) * 100) / 100) : '—' },
                    { label: 'Фудкост', value: <FoodCost value={foodCost} /> },
                  ]
              ).map(s => (
                <Box key={s.label} sx={{ bgcolor: t.surfaceMuted, borderRadius: '14px', p: 1.5 }}>
                  <Typography variant="caption" color="text.secondary">{s.label}</Typography>
                  <Typography sx={{ fontWeight: 800, fontSize: '1.15rem' }}>{s.value}</Typography>
                </Box>
              ))}
            </Box>

            {/* Состав */}
            <Box>
              <Box sx={{ display: 'flex', alignItems: 'center', mb: 1, gap: 1 }}>
                <Typography sx={{ fontWeight: 700, flex: 1 }}>Состав</Typography>
                {target !== 'semi' && sourceDishes.length > 0 && (
                  <Button size="small" startIcon={<CopyIcon />} onClick={() => setCopyOpen(o => !o)} sx={{ bgcolor: t.surfaceMuted }}>
                    Скопировать из блюда
                  </Button>
                )}
              </Box>

              {copyOpen && (
                <Autocomplete
                  size="small"
                  options={sourceDishes}
                  // Ключ — по id: у одноимённых позиций одинаковое название, и список путался
                  getOptionKey={d => d.id}
                  groupBy={d => d.category || 'Без раздела'}
                  getOptionLabel={d => d.name}
                  onChange={(_, d) => d && copyFrom(d)}
                  openOnFocus
                  renderInput={params => <TextField {...params} autoFocus placeholder="Блюдо, из которого взять состав" />}
                  sx={{ mb: 1.5 }}
                />
              )}

              <Box
                sx={{
                  display: { xs: 'none', md: 'grid' },
                  gridTemplateColumns: 'minmax(0,1fr) 130px 100px 90px 100px 40px',
                  gap: 1,
                  color: 'text.secondary',
                  fontSize: '0.8rem',
                  fontWeight: 600,
                  pb: 0.5,
                }}
              >
                <span>Ингредиент</span><span>Брутто</span><span>Потери</span>
                <span style={{ textAlign: 'right' }}>Нетто</span><span style={{ textAlign: 'right' }}>Стоимость</span><span />
              </Box>

              {lines.map((l, index) => {
                if (l.kind === 'dish') {
                  const dish = l.subDishId ? dishesById.get(l.subDishId) ?? null : null;
                  const value = dish ?? (l.subDishId ? ({ id: l.subDishId, name: l.name, cost: null } as DishRow) : null);
                  return (
                    <Box
                      key={l.key}
                      sx={{
                        display: 'grid',
                        gridTemplateColumns: { xs: '1fr 1fr 40px', md: 'minmax(0,1fr) 130px 100px 90px 100px 40px' },
                        gridTemplateAreas: { xs: '"item item del" "gross loss del" "info info info"', md: 'none' },
                        gap: 1,
                        alignItems: 'start',
                        py: 1.25,
                        borderTop: index ? '1px solid' : 0,
                        borderColor: 'divider',
                      }}
                    >
                      <Autocomplete<DishRow, false, false, false>
                        size="small"
                        options={dishOptions}
                        // Ключ — по id: у одноимённых позиций одинаковое название, и список путался
                        getOptionKey={d => d.id}
                        value={value}
                        groupBy={d => d.category || 'Без раздела'}
                        onChange={(_, d) => d && update(l.key, { subDishId: d.id, name: d.name })}
                        getOptionLabel={d => d.name}
                        isOptionEqualToValue={(a, b) => a.id === b.id}
                        renderOption={(props, d) => {
                          const { key, ...rest } = props as typeof props & { key: string };
                          return (
                            <li key={key} {...rest}>
                              <Box sx={{ flex: 1, minWidth: 0 }}>
                                <Typography noWrap>{d.name}</Typography>
                                <Typography variant="caption" color="text.secondary">
                                  {d.techCard === 'none' ? 'нет техкарты — не спишется' : `себестоимость ${fmtMoney(d.cost)}`}
                                </Typography>
                              </Box>
                            </li>
                          );
                        }}
                        renderInput={params => (
                          <TextField {...params} placeholder="Блюдо из меню"
                            InputProps={{ ...params.InputProps, startAdornment: <><DishIcon fontSize="small" sx={{ color: 'text.secondary', ml: 0.5 }} />{params.InputProps.startAdornment}</> }} />
                        )}
                        sx={{ gridArea: { xs: 'item', md: 'auto' } }}
                      />
                      <TextField
                        size="small"
                        type="number"
                        value={l.amount}
                        onChange={e => update(l.key, { amount: e.target.value })}
                        sx={{ gridArea: { xs: 'gross', md: 'auto' } }}
                        slotProps={{
                          input: { endAdornment: <InputAdornment position="end">порц</InputAdornment> },
                          htmlInput: { min: 0, step: 'any', inputMode: 'decimal', 'aria-label': 'Порций' },
                        }}
                      />
                      <ToggleButtonGroup
                        exclusive
                        size="small"
                        value={PORTIONS.find(([v]) => Math.abs(Number(v) - num(l.amount)) < 0.001)?.[0] ?? null}
                        onChange={(_, v) => v && update(l.key, { amount: v })}
                        sx={{ gridArea: { xs: 'loss', md: 'auto' }, height: 40, '& .MuiToggleButton-root': { px: 0.9, flex: 1 } }}
                      >
                        {PORTIONS.map(([v, label]) => <ToggleButton key={v} value={v} aria-label={`${label} порции`}>{label}</ToggleButton>)}
                      </ToggleButtonGroup>
                      <Box sx={{ display: { xs: 'none', md: 'flex' }, alignItems: 'center', justifyContent: 'flex-end', height: 40, color: 'text.secondary' }}>
                        {l.amount ? fmtPortions(num(l.amount)) : ''}
                      </Box>
                      <Box sx={{ display: { xs: 'none', md: 'flex' }, alignItems: 'center', justifyContent: 'flex-end', height: 40, fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>
                        {l.subDishId && l.amount ? fmtMoney(Math.round(lineCost(l) * 100) / 100) : ''}
                      </Box>
                      <IconButton
                        aria-label="Удалить строку"
                        onClick={() => setLines(ls => (ls.length > 1 ? ls.filter(x => x.key !== l.key) : [emptyLine()]))}
                        sx={{ gridArea: { xs: 'del', md: 'auto' }, color: 'text.secondary' }}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                      {dish && dish.techCard === 'none' && (
                        <Typography variant="caption" color="warning.main" sx={{ gridArea: { xs: 'info', md: 'auto' }, gridColumn: { md: '1 / -1' } }}>
                          У «{dish.name}» нет техкарты — при продаже по этой строке ничего не спишется
                        </Typography>
                      )}
                    </Box>
                  );
                }
                const selected = l.stockItemId
                  ? itemsById.get(l.stockItemId) ?? ({ id: l.stockItemId, name: l.name, unit: l.unit ?? 'kg', kind: l.kind ?? 'raw' } as StockItem)
                  : null;
                const net = num(l.amount) * (1 - num(l.loss) / 100);
                const su = smallUnit(l.unit);
                return (
                  <Box
                    key={l.key}
                    sx={{
                      display: 'grid',
                      gridTemplateColumns: { xs: '1fr 1fr 40px', md: 'minmax(0,1fr) 130px 100px 90px 100px 40px' },
                      gridTemplateAreas: { xs: '"item item del" "gross loss del" "info info info"', md: 'none' },
                      gap: 1,
                      alignItems: 'start',
                      py: 1.25,
                      borderTop: index ? '1px solid' : 0,
                      borderColor: 'divider',
                    }}
                  >
                    <Autocomplete<ItemOption, false, false, false>
                      size="small"
                      options={options}
                      // Ключ — по id: у одноимённых позиций одинаковое название, и список путался
                      getOptionKey={o => (isCreate(o) ? `new:${o.inputValue}` : o.id)}
                      value={selected}
                      onChange={(_, v) => {
                        if (!v) return;
                        if (isCreate(v)) setNewItem({ lineKey: l.key, name: v.inputValue });
                        else choose(l, v);
                      }}
                      filterOptions={(opts, params) => {
                        const filtered = itemFilter(opts as StockItem[], params) as ItemOption[];
                        const q = params.inputValue.trim();
                        if (q && !(opts as StockItem[]).some(o => o.name.toLowerCase() === q.toLowerCase()))
                          filtered.push({ create: true, inputValue: q });
                        return filtered;
                      }}
                      getOptionLabel={o => (isCreate(o) ? o.inputValue : o.name)}
                      isOptionEqualToValue={(a, b) => !isCreate(a) && !isCreate(b) && a.id === b.id}
                      renderOption={(props, o) => {
                        const { key, ...rest } = props as typeof props & { key: string };
                        return isCreate(o) ? (
                          <li key={key} {...rest}><AddIcon fontSize="small" sx={{ mr: 1 }} />Создать «{o.inputValue}»</li>
                        ) : (
                          <li key={key} {...rest}>
                            <Box sx={{ flex: 1, minWidth: 0 }}>
                              <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
                                <Typography noWrap>{o.name}</Typography>
                                {o.kind === 'semi' && <Pill>п/ф</Pill>}
                              </Box>
                              <Typography variant="caption" color="text.secondary">
                                {fmtMoney(o.unitCost)}/{UNIT_LABELS[o.unit]}{o.category ? ` · ${o.category}` : ''}
                              </Typography>
                            </Box>
                          </li>
                        );
                      }}
                      noOptionsText="Начните вводить название"
                      renderInput={params => <TextField {...params} placeholder="Ингредиент" />}
                      sx={{ gridArea: { xs: 'item', md: 'auto' } }}
                    />
                    <TextField
                      size="small"
                      type="number"
                      placeholder="Брутто"
                      value={l.amount}
                      onChange={e => update(l.key, { amount: e.target.value })}
                      sx={{ gridArea: { xs: 'gross', md: 'auto' } }}
                      slotProps={{
                        input: { endAdornment: su ? <InputAdornment position="end">{su}</InputAdornment> : undefined },
                        htmlInput: { min: 0, step: 'any', inputMode: 'decimal', 'aria-label': 'Брутто' },
                      }}
                    />
                    <TextField
                      size="small"
                      type="number"
                      placeholder="0"
                      value={l.loss}
                      onChange={e => update(l.key, { loss: e.target.value })}
                      sx={{ gridArea: { xs: 'loss', md: 'auto' } }}
                      slotProps={{
                        input: { endAdornment: <InputAdornment position="end">%</InputAdornment> },
                        htmlInput: { min: 0, max: 99, step: 'any', inputMode: 'decimal', 'aria-label': 'Потери, %' },
                      }}
                    />
                    <Box sx={{ display: { xs: 'none', md: 'flex' }, alignItems: 'center', justifyContent: 'flex-end', height: 40, color: 'text.secondary', fontVariantNumeric: 'tabular-nums' }}>
                      {l.amount ? `${Math.round(net * 10) / 10} ${su}` : ''}
                    </Box>
                    <Box sx={{ display: { xs: 'none', md: 'flex' }, alignItems: 'center', justifyContent: 'flex-end', height: 40, fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>
                      {l.stockItemId && l.amount ? fmtMoney(Math.round(lineCost(l) * 100) / 100) : ''}
                    </Box>
                    <IconButton
                      aria-label="Удалить строку"
                      onClick={() => setLines(ls => (ls.length > 1 ? ls.filter(x => x.key !== l.key) : [emptyLine()]))}
                      sx={{ gridArea: { xs: 'del', md: 'auto' }, color: 'text.secondary' }}
                    >
                      <DeleteIcon fontSize="small" />
                    </IconButton>
                    {l.stockItemId && l.amount && (
                      <Typography variant="caption" color="text.secondary" sx={{ gridArea: 'info', display: { md: 'none' } }}>
                        Нетто {Math.round(net * 10) / 10} {su} · {fmtMoney(Math.round(lineCost(l) * 100) / 100)}
                      </Typography>
                    )}
                  </Box>
                );
              })}
              <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', mt: 1 }}>
                <Button startIcon={<AddIcon />} onClick={() => setLines(ls => [...ls, emptyLine()])} sx={{ bgcolor: t.surfaceMuted }}>
                  Добавить ингредиент
                </Button>
                {target !== 'semi' && (
                  <Button startIcon={<DishIcon />} onClick={() => setLines(ls => [...ls.filter(x => x.stockItemId || x.subDishId || x.amount), emptyDishLine()])} sx={{ bgcolor: t.surfaceMuted }}>
                    Добавить блюдо
                  </Button>
                )}
              </Box>
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
                {target === 'semi'
                  ? 'Полуфабрикат собирается из товаров и других полуфабрикатов. Брутто — сколько берётся со склада, потери — очистка, ужарка; выход партии — сколько получилось.'
                  : 'Блюдо собирается из товаров, полуфабрикатов и других блюд (целиком или долей: ½, ⅓). Брутто — сколько берётся со склада; потери на списание не влияют, нужны для расчёта выхода.'}
              </Typography>
            </Box>

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr' }, gap: 1.5 }}>
              {target === 'semi' ? (
                <TextField
                  label="Выход партии"
                  type="number"
                  value={yieldQuantity}
                  onChange={e => setYieldQuantity(e.target.value)}
                  helperText="Сколько полуфабриката получается из этого состава"
                  slotProps={{
                    input: { endAdornment: <InputAdornment position="end">{semiItem ? UNIT_LABELS[semiItem.unit] : ''}</InputAdornment> },
                    htmlInput: { min: 0, step: 'any', inputMode: 'decimal' },
                  }}
                />
              ) : (
                <TextField
                  label="Выход готового блюда"
                  type="number"
                  value={outputWeight}
                  onChange={e => setOutputWeight(e.target.value)}
                  helperText={netWeight > 0 ? `Сумма нетто по весовым ингредиентам: ${Math.round(netWeight)} г` : 'Для справки и печати'}
                  slotProps={{
                    input: { endAdornment: <InputAdornment position="end">г</InputAdornment> },
                    htmlInput: { min: 0, step: 'any', inputMode: 'decimal' },
                  }}
                />
              )}
            </Box>

            <TextField
              label="Технология приготовления"
              value={instructions}
              onChange={e => setInstructions(e.target.value)}
              multiline
              minRows={2}
              placeholder="Порядок сборки, температура, время…"
              slotProps={{ htmlInput: { maxLength: 4000 } }}
            />

            {confirmDelete && (
              <Alert
                severity="warning"
                action={
                  <Box sx={{ display: 'flex', gap: 1 }}>
                    <Button size="small" color="inherit" onClick={() => setConfirmDelete(false)}>Нет</Button>
                    <Button size="small" variant="contained" color="error" onClick={handleDelete}>Удалить</Button>
                  </Box>
                }
              >
                Удалить техкарту? По этой позиции перестанут списываться продукты.
              </Alert>
            )}
          </>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 3, pt: 1.5, gap: 1, borderTop: '1px solid', borderColor: 'divider' }}>
        {data?.exists && (
          <Button onClick={() => setConfirmDelete(true)} disabled={busy} sx={{ color: tokens.chili, mr: 'auto' }}>
            Удалить техкарту
          </Button>
        )}
        <Button onClick={onClose} disabled={busy} sx={{ color: 'text.secondary', ml: data?.exists ? 0 : 'auto' }}>Отмена</Button>
        <Button
          variant="contained"
          color="secondary"
          onClick={handleSave}
          disabled={busy || isLoading}
          startIcon={save.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}
        >
          Сохранить
        </Button>
      </DialogActions>

      <StockItemDialog
        open={!!newItem}
        initialName={newItem?.name}
        onClose={() => setNewItem(null)}
        onSaved={(item) => {
          const line = lines.find(l => l.key === newItem?.lineKey);
          if (line) choose(line, item);
        }}
      />
      {semiItem && <StockItemDialog open={editSemi} item={semiItem} onClose={() => setEditSemi(false)} />}
    </Dialog>
  );
};

export default TechCardEditor;
