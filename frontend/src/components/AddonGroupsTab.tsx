import * as React from 'react';
import { Box, Typography, IconButton, Tooltip, Button, Collapse, TextField, InputAdornment, useTheme, alpha } from '@mui/material';
import {
  Add as AddIcon,
  EditOutlined as EditIcon,
  DeleteOutline as DeleteIcon,
  ExpandMore as ExpandMoreIcon,
  Search as SearchIcon,
  Category as CategoryIcon,
} from '@mui/icons-material';
import type { Addon, AddonCategory } from '../types';
import { Pill, EmptyState } from '../pages/inventory/ui';
import { plural } from '../utils/plural';
import { tokens } from '../theme';

interface AddonGroupsTabProps {
  categories: AddonCategory[];
  expandedId: number | null;
  onToggle: (categoryId: number) => void;
  onCreateGroup: () => void;
  onEditGroup: (category: AddonCategory) => void;
  onDeleteGroup: (category: AddonCategory) => void;
  onCreateAddon: (categoryId: number) => void;
  onEditAddon: (addon: Addon, categoryId: number) => void;
  onDeleteAddon: (addonId: number) => void;
}

// Сколько названий добавок показывать в свёрнутой группе
const PREVIEW_COUNT = 4;

const ADDON_FORMS: [string, string, string] = ['добавка', 'добавки', 'добавок'];
const GROUP_FORMS: [string, string, string] = ['группа', 'группы', 'групп'];

/** Правило выбора для покупателя: «до 1», «от 1 до 3»… null — без ограничений */
const selectionRule = ({ minSelections: min, maxSelections: max }: AddonCategory): string | null => {
  if (max > 0 && min > 0) return min === max ? `ровно ${max}` : `от ${min} до ${max}`;
  if (max > 0) return `до ${max}`;
  if (min > 0) return `от ${min}`;
  return null;
};

const previewText = (addons: Addon[]): string => {
  const names = addons.slice(0, PREVIEW_COUNT).map(a => a.name).join(', ');
  const rest = addons.length - PREVIEW_COUNT;
  return rest > 0 ? `${names} и ещё ${rest}` : names;
};

/** Группы с подходящими добавками; если совпало название группы — показываем её целиком */
const filterCategories = (categories: AddonCategory[], query: string): AddonCategory[] => {
  const q = query.trim().toLowerCase();
  if (!q) return categories;
  return categories
    .map(c => (c.name.toLowerCase().includes(q)
      ? c
      : { ...c, addons: (c.addons ?? []).filter(a => a.name.toLowerCase().includes(q)) }))
    .filter(c => c.name.toLowerCase().includes(q) || c.addons.length > 0);
};

// Удаление — приглушённая иконка, краснеет только при наведении: в списке не должно быть столбца красных корзин
const dangerHover = { color: 'text.secondary', '&:hover': { color: tokens.chili, bgcolor: alpha(tokens.chili, 0.08) } };

// Кнопка без оформления: кликабельная область строки или заголовка
const unstyledButton = {
  p: 0,
  border: 0,
  bgcolor: 'transparent',
  color: 'inherit',
  font: 'inherit',
  textAlign: 'left',
  cursor: 'pointer',
} as const;

