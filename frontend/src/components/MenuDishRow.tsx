import * as React from 'react';
import {
  Box, Typography, IconButton, Switch, Tooltip, Menu, MenuItem, ListItemIcon, ListItemText, Chip, Collapse, Button, alpha, useTheme,
} from '@mui/material';
import {
  MoreVert as MoreIcon,
  Edit as EditIcon,
  Delete as DeleteIcon,
  Add as AddIcon,
  Link as LinkIcon,
  ExpandMore as ExpandMoreIcon,
  Restaurant as PlaceholderIcon,
} from '@mui/icons-material';
import type { Dish } from '../types';
import { resolveMediaUrl } from '../utils/media';
import { KitchenStationSelect } from './KitchenStationControls';
import { plural } from './CartPanel';

const rub = (v: number) => `${v.toLocaleString('ru-RU', { maximumFractionDigits: 2 })} ₽`;
const priceOf = (d: Dish, card: Dish) => `${rub(d.price)}${d.isWeighted || card.isWeighted ? ' / 100 г' : ''}`;

const imageOf = (d: Dish) => d.primaryImage || d.images?.find(i => i.isPrimary)?.filePath || d.images?.[0]?.filePath;

interface Props {
  card: Dish;
  variants: Dish[];
  dragHandle: React.ReactNode;
  /** Больше одной кухни — показываем выбор кухни */
  showKitchen: boolean;
  onEdit: (id: number) => void;
  onAddVariant: (cardId: number) => void;
  onLinkAddons: (card: Dish) => void;
  onDelete: (dish: Dish, isCard: boolean) => void;
  onAvailability: (ids: number[], isAvailable: boolean) => void;
  onError: (text: string) => void;
}

/**
 * Строка блюда в разделе: фото, название, варианты, цена, «в продаже» и действия в меню «⋯».
 * Нажатие на строку раскрывает варианты.
 */
