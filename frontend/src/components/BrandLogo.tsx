import * as React from 'react';
import { Box, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { tokens } from '../theme';

/** Логотип: шафрановый «лаваш» с буквой К и надпись */
const BrandLogo: React.FC<{ compact?: boolean; onClick?: () => void }> = ({ compact, onClick }) => (
  <Box
    component={Link}
    to="/"
    onClick={onClick}
    aria-label="Кума Шаурма — на главную"
    sx={{ display: 'inline-flex', alignItems: 'center', gap: 1.25, textDecoration: 'none', color: 'text.primary' }}
  >
    <Box
      aria-hidden
      sx={{
        width: 38,
        height: 38,
        borderRadius: '12px 12px 12px 4px',
        bgcolor: tokens.saffron,
        color: '#21201F',
        display: 'grid',
        placeItems: 'center',
        fontWeight: 900,
        fontSize: '1.3rem',
        letterSpacing: '-0.04em',
        transform: 'rotate(-6deg)',
        flexShrink: 0,
      }}
    >
      К
    </Box>
    {!compact && (
      <Typography component="span" sx={{ fontWeight: 800, fontSize: '1.2rem', letterSpacing: '-0.03em', lineHeight: 1 }}>
        Кума
        <Box component="span" sx={{ color: 'text.secondary', fontWeight: 600, ml: 0.5 }}>шаурма</Box>
      </Typography>
    )}
  </Box>
);

export default BrandLogo;
