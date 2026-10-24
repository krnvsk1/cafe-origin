import * as React from 'react';
import {
  Box, TextField, InputAdornment, Button, Typography, CircularProgress, Chip, FormControlLabel, Switch,
  ToggleButtonGroup, ToggleButton, useTheme,
} from '@mui/material';
import {
  Search as SearchIcon, Add as AddIcon, Inventory2Outlined as InventoryIcon, ChevronRight as ChevronRightIcon,
  WarningAmberRounded as WarningIcon,
} from '@mui/icons-material';
import {
  useStockItems, useStockCategories, fmtQty, fmtMoney, UNIT_LABELS, type StockItem,
} from '../../api/inventory';
import { tokens } from '../../theme';
import { Panel, ListRow, ListHeader, EmptyState, Pill } from './ui';
import StockItemDialog from './StockItemDialog';
import ItemHistoryDialog from './ItemHistoryDialog';

interface Props {
  /** stock — остатки сырья; catalog — вся номенклатура с полуфабрикатами и архивом */
  mode: 'stock' | 'catalog';
  lowOnlyDefault?: boolean;
  onOpenDocument?: (id: number) => void;
  onOpenSemiCard?: (item: StockItem) => void;
}

const StockTab: React.FC<Props> = ({ mode, lowOnlyDefault = false, onOpenDocument, onOpenSemiCard }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const [search, setSearch] = React.useState('');
  const [debounced, setDebounced] = React.useState('');
  const [category, setCategory] = React.useState('');
  const [lowOnly, setLowOnly] = React.useState(lowOnlyDefault);
  const [kind, setKind] = React.useState<'' | 'raw' | 'semi'>('');
  const [includeArchived, setIncludeArchived] = React.useState(false);
  const [editing, setEditing] = React.useState<StockItem | null | undefined>(undefined);
  const [history, setHistory] = React.useState<StockItem | null>(null);

  React.useEffect(() => setLowOnly(lowOnlyDefault), [lowOnlyDefault]);
  React.useEffect(() => {
    const id = setTimeout(() => setDebounced(search), 250);
    return () => clearTimeout(id);
  }, [search]);

  const { data: categories = [] } = useStockCategories();
  const { data: items = [], isLoading, isFetching } = useStockItems({
    search: debounced || undefined,
    category: category || undefined,
    kind: mode === 'stock' ? 'raw' : kind || undefined,
    lowOnly: mode === 'stock' ? lowOnly : undefined,
    includeArchived: mode === 'catalog' ? includeArchived : undefined,
  });

  const total = items.reduce((s, i) => s + i.value, 0);

  const openItem = (item: StockItem) => {
    if (item.kind === 'semi') onOpenSemiCard?.(item);
    else setHistory(item);
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {/* Фильтры */}
      <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
        <TextField
          placeholder="Найти позицию"
          value={search}
          onChange={e => setSearch(e.target.value)}
          size="small"
          sx={{ flex: '1 1 240px', '& .MuiOutlinedInput-root': { bgcolor: 'background.paper' } }}
          slotProps={{
            input: {
              startAdornment: <InputAdornment position="start"><SearchIcon sx={{ color: 'text.secondary' }} /></InputAdornment>,
              endAdornment: isFetching && !isLoading ? <CircularProgress size={16} /> : undefined,
            },
          }}
        />
        {mode === 'stock' ? (
          <FormControlLabel
            control={<Switch checked={lowOnly} onChange={e => setLowOnly(e.target.checked)} />}
            label="Заканчивается"
            sx={{ mr: 0 }}
          />
        ) : (
          <>
            <ToggleButtonGroup exclusive size="small" value={kind} onChange={(_, v) => v !== null && setKind(v)}>
              <ToggleButton value="">Все</ToggleButton>
              <ToggleButton value="raw">Сырьё</ToggleButton>
              <ToggleButton value="semi">Полуфабрикаты</ToggleButton>
            </ToggleButtonGroup>
            <FormControlLabel
              control={<Switch checked={includeArchived} onChange={e => setIncludeArchived(e.target.checked)} />}
              label="Архив"
              sx={{ mr: 0 }}
            />
          </>
        )}
        <Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={() => setEditing(null)} sx={{ ml: { md: 'auto' } }}>
          Позиция
        </Button>
      </Box>

      {categories.length > 0 && (
        <Box sx={{ display: 'flex', gap: 1, overflowX: 'auto', pb: 0.5, mx: { xs: -2, sm: 0 }, px: { xs: 2, sm: 0 }, scrollbarWidth: 'none' }}>
          <Chip label="Все категории" color={category === '' ? 'primary' : 'default'} onClick={() => setCategory('')} />
          {categories.map(c => (
            <Chip key={c} label={c} color={category === c ? 'primary' : 'default'} onClick={() => setCategory(category === c ? '' : c)} />
          ))}
        </Box>
      )}

      <Panel padding={false}>
        {isLoading ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress size={32} /></Box>
        ) : items.length === 0 ? (
          <EmptyState
            icon={<InventoryIcon />}
            title={debounced || category || lowOnly ? 'Ничего не найдено' : 'Склад пока пуст'}
            text={debounced || category || lowOnly
              ? 'Попробуйте изменить условия поиска'
              : 'Добавьте позиции вручную или проведите первый приход — позиции можно создавать прямо из накладной'}
            action={!debounced && !category && !lowOnly && (
              <Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={() => setEditing(null)}>Добавить позицию</Button>
            )}
          />
        ) : (
          <>
            <ListHeader>
              <Box sx={{ flex: 1 }}>Позиция</Box>
              <Box sx={{ width: 140, textAlign: 'right' }}>{mode === 'stock' ? 'Остаток' : 'Тип'}</Box>
              <Box sx={{ width: 120, textAlign: 'right' }}>Себестоимость</Box>
              <Box sx={{ width: 120, textAlign: 'right' }}>{mode === 'stock' ? 'Сумма' : 'Остаток'}</Box>
              <Box sx={{ width: 24 }} />
            </ListHeader>
            {items.map(item => (
              <ListRow key={item.id} onClick={() => openItem(item)} muted={item.isArchived}>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                    <Typography sx={{ fontWeight: 600 }}>{item.name}</Typography>
                    {item.isLow && (
                      <Pill color={tokens.chili} bg="rgba(226,70,44,0.1)">
                        <WarningIcon sx={{ fontSize: 14, mr: 0.25 }} />
                        {item.quantity < 0 ? 'Минус' : 'Заканчивается'}
                      </Pill>
                    )}
                    {item.kind === 'semi' && !item.hasTechCard && <Pill color="#C77700" bg="rgba(232,137,12,0.12)">Нет техкарты</Pill>}
                    {item.isArchived && <Pill>Архив</Pill>}
                  </Box>
                  <Typography variant="body2" color="text.secondary" noWrap>
                    {item.category || 'Без категории'}
                    <Box component="span" sx={{ display: { md: 'none' } }}>
                      {' · '}{fmtMoney(item.unitCost)}/{UNIT_LABELS[item.unit]}
                    </Box>
                  </Typography>
                </Box>

                {mode === 'stock' ? (
                  <Box sx={{ width: { md: 140 }, textAlign: 'right' }}>
                    <Typography sx={{ fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: item.isLow ? tokens.chili : 'text.primary' }}>
                      {fmtQty(item.quantity, item.unit)}
                    </Typography>
                    {item.minQuantity > 0 && (
                      <Typography variant="caption" color="text.secondary">мин. {fmtQty(item.minQuantity)}</Typography>
                    )}
                  </Box>
                ) : (
                  <Box sx={{ width: 140, textAlign: 'right', display: { xs: 'none', md: 'block' } }}>
                    <Pill bg={item.kind === 'semi' ? 'rgba(255,201,51,0.25)' : t.surfaceMuted} color="inherit">
                      {item.kind === 'semi' ? 'Полуфабрикат' : 'Сырьё'}
                    </Pill>
                  </Box>
                )}
                <Box sx={{ width: 120, textAlign: 'right', display: { xs: 'none', md: 'block' }, fontVariantNumeric: 'tabular-nums' }}>
                  {fmtMoney(item.unitCost)}<Typography component="span" color="text.secondary">/{UNIT_LABELS[item.unit]}</Typography>
                </Box>
                <Box sx={{ width: 120, textAlign: 'right', display: { xs: 'none', md: 'block' }, fontVariantNumeric: 'tabular-nums' }}>
                  {mode === 'stock' ? fmtMoney(item.value) : item.kind === 'semi' ? '—' : fmtQty(item.quantity, item.unit)}
                </Box>
                <ChevronRightIcon sx={{ color: 'text.secondary', display: { xs: 'none', sm: 'block' } }} />
              </ListRow>
            ))}
            {mode === 'stock' && (
              <Box sx={{ display: 'flex', justifyContent: 'space-between', px: { xs: 2, sm: 2.5 }, py: 1.75, borderTop: '1px solid', borderColor: 'divider' }}>
                <Typography color="text.secondary">{items.length} поз.</Typography>
                <Typography sx={{ fontWeight: 700 }}>Итого {fmtMoney(Math.round(total * 100) / 100)}</Typography>
              </Box>
            )}
          </>
        )}
      </Panel>

      <StockItemDialog open={editing !== undefined} item={editing} onClose={() => setEditing(undefined)} />
      <ItemHistoryDialog
        item={history}
        onClose={() => setHistory(null)}
        onEdit={(item) => { setHistory(null); setEditing(item); }}
        onOpenDocument={onOpenDocument ? (id) => { setHistory(null); onOpenDocument(id); } : undefined}
      />
    </Box>
  );
};

export default StockTab;
