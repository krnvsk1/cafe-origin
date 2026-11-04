import * as React from 'react';
import { Box, ClickAwayListener, IconButton, Tooltip, Typography } from '@mui/material';
import { HelpOutline as HelpIcon } from '@mui/icons-material';

/**
 * Подсказка за значком «?»: на компьютере — при наведении, на телефоне и по клику — по нажатию
 * (повторное нажатие или нажатие мимо закрывает).
 */
export const Hint: React.FC<{ title: React.ReactNode; label?: string; size?: 'small' | 'medium' }> = ({ title, label = 'Подсказка', size = 'small' }) => {
  const [pinned, setPinned] = React.useState(false);
  const [hover, setHover] = React.useState(false);
  const content = typeof title === 'string'
    ? <Typography variant="body2" sx={{ whiteSpace: 'pre-line' }}>{title}</Typography>
    : title;
  return (
    <ClickAwayListener onClickAway={() => setPinned(false)}>
      <Tooltip
        title={<Box sx={{ p: 0.5, maxWidth: 360 }}>{content}</Box>}
        open={pinned || hover}
        onOpen={() => setHover(true)}
        onClose={() => setHover(false)}
        arrow
        placement="top"
        disableTouchListener
        slotProps={{ popper: { disablePortal: false } }}
      >
        <IconButton
          size={size}
          aria-label={label}
          onClick={e => { e.stopPropagation(); e.preventDefault(); setPinned(p => !p); }}
          sx={{ color: 'text.secondary', p: 0.25, verticalAlign: 'middle' }}
        >
          <HelpIcon fontSize="small" />
        </IconButton>
      </Tooltip>
    </ClickAwayListener>
  );
};

/** Поле с подсказкой справа (вместо текста под полем) */
export const WithHint: React.FC<{ hint: React.ReactNode; children: React.ReactNode; sx?: object }> = ({ hint, children, sx }) => (
  <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 0.5, ...sx }}>
    <Box sx={{ flex: 1, minWidth: 0, display: 'flex', '& > *': { flex: 1 } }}>{children}</Box>
    <Box sx={{ pt: 1.25 }}><Hint title={hint} /></Box>
  </Box>
);

/** Заголовок с подсказкой рядом */
export const TitleWithHint: React.FC<{ children: React.ReactNode; hint: React.ReactNode }> = ({ children, hint }) => (
  <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5 }}>
    {children}
    <Hint title={hint} />
  </Box>
);

/** Подсказка под полем: только значок «?» (в helperText поля) */
export const FieldHint: React.FC<{ text: React.ReactNode }> = ({ text }) => (
  <Box component="span" sx={{ display: 'inline-flex', ml: -0.75 }}><Hint title={text} /></Box>
);
