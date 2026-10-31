import * as React from 'react';
import { Box, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { tokens } from '../theme';
import { useBrand } from '../api/hooks';

/** Логотип: шафрановый «лаваш» с первой буквой названия и само название (из настроек) */
const BrandLogo: React.FC<{ compact?: boolean; onClick?: () => void }> = ({ compact, onClick }) => {
  const { name } = useBrand();
  const [first, ...rest] = name.split(' ');
  return (
  <Box
    component={Link}
    to="/"
    onClick={onClick}
    aria-label={`${name} — на главную`}
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
      {name.trim().charAt(0).toUpperCase()}
    </Box>
    {!compact && (
      <Typography component="span" sx={{ fontWeight: 800, fontSize: '1.2rem', letterSpacing: '-0.03em', lineHeight: 1 }}>
        {first}
        {rest.length > 0 && <Box component="span" sx={{ color: 'text.secondary', fontWeight: 600, ml: 0.5 }}>{rest.join(' ').toLowerCase()}</Box>}
      </Typography>
    )}
  </Box>
  );
};

/** Название заведения текстом (подвал и т.п.) */
export const BrandName: React.FC = () => <>{useBrand().name}</>;

/** Заголовок вкладки браузера = название заведения */
export const BrandTitle: React.FC = () => {
  const { name, loaded } = useBrand();
  React.useEffect(() => { if (loaded) document.title = name; }, [name, loaded]);
  return null;
};

export default BrandLogo;
