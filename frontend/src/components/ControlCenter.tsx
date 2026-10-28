import * as React from 'react';
import {
  Dialog, DialogContent, Box, Typography, IconButton, Tooltip, useMediaQuery, useTheme, alpha,
} from '@mui/material';
import { Close as CloseIcon, OpenInNew as OpenInNewIcon } from '@mui/icons-material';
import { useLocation, useNavigate } from 'react-router-dom';
import { tokens } from '../theme';

export interface ControlLink {
  text: string;
  icon: React.ReactNode;
  path: string;
  group: string;
  hint: string;
}

// Порядок групп в окне; разделы без группы из списка попадут в конец
const GROUP_ORDER = ['Смена', 'Меню', 'Продажи и маркетинг', 'Склад', 'Госсистемы', 'Финансы', 'Команда'];

const Tile: React.FC<{ link: ControlLink; active: boolean; onOpen: () => void }> = ({ link, active, onOpen }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  return (
    <Box sx={{ position: 'relative' }}>
      <Box
        component="button"
        type="button"
        onClick={onOpen}
        aria-current={active ? 'page' : undefined}
        sx={{
          width: '100%',
          height: '100%',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'flex-start',
          gap: 1,
          p: 2,
          pr: 5,
          border: '1px solid',
          borderColor: active ? 'text.primary' : 'divider',
          borderRadius: '18px',
          bgcolor: 'background.paper',
          color: 'text.primary',
          font: 'inherit',
          textAlign: 'left',
          cursor: 'pointer',
          transition: 'box-shadow 0.15s ease, transform 0.15s ease',
          '&:hover': { boxShadow: '0 8px 24px rgba(33,32,31,0.10)', transform: 'translateY(-1px)' },
          '&:focus-visible': { outline: `2px solid ${theme.palette.text.primary}`, outlineOffset: 2 },
        }}
      >
        <Box
          sx={{
            width: 44, height: 44, borderRadius: '14px', display: 'grid', placeItems: 'center',
            bgcolor: active ? alpha(tokens.saffron, 0.45) : t.surfaceMuted,
            '& svg': { fontSize: 24 },
          }}
        >
          {link.icon}
        </Box>
        <Box>
          <Typography sx={{ fontWeight: 700, lineHeight: 1.25 }}>{link.text}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25, lineHeight: 1.35 }}>{link.hint}</Typography>
        </Box>
      </Box>
      <Tooltip title="Открыть в новом окне">
        <IconButton
          size="small"
          aria-label={`${link.text} — открыть в новом окне`}
          onClick={() => window.open(link.path, '_blank', 'noopener')}
          sx={{ position: 'absolute', top: 8, right: 8, color: 'text.secondary' }}
        >
          <OpenInNewIcon fontSize="small" />
        </IconButton>
      </Tooltip>
    </Box>
  );
};

/** Окно «Управление»: разделы для сотрудников плитками, сгруппированными по смыслу (вместо выпадающего списка) */
const ControlCenter: React.FC<{ open: boolean; onClose: () => void; links: ControlLink[] }> = ({ open, onClose, links }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const navigate = useNavigate();
  const { pathname } = useLocation();

  const groups = React.useMemo(() => {
    const names = [...new Set(links.map(l => l.group))]
      .sort((a, b) => (GROUP_ORDER.indexOf(a) + 1 || 99) - (GROUP_ORDER.indexOf(b) + 1 || 99));
    return names.map(name => ({ name, links: links.filter(l => l.group === name) }));
  }, [links]);

  const openLink = (path: string) => {
    onClose();
    navigate(path);
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md" fullScreen={fullScreen} aria-labelledby="control-center-title">
      <Box sx={{ display: 'flex', alignItems: 'center', px: { xs: 2, sm: 3 }, pt: { xs: 2, sm: 2.5 }, pb: 1 }}>
        <Typography id="control-center-title" sx={{ fontWeight: 800, fontSize: '1.5rem', flex: 1 }}>Управление</Typography>
        <IconButton onClick={onClose} aria-label="Закрыть"><CloseIcon /></IconButton>
      </Box>
      <DialogContent sx={{ px: { xs: 2, sm: 3 }, pb: 3 }}>
        {groups.map(group => (
          <Box key={group.name} sx={{ mb: 2.5 }}>
            <Typography variant="overline" color="text.secondary" sx={{ display: 'block', mb: 1, lineHeight: 1.5 }}>{group.name}</Typography>
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', sm: 'repeat(3, minmax(0, 1fr))' }, gap: 1.5 }}>
              {group.links.map(link => (
                <Tile
                  key={link.path}
                  link={link}
                  active={pathname === link.path || (link.path !== '/' && pathname.startsWith(`${link.path}/`))}
                  onOpen={() => openLink(link.path)}
                />
              ))}
            </Box>
          </Box>
        ))}
      </DialogContent>
    </Dialog>
  );
};

export default ControlCenter;