const MenuDishRow: React.FC<Props> = ({
  card, variants, dragHandle, showKitchen, onEdit, onAddVariant, onLinkAddons, onDelete, onAvailability, onError,
}) => {
  const theme = useTheme();
  const [open, setOpen] = React.useState(false);
  const [menu, setMenu] = React.useState<HTMLElement | null>(null);

  const items = variants.length ? variants : [card];
  const onSale = items.filter(v => v.isAvailable).length;
  const allOff = onSale === 0;
  const partly = onSale > 0 && onSale < items.length;
  const prices = items.map(v => v.price);
  const min = Math.min(...prices);
  const max = Math.max(...prices);
  const img = imageOf(card) || variants.map(imageOf).find(Boolean);
  const single = variants.length === 1 && /^стандарт$/i.test(variants[0].name.trim());
  const subtitle = variants.length === 0
    ? 'Нет вариантов — добавьте, чтобы блюдо появилось в меню'
    : single
      ? (card.description || '')
      : `${variants.length} ${plural(variants.length, ['вариант', 'варианта', 'вариантов'])}: ${variants.map(v => v.name).join(', ')}`;

  const stop = (e: React.SyntheticEvent) => e.stopPropagation();

  return (
    <Box sx={{ bgcolor: 'background.paper' }}>
      <Box
        onClick={() => setOpen(o => !o)}
        sx={{
          display: 'flex', alignItems: 'center', gap: { xs: 1, sm: 1.5 }, px: { xs: 1, sm: 1.5 }, py: 1,
          cursor: 'pointer', '&:hover': { bgcolor: 'action.hover' },
        }}
      >
        <Box onClick={stop} sx={{ display: 'flex' }}>{dragHandle}</Box>

        <Box sx={{
          width: 48, height: 48, borderRadius: 2, overflow: 'hidden', flexShrink: 0,
          bgcolor: alpha(theme.palette.primary.main, 0.1), display: 'grid', placeItems: 'center',
          filter: allOff ? 'grayscale(1)' : undefined, opacity: allOff ? 0.6 : 1,
        }}>
          {img
            ? <Box component="img" src={resolveMediaUrl(img)} alt="" loading="lazy" sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
            : <PlaceholderIcon sx={{ color: 'primary.main', opacity: 0.7 }} />}
        </Box>

        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, minWidth: 0 }}>
            <Typography fontWeight={700} noWrap sx={{ color: allOff ? 'text.secondary' : 'text.primary' }}>{card.name}</Typography>
            {card.isWeighted && <Chip size="small" label="на вес" sx={{ height: 20, fontSize: '0.7rem' }} />}
            {allOff && items.length > 0 && <Chip size="small" label="не продаётся" sx={{ height: 20, fontSize: '0.7rem' }} />}
            {partly && <Chip size="small" color="warning" variant="outlined" label={`в продаже ${onSale} из ${items.length}`} sx={{ height: 20, fontSize: '0.7rem' }} />}
          </Box>
          <Typography variant="body2" color={variants.length === 0 ? 'warning.main' : 'text.secondary'} noWrap>
            {variants.length > 0 && (
              <Box component="span" sx={{ display: { xs: 'inline', sm: 'none' }, fontWeight: 700, color: 'text.primary', mr: subtitle ? 0.75 : 0 }}>
                {min === max ? priceOf(items[0], card) : `от ${rub(min)}`}
              </Box>
            )}
            {subtitle}
          </Typography>
        </Box>

        <Typography fontWeight={700} sx={{ whiteSpace: 'nowrap', display: { xs: 'none', sm: 'block' }, color: allOff ? 'text.secondary' : 'text.primary' }}>
          {variants.length === 0 ? '—' : min === max ? priceOf(items[0], card) : `от ${rub(min)}`}
        </Typography>

        {showKitchen && (
          <Box onClick={stop} sx={{ display: { xs: 'none', md: 'block' } }}>
            <KitchenStationSelect item={card} onError={onError} />
          </Box>
        )}

        <Tooltip title={allOff ? 'Вернуть в продажу' : 'Снять с продажи'}>
          <Switch
            checked={!allOff}
            onClick={stop}
            onChange={e => onAvailability(items.map(v => v.id), e.target.checked)}
            disabled={variants.length === 0}
            inputProps={{ 'aria-label': `«${card.name}» в продаже` }}
          />
        </Tooltip>

        <IconButton size="small" onClick={e => { stop(e); setMenu(e.currentTarget); }} aria-label={`Действия с «${card.name}»`}>
          <MoreIcon />
        </IconButton>
        <ExpandMoreIcon sx={{ color: 'text.secondary', transform: open ? 'rotate(180deg)' : 'none', transition: 'transform .2s', display: { xs: 'none', sm: 'block' } }} />
      </Box>

      <Menu anchorEl={menu} open={!!menu} onClose={() => setMenu(null)} onClick={() => setMenu(null)}>
        <MenuItem onClick={() => onEdit(card.id)}>
          <ListItemIcon><EditIcon fontSize="small" /></ListItemIcon><ListItemText>Изменить блюдо</ListItemText>
        </MenuItem>
        <MenuItem onClick={() => onAddVariant(card.id)}>
          <ListItemIcon><AddIcon fontSize="small" /></ListItemIcon><ListItemText>Добавить вариант</ListItemText>
        </MenuItem>
        <MenuItem onClick={() => onLinkAddons(card)}>
          <ListItemIcon><LinkIcon fontSize="small" /></ListItemIcon><ListItemText>Привязать добавки</ListItemText>
        </MenuItem>
        <MenuItem onClick={() => onDelete(card, true)} sx={{ color: 'error.main' }}>
          <ListItemIcon><DeleteIcon fontSize="small" color="error" /></ListItemIcon><ListItemText>Удалить блюдо</ListItemText>
        </MenuItem>
      </Menu>

      <Collapse in={open} unmountOnExit>
        <Box sx={{ pl: { xs: 2, sm: 9 }, pr: { xs: 1, sm: 1.5 }, pb: 1.5 }}>
          {variants.map(v => (
            <Box
              key={v.id}
              sx={{
                display: 'flex', alignItems: 'center', gap: 1.5, py: 0.5, pl: 1.5, pr: 0.5,
                borderLeft: `2px solid ${theme.palette.divider}`,
              }}
            >
              <Typography variant="body2" fontWeight={600} noWrap sx={{ flex: 1, minWidth: 0, color: v.isAvailable ? 'text.primary' : 'text.secondary' }}>
                {v.name}
              </Typography>
              <Typography variant="body2" fontWeight={700} sx={{ whiteSpace: 'nowrap' }}>{priceOf(v, card)}</Typography>
              {showKitchen && (
                <Box sx={{ display: { xs: 'none', md: 'block' } }}>
                  <KitchenStationSelect item={v} parent={card} onError={onError} />
                </Box>
              )}
              <Switch size="small" checked={v.isAvailable} onChange={e => onAvailability([v.id], e.target.checked)}
                inputProps={{ 'aria-label': `«${v.name}» в продаже` }} />
              <IconButton size="small" onClick={() => onEdit(v.id)} aria-label={`Изменить «${v.name}»`}><EditIcon sx={{ fontSize: 18 }} /></IconButton>
              <IconButton size="small" onClick={() => onDelete(v, false)} aria-label={`Удалить «${v.name}»`}
                sx={{ '&:hover': { color: 'error.main' } }}><DeleteIcon sx={{ fontSize: 18 }} /></IconButton>
            </Box>
          ))}
          <Button size="small" startIcon={<AddIcon />} onClick={() => onAddVariant(card.id)} sx={{ mt: 0.5, ml: 1 }}>
            Добавить вариант
          </Button>
        </Box>
      </Collapse>
    </Box>
  );
};

export default MenuDishRow;
