import * as React from 'react';
import { Box, Typography, IconButton, Button, useTheme } from '@mui/material';
import {
  Add as AddIcon,
  Favorite as FavoriteIcon,
  FavoriteBorder as FavoriteBorderIcon,
} from '@mui/icons-material';
import type { Dish } from '../types';
import { resolveMediaUrl } from '../utils/media';
import { tokens } from '../theme';
import { allowedOrderTypes, describeOrderTypes } from '../utils/orderTypes';

interface MenuItemCardProps {
  item: Dish;
  onOpen: () => void;
  isFavorite?: boolean;
  // Если не передан — кнопка «в избранное» не показывается (например, для гостей)
  onToggleFavorite?: () => void;
  /** Сколько штук этой позиции уже в корзине */
  inCartCount?: number;
}

const priceOf = (item: Dish) => {
  const children = item.children ?? [];
  if (children.length === 0) return { value: item.price, from: false };
  const min = Math.min(...children.map(c => c.price));
  const max = Math.max(...children.map(c => c.price));
  return { value: min, from: min !== max };
};

/**
 * Карточка позиции меню: фото, цена, название, кнопка «Добавить».
 * Клик по карточке или кнопке открывает выбор варианта и добавок.
 */
const MenuItemCard: React.FC<MenuItemCardProps> = ({ item, onOpen, isFavorite = false, onToggleFavorite, inCartCount = 0 }) => {
  const theme = useTheme();
  const muted = theme.palette.mode === 'light' ? tokens.light.surfaceMuted : tokens.dark.surfaceMuted;
  const image = item.primaryImage || item.images?.find(i => i.isPrimary)?.filePath || item.images?.[0]?.filePath;
  const imageUrl = image ? resolveMediaUrl(image) : '';
  const price = priceOf(item);
  const description = item.description || item.children?.[0]?.description || '';
  const spicy = item.isSpicy || item.children?.some(c => c.isSpicy);
  const orderTypes = allowedOrderTypes(item);
  const venueNote = orderTypes ? describeOrderTypes(orderTypes) : '';
  const badge = item.badge ? BADGES[item.badge] : undefined;
  // Единственный вариант с весом/размером (например «400 г») показываем под названием
  const sizeNote = item.children?.length === 1 && item.children[0].name !== 'Стандарт' ? item.children[0].name : '';

  return (
    <Box
      role="button"
      tabIndex={0}
      onClick={onOpen}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onOpen(); } }}
      sx={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        p: { xs: 1, sm: 1.25 },
        borderRadius: '24px',
        bgcolor: 'background.paper',
        cursor: 'pointer',
        outline: 'none',
        transition: 'box-shadow 0.2s ease',
        '&:hover': { boxShadow: '0 8px 28px rgba(33,32,31,0.10)' },
        '&:focus-visible': { boxShadow: `0 0 0 3px ${theme.palette.text.primary}` },
      }}
    >
      {/* Фото */}
      <Box
        sx={{
          position: 'relative',
          aspectRatio: '1 / 1',
          borderRadius: '18px',
          overflow: 'hidden',
          bgcolor: muted,
        }}
      >
        {imageUrl ? (
          <Box
            component="img"
            src={imageUrl}
            alt={item.name}
            loading="lazy"
            sx={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }}
          />
        ) : (
          <Box sx={{ position: 'absolute', inset: 0, display: 'grid', placeItems: 'center' }}>
            <Typography aria-hidden sx={{ fontSize: { xs: 56, sm: 72 }, lineHeight: 1, opacity: 0.85 }}>🍽️</Typography>
          </Box>
        )}

        {(badge || spicy || venueNote) && (
          <Box sx={{ position: 'absolute', left: 8, top: 8, display: 'flex', gap: 0.5, flexWrap: 'wrap', pr: 5 }}>
            {badge && <Tag label={badge.label} color={badge.color} solid />}
            {spicy && <Tag label="Острое" color={tokens.chili} />}
            {venueNote && <Tag label={venueNote[0].toUpperCase() + venueNote.slice(1)} color="#21201F" />}
          </Box>
        )}

        {onToggleFavorite && (
          <IconButton
            size="small"
            aria-label={isFavorite ? 'Убрать из избранного' : 'В избранное'}
            onClick={(e) => { e.stopPropagation(); onToggleFavorite(); }}
            sx={{
              position: 'absolute',
              top: 8,
              right: 8,
              bgcolor: 'rgba(255,255,255,0.92)',
              color: '#21201F',
              '&:hover': { bgcolor: '#fff' },
            }}
          >
            {isFavorite ? <FavoriteIcon fontSize="small" sx={{ color: tokens.chili }} /> : <FavoriteBorderIcon fontSize="small" />}
          </IconButton>
        )}
      </Box>

      {/* Текст */}
      <Box sx={{ px: 0.75, pt: 1.25, flex: 1, display: 'flex', flexDirection: 'column' }}>
        <Typography sx={{ fontWeight: 800, fontSize: { xs: '1.05rem', sm: '1.2rem' }, letterSpacing: '-0.02em' }}>
          {price.from && <Box component="span" sx={{ fontWeight: 600, color: 'text.secondary', fontSize: '0.85em', mr: 0.5 }}>от</Box>}
          {price.value} ₽
        </Typography>
        <Typography
          sx={{
            mt: 0.25,
            fontWeight: 500,
            fontSize: { xs: '0.9rem', sm: '0.95rem' },
            lineHeight: 1.3,
            display: '-webkit-box',
            WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical',
            overflow: 'hidden',
          }}
        >
          {item.name}
        </Typography>
        {sizeNote && (
          <Typography variant="body2" color="text.secondary" sx={{ fontSize: '0.8rem', mt: 0.25 }}>{sizeNote}</Typography>
        )}
        {description && (
          <Typography
            variant="body2"
            color="text.secondary"
            sx={{
              mt: 0.25,
              fontSize: '0.8rem',
              display: { xs: 'none', sm: '-webkit-box' },
              WebkitLineClamp: 1,
              WebkitBoxOrient: 'vertical',
              overflow: 'hidden',
            }}
          >
            {description}
          </Typography>
        )}
        <Box sx={{ flex: 1 }} />
        <Button
          fullWidth
          startIcon={inCartCount > 0 ? undefined : <AddIcon />}
          onClick={(e) => { e.stopPropagation(); onOpen(); }}
          sx={{
            mt: 1.25,
            bgcolor: inCartCount > 0 ? tokens.saffron : muted,
            color: inCartCount > 0 ? '#21201F' : 'text.primary',
            '&:hover': { bgcolor: inCartCount > 0 ? tokens.saffronHover : 'action.selected' },
          }}
        >
          {inCartCount > 0 ? `В корзине: ${inCartCount}` : 'Добавить'}
        </Button>
      </Box>
    </Box>
  );
};

export const BADGES: Record<string, { label: string; color: string }> = {
  hit: { label: 'Хит', color: tokens.chili },
  new: { label: 'Новинка', color: tokens.herb },
  promo: { label: 'Акция', color: '#7A4DFF' },
  lean: { label: 'Постное', color: '#4E7D2E' },
};

const Tag: React.FC<{ label: string; color: string; solid?: boolean }> = ({ label, color, solid }) => (
  <Box
    component="span"
    sx={{
      px: 1,
      py: 0.25,
      borderRadius: '8px',
      bgcolor: solid ? color : 'rgba(255,255,255,0.94)',
      color: solid ? '#fff' : color,
      fontSize: '0.72rem',
      fontWeight: 700,
    }}
  >
    {label}
  </Box>
);

export default MenuItemCard;