const AddonRow: React.FC<{
  addon: Addon;
  onEdit: () => void;
  onDelete: () => void;
}> = ({ addon, onEdit, onDelete }) => (
  <Box
    sx={{
      display: 'flex',
      alignItems: 'center',
      gap: { xs: 1, sm: 1.5 },
      pl: { xs: 2, sm: 7 },
      pr: { xs: 1, sm: 1.5 },
      py: 1,
      borderTop: '1px solid',
      borderColor: 'divider',
      '&:hover': { bgcolor: 'action.hover' },
      '&:hover .row-actions, &:focus-within .row-actions': { opacity: 1 },
    }}
  >
    <Box component="button" type="button" onClick={onEdit} sx={{ ...unstyledButton, flex: 1, minWidth: 0 }}>
      <Typography noWrap sx={{ fontWeight: 500, color: addon.isAvailable ? 'text.primary' : 'text.secondary' }}>
        {addon.name}
      </Typography>
      {addon.description && (
        <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
          {addon.description}
        </Typography>
      )}
    </Box>
    {!addon.isAvailable && <Pill color={tokens.chili} bg={alpha(tokens.chili, 0.1)}>в стопе</Pill>}
    <Typography sx={{ fontWeight: 700, minWidth: { xs: 0, sm: 72 }, textAlign: 'right', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
      {addon.price > 0 ? `+${addon.price} ₽` : 'бесплатно'}
    </Typography>
    <Box className="row-actions" sx={{ display: 'flex', opacity: { xs: 1, md: 0.4 }, transition: 'opacity 0.15s ease' }}>
      <Tooltip title="Изменить добавку">
        <IconButton size="small" onClick={onEdit} aria-label={`Изменить «${addon.name}»`} sx={{ display: { xs: 'none', sm: 'inline-flex' } }}>
          <EditIcon fontSize="small" />
        </IconButton>
      </Tooltip>
      <Tooltip title="Удалить добавку">
        <IconButton size="small" onClick={onDelete} sx={dangerHover} aria-label={`Удалить «${addon.name}»`}>
          <DeleteIcon fontSize="small" />
        </IconButton>
      </Tooltip>
    </Box>
  </Box>
);

const AddonGroup: React.FC<{
  category: AddonCategory;
  /** Сколько добавок в группе без учёта поиска */
  total: number;
  searching: boolean;
  open: boolean;
  onToggle: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onCreateAddon: () => void;
  onEditAddon: (addon: Addon) => void;
  onDeleteAddon: (addonId: number) => void;
}> = ({ category, total, searching, open, onToggle, onEdit, onDelete, onCreateAddon, onEditAddon, onDeleteAddon }) => {
  const theme = useTheme();
  const addons = category.addons ?? [];
  const stopped = addons.filter(a => !a.isAvailable).length;
  const rule = selectionRule(category);
  const count = searching
    ? `найдено ${addons.length} из ${total}`
    : addons.length > 0 ? `${addons.length} ${plural(addons.length, ADDON_FORMS)}` : 'Пока пусто';
  const preview = !searching && addons.length > 0 ? previewText(addons) : null;

  return (
    <Box sx={{ border: '1px solid', borderColor: 'divider', borderRadius: '16px', overflow: 'hidden', bgcolor: 'background.paper' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, pl: { xs: 1, sm: 1.5 }, pr: { xs: 1, sm: 1.5 }, py: 1.25 }}>
        <Box
          component="button"
          type="button"
          onClick={onToggle}
          aria-expanded={open}
          sx={{
            flex: 1,
            minWidth: 0,
            display: 'flex',
            alignItems: 'center',
            ...unstyledButton,
            gap: 1.25,
            p: 0.5,
            borderRadius: '10px',
          }}
        >
          <ExpandMoreIcon
            sx={{ color: 'text.secondary', transition: 'transform 0.2s ease', transform: open ? 'rotate(180deg)' : 'none', flexShrink: 0 }}
          />
          <Box sx={{ minWidth: 0 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, flexWrap: 'wrap' }}>
              <Typography sx={{ fontWeight: 700, fontSize: '1.05rem' }}>{category.name}</Typography>
              {category.isRequired && (
                <Pill color={theme.palette.text.primary} bg={alpha(tokens.saffron, 0.35)}>обязательно</Pill>
              )}
              {rule && <Pill>выбор {rule}</Pill>}
              {stopped > 0 && <Pill color={tokens.chili} bg={alpha(tokens.chili, 0.1)}>{stopped} в стопе</Pill>}
            </Box>
            <Typography variant="body2" color="text.secondary" noWrap sx={{ mt: 0.25 }}>
              {count}
              {preview && <Box component="span" sx={{ display: { xs: 'none', sm: 'inline' } }}> · {preview}</Box>}
            </Typography>
          </Box>
        </Box>
        <Tooltip title="Добавить добавку в группу">
          <IconButton size="small" onClick={onCreateAddon} aria-label={`Добавить добавку в «${category.name}»`} sx={{ display: { xs: 'none', sm: 'inline-flex' } }}>
            <AddIcon fontSize="small" />
          </IconButton>
        </Tooltip>
        <Tooltip title="Настройки группы">
          <IconButton size="small" onClick={onEdit} aria-label={`Настройки группы «${category.name}»`}>
            <EditIcon fontSize="small" />
          </IconButton>
        </Tooltip>
        <Tooltip title="Удалить группу">
          <IconButton size="small" onClick={onDelete} sx={dangerHover} aria-label={`Удалить группу «${category.name}»`}>
            <DeleteIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      </Box>

      <Collapse in={open} unmountOnExit>
        {category.description && (
          <Typography variant="body2" color="text.secondary" sx={{ pl: { xs: 2, sm: 7 }, pr: 2, pb: 1.25 }}>
            {category.description}
          </Typography>
        )}
        {addons.map(addon => (
          <AddonRow
            key={addon.id}
            addon={addon}
            onEdit={() => onEditAddon(addon)}
            onDelete={() => onDeleteAddon(addon.id)}
          />
        ))}
        <Box sx={{ borderTop: '1px solid', borderColor: 'divider', pl: { xs: 1, sm: 6 }, py: 0.75 }}>
          <Button size="small" startIcon={<AddIcon />} onClick={onCreateAddon}>
            Добавить добавку
          </Button>
        </Box>
      </Collapse>
    </Box>
  );
};

/** Вкладка «Добавки» в управлении меню: группы добавок со сводкой и компактным списком */
const AddonGroupsTab: React.FC<AddonGroupsTabProps> = ({
  categories, expandedId, onToggle, onCreateGroup, onEditGroup, onDeleteGroup, onCreateAddon, onEditAddon, onDeleteAddon,
}) => {
  const [query, setQuery] = React.useState('');
  const visible = React.useMemo(() => filterCategories(categories, query), [categories, query]);
  const searching = query.trim().length > 0;
  const addonsTotal = categories.reduce((sum, c) => sum + (c.addons?.length ?? 0), 0);
  const totals = React.useMemo(() => new Map(categories.map(c => [c.id, c.addons?.length ?? 0])), [categories]);

  if (categories.length === 0) {
    return (
      <EmptyState
        icon={<CategoryIcon />}
        title="Групп добавок пока нет"
        text="Соусы, сыр, размер порции — соберите их в группу и привяжите к товарам."
        action={<Button variant="contained" startIcon={<AddIcon />} onClick={onCreateGroup}>Создать группу</Button>}
      />
    );
  }

  return (
    <Box sx={{ px: { xs: 2, sm: 3 } }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap', mb: 2 }}>
        <TextField
          placeholder="Найти добавку или группу"
          value={query}
          onChange={e => setQuery(e.target.value)}
          size="small"
          sx={{ flex: '1 1 260px' }}
          slotProps={{
            input: { startAdornment: <InputAdornment position="start"><SearchIcon sx={{ color: 'text.secondary' }} /></InputAdornment> },
          }}
        />
        <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'nowrap' }}>
          {categories.length} {plural(categories.length, GROUP_FORMS)} · {addonsTotal} {plural(addonsTotal, ADDON_FORMS)}
        </Typography>
      </Box>

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.25 }}>
        {visible.map(category => (
          <AddonGroup
            key={category.id}
            category={category}
            total={totals.get(category.id) ?? category.addons.length}
            searching={searching}
            open={searching || expandedId === category.id}
            onToggle={() => onToggle(category.id)}
            onEdit={() => onEditGroup(category)}
            onDelete={() => onDeleteGroup(category)}
            onCreateAddon={() => onCreateAddon(category.id)}
            onEditAddon={addon => onEditAddon(addon, category.id)}
            onDeleteAddon={onDeleteAddon}
          />
        ))}
        {searching && visible.length === 0 && (
          <Typography color="text.secondary" sx={{ textAlign: 'center', py: 4 }}>
            Ничего не нашлось по запросу «{query.trim()}»
          </Typography>
        )}
      </Box>
    </Box>
  );
};

export default AddonGroupsTab;
