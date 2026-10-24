import * as React from 'react';
import {
  Box, Typography, CircularProgress, TextField, InputAdornment, ToggleButtonGroup, ToggleButton, LinearProgress,
} from '@mui/material';
import { Search as SearchIcon, RestaurantMenuOutlined as DishIcon, ChevronRight as ChevronRightIcon } from '@mui/icons-material';
import { useDishes, fmtMoney, type DishRow, type AddonRow, type TechCardTarget } from '../../api/inventory';
import { tokens } from '../../theme';
import { Panel, ListRow, ListHeader, EmptyState, Pill, FoodCost } from './ui';

interface Props {
  onOpenCard: (target: TechCardTarget, id: number) => void;
}

type Filter = 'all' | 'none' | 'expensive';

const CardStatus: React.FC<{ status: DishRow['techCard'] }> = ({ status }) =>
  status === 'own' ? null
    : status === 'parent' ? <Pill>общая карта</Pill>
      : <Pill color="#C77700" bg="rgba(232,137,12,0.12)">нет техкарты</Pill>;

const DishesTab: React.FC<Props> = ({ onOpenCard }) => {
  const { data, isLoading } = useDishes();
  const [search, setSearch] = React.useState('');
  const [filter, setFilter] = React.useState<Filter>('all');
  const [section, setSection] = React.useState<'dishes' | 'addons'>('dishes');

  const q = search.trim().toLowerCase();
  const match = (r: { name: string; category: string; techCard: string; foodCostPercent: number | null }) =>
    (!q || r.name.toLowerCase().includes(q) || r.category?.toLowerCase().includes(q))
    && (filter === 'all' || (filter === 'none' ? r.techCard === 'none' : (r.foodCostPercent ?? 0) > 40));

  const dishes = (data?.dishes ?? []).filter(match);
  const addons = (data?.addons ?? []).filter(match);
  const all = data?.dishes ?? [];
  const covered = all.filter(d => d.techCard !== 'none').length;

  // Группировка блюд по разделам меню
  const groups = React.useMemo(() => {
    const map = new Map<string, DishRow[]>();
    dishes.forEach(d => {
      const key = d.category || 'Без раздела';
      map.set(key, [...(map.get(key) ?? []), d]);
    });
    return [...map.entries()];
  }, [dishes]);

  const addonGroups = React.useMemo(() => {
    const map = new Map<string, AddonRow[]>();
    addons.forEach(a => map.set(a.category, [...(map.get(a.category) ?? []), a]));
    return [...map.entries()];
  }, [addons]);

  const renderRow = (r: DishRow | AddonRow, target: TechCardTarget) => (
    <ListRow key={`${target}-${r.id}`} onClick={() => onOpenCard(target, r.id)} muted={!r.isAvailable}>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <Typography sx={{ fontWeight: 600 }}>{'variantName' in r && r.variantName ? r.dishName : r.name}</Typography>
          {'variantName' in r && r.variantName && <Typography color="text.secondary">{r.variantName}</Typography>}
          <CardStatus status={r.techCard} />
        </Box>
        <Typography variant="body2" color="text.secondary" sx={{ display: { md: 'none' } }}>
          {fmtMoney(r.price)}{r.cost !== null ? ` · себест. ${fmtMoney(r.cost)}` : ''}
        </Typography>
      </Box>
      <Box sx={{ width: 100, textAlign: 'right', display: { xs: 'none', md: 'block' }, fontVariantNumeric: 'tabular-nums' }}>{fmtMoney(r.price)}</Box>
      <Box sx={{ width: 120, textAlign: 'right', display: { xs: 'none', md: 'block' }, fontVariantNumeric: 'tabular-nums' }}>{fmtMoney(r.cost)}</Box>
      <Box sx={{ width: 110, textAlign: 'right', display: { xs: 'none', md: 'block' }, fontVariantNumeric: 'tabular-nums' }}>{fmtMoney(r.margin)}</Box>
      <Box sx={{ width: { md: 90 }, textAlign: 'right' }}><FoodCost value={r.foodCostPercent} /></Box>
      <ChevronRightIcon sx={{ color: 'text.secondary', display: { xs: 'none', sm: 'block' } }} />
    </ListRow>
  );

  const header = (
    <ListHeader>
      <Box sx={{ flex: 1 }}>Позиция</Box>
      <Box sx={{ width: 100, textAlign: 'right' }}>Цена</Box>
      <Box sx={{ width: 120, textAlign: 'right' }}>Себестоимость</Box>
      <Box sx={{ width: 110, textAlign: 'right' }}>Маржа</Box>
      <Box sx={{ width: 90, textAlign: 'right' }}>Фудкост</Box>
      <Box sx={{ width: 24 }} />
    </ListHeader>
  );

  if (isLoading) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {all.length > 0 && (
        <Panel>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: 1, gap: 2, flexWrap: 'wrap' }}>
            <Typography sx={{ fontWeight: 700 }}>Заполнено техкарт: {covered} из {all.length}</Typography>
            <Typography variant="body2" color="text.secondary">
              Без техкарты продажи не списывают продукты со склада
            </Typography>
          </Box>
          <LinearProgress
            variant="determinate"
            value={all.length ? (covered / all.length) * 100 : 0}
            sx={{ height: 8, bgcolor: 'action.hover', '& .MuiLinearProgress-bar': { bgcolor: tokens.herb } }}
          />
        </Panel>
      )}

      <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
        <ToggleButtonGroup exclusive size="small" value={section} onChange={(_, v) => v && setSection(v)}>
          <ToggleButton value="dishes">Блюда</ToggleButton>
          <ToggleButton value="addons">Добавки</ToggleButton>
        </ToggleButtonGroup>
        <TextField
          placeholder="Найти блюдо"
          value={search}
          onChange={e => setSearch(e.target.value)}
          size="small"
          sx={{ flex: '1 1 220px', '& .MuiOutlinedInput-root': { bgcolor: 'background.paper' } }}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon sx={{ color: 'text.secondary' }} /></InputAdornment> } }}
        />
        <ToggleButtonGroup exclusive size="small" value={filter} onChange={(_, v) => v && setFilter(v)}>
          <ToggleButton value="all">Все</ToggleButton>
          <ToggleButton value="none">Без техкарты</ToggleButton>
          <ToggleButton value="expensive">Фудкост &gt; 40 %</ToggleButton>
        </ToggleButtonGroup>
      </Box>

      {section === 'dishes' ? (
        groups.length === 0 ? (
          <Panel><EmptyState icon={<DishIcon />} title="Ничего не найдено" text={all.length ? 'Измените фильтр' : 'Сначала добавьте блюда в меню'} /></Panel>
        ) : groups.map(([category, rows]) => (
          <Box key={category}>
            <Typography sx={{ fontWeight: 800, fontSize: '1.1rem', mb: 1, px: 0.5 }}>{category}</Typography>
            <Panel padding={false}>
              {header}
              {rows.map(r => renderRow(r, 'dish'))}
            </Panel>
          </Box>
        ))
      ) : addonGroups.length === 0 ? (
        <Panel><EmptyState icon={<DishIcon />} title="Добавок не найдено" /></Panel>
      ) : addonGroups.map(([category, rows]) => (
        <Box key={category}>
          <Typography sx={{ fontWeight: 800, fontSize: '1.1rem', mb: 1, px: 0.5 }}>{category}</Typography>
          <Panel padding={false}>
            {header}
            {rows.map(r => renderRow(r, 'addon'))}
          </Panel>
        </Box>
      ))}
    </Box>
  );
};

export default DishesTab;
