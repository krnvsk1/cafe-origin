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

/** Значок с первой буквой названия — как у логотипа на сайте */
const iconSvg = (letter: string) => {
  const safe = letter.replace(/[<>&"']/g, '');
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><g transform="rotate(-6 32 32)">`
    + `<rect x="6" y="6" width="52" height="52" rx="16" ry="16" fill="${tokens.saffron}"/><rect x="6" y="42" width="16" height="16" rx="5" fill="${tokens.saffron}"/></g>`
    + `<text x="32" y="44" font-family="Arial Black, Arial, sans-serif" font-weight="900" font-size="34" text-anchor="middle" fill="#21201F">${safe}</text></svg>`;
};

const setLink = (rel: string, href: string, type?: string) => {
  let link = document.querySelector<HTMLLinkElement>(`link[rel="${rel}"]`);
  if (!link) { link = document.createElement('link'); link.rel = rel; document.head.appendChild(link); }
  if (type) link.type = type;
  link.href = href;
};

/** Значок для экрана «Домой» на iPhone — PNG 180×180 на белом фоне */
const touchIcon = (svg: string) => new Promise<string | null>(resolve => {
  const img = new Image();
  img.onload = () => {
    try {
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = 180;
      const ctx = canvas.getContext('2d');
      if (!ctx) return resolve(null);
      ctx.fillStyle = '#FFFFFF';
      ctx.fillRect(0, 0, 180, 180);
      ctx.drawImage(img, 0, 0, 180, 180);
      resolve(canvas.toDataURL('image/png'));
    } catch { resolve(null); }
  };
  img.onerror = () => resolve(null);
  img.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
});

/** Заголовок и значок вкладки браузера = название заведения и его первая буква */
export const BrandTitle: React.FC = () => {
  const { name, loaded } = useBrand();
  React.useEffect(() => {
    if (!loaded) return;
    document.title = name;
    const letter = name.trim().charAt(0).toUpperCase();
    if (!letter) return;
    const svg = iconSvg(letter);
    setLink('icon', `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`, 'image/svg+xml');
    let alive = true;
    void touchIcon(svg).then(png => { if (alive && png) setLink('apple-touch-icon', png); });
    return () => { alive = false; };
  }, [name, loaded]);
  return null;
};

export default BrandLogo;
