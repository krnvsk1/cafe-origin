import * as React from 'react';
import { Box, Typography, Chip, useTheme, type ChipProps } from '@mui/material';
import { tokens } from '../../theme';
import type { StockDocumentStatus } from '../../api/inventory';
import { STATUS_LABELS } from '../../api/inventory';

/** Белый блок раздела склада */
export const Panel: React.FC<{ children: React.ReactNode; sx?: object; padding?: boolean }> = ({ children, sx, padding = true }) => (
  <Box sx={{ bgcolor: 'background.paper', borderRadius: '20px', p: padding ? { xs: 2, sm: 2.5 } : 0, ...sx }}>
    {children}
  </Box>
);

/** Плитка со сводной цифрой */
export const StatTile: React.FC<{
  label: string;
  value: React.ReactNode;
  hint?: React.ReactNode;
  tone?: 'default' | 'warning' | 'danger' | 'good';
  onClick?: () => void;
}> = ({ label, value, hint, tone = 'default', onClick }) => {
  const color = tone === 'danger' ? tokens.chili : tone === 'warning' ? '#C77700' : tone === 'good' ? tokens.herb : 'text.primary';
  return (
    <Box
      component={onClick ? 'button' : 'div'}
      type={onClick ? 'button' : undefined}
      onClick={onClick}
      sx={{
        textAlign: 'left',
        border: 0,
        font: 'inherit',
        color: 'text.primary',
        bgcolor: 'background.paper',
        borderRadius: '20px',
        p: { xs: 1.75, sm: 2.25 },
        cursor: onClick ? 'pointer' : 'default',
        transition: 'box-shadow 0.15s ease',
        '&:hover': onClick ? { boxShadow: '0 6px 20px rgba(33,32,31,0.08)' } : undefined,
      }}
    >
      <Typography variant="body2" color="text.secondary" sx={{ mb: 0.5 }}>{label}</Typography>
      <Typography sx={{ fontWeight: 800, fontSize: { xs: '1.3rem', sm: '1.6rem' }, letterSpacing: '-0.02em', color, lineHeight: 1.2 }}>
        {value}
      </Typography>
      {hint && <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>{hint}</Typography>}
    </Box>
  );
};

/** Цветной статус документа */
export const StatusChip: React.FC<{ status: StockDocumentStatus; size?: ChipProps['size'] }> = ({ status, size = 'small' }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const styles: Record<StockDocumentStatus, { bg: string; color: string }> = {
    draft: { bg: t.surfaceMuted, color: theme.palette.text.secondary },
    posted: { bg: 'rgba(46,158,91,0.14)', color: tokens.herb },
    cancelled: { bg: 'rgba(226,70,44,0.12)', color: tokens.chili },
  };
  return (
    <Chip
      size={size}
      label={STATUS_LABELS[status]}
      sx={{ bgcolor: styles[status].bg, color: styles[status].color, fontWeight: 600 }}
    />
  );
};

/** Маленькая метка внутри строки */
export const Pill: React.FC<{ children: React.ReactNode; color?: string; bg?: string }> = ({ children, color, bg }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  return (
    <Box
      component="span"
      sx={{
        display: 'inline-flex',
        alignItems: 'center',
        px: 0.875,
        py: 0.125,
        borderRadius: '8px',
        fontSize: '0.75rem',
        fontWeight: 600,
        whiteSpace: 'nowrap',
        bgcolor: bg ?? t.surfaceMuted,
        color: color ?? 'text.secondary',
      }}
    >
      {children}
    </Box>
  );
};

/** Пустое состояние списка */
export const EmptyState: React.FC<{ icon?: React.ReactNode; title: string; text?: string; action?: React.ReactNode }> = ({ icon, title, text, action }) => (
  <Box sx={{ textAlign: 'center', py: { xs: 5, sm: 7 }, px: 2 }}>
    {icon && <Box sx={{ color: 'text.secondary', mb: 1.5, '& svg': { fontSize: 44 } }}>{icon}</Box>}
    <Typography sx={{ fontWeight: 700, fontSize: '1.1rem' }}>{title}</Typography>
    {text && <Typography color="text.secondary" sx={{ mt: 0.5, maxWidth: 420, mx: 'auto' }}>{text}</Typography>}
    {action && <Box sx={{ mt: 2.5 }}>{action}</Box>}
  </Box>
);

/** Строка списка: кликабельная, с разделителем */
export const ListRow: React.FC<{ onClick?: () => void; children: React.ReactNode; muted?: boolean }> = ({ onClick, children, muted }) => (
  <Box
    component={onClick ? 'button' : 'div'}
    type={onClick ? 'button' : undefined}
    onClick={onClick}
    sx={{
      display: 'flex',
      alignItems: 'center',
      gap: 1.5,
      width: '100%',
      px: { xs: 2, sm: 2.5 },
      py: 1.5,
      border: 0,
      borderTop: '1px solid',
      borderColor: 'divider',
      bgcolor: 'transparent',
      color: 'text.primary',
      font: 'inherit',
      textAlign: 'left',
      cursor: onClick ? 'pointer' : 'default',
      opacity: muted ? 0.55 : 1,
      '&:first-of-type': { borderTop: 0 },
      '&:hover': onClick ? { bgcolor: 'action.hover' } : undefined,
    }}
  >
    {children}
  </Box>
);

/** Заголовок колонок над ListRow (только на компьютере) */
export const ListHeader: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <Box
    sx={{
      display: { xs: 'none', md: 'flex' },
      alignItems: 'center',
      gap: 1.5,
      px: 2.5,
      py: 1.25,
      color: 'text.secondary',
      fontSize: '0.8rem',
      fontWeight: 600,
      borderBottom: '1px solid',
      borderColor: 'divider',
    }}
  >
    {children}
  </Box>
);

/** Процент фудкоста с цветом: до 30 % — хорошо, до 40 % — норма, выше — дорого */
export const FoodCost: React.FC<{ value: number | null }> = ({ value }) => {
  if (value === null || value === undefined) return <Typography component="span" color="text.secondary">—</Typography>;
  const color = value <= 30 ? tokens.herb : value <= 40 ? '#C77700' : tokens.chili;
  return <Box component="span" sx={{ color, fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>{value.toLocaleString('ru-RU')} %</Box>;
};
