import * as React from 'react';
import { Box, Button, Typography } from '@mui/material';
import { FactCheckOutlined as HaccpIcon } from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import { useHaccpToday, isOverdue } from '../api/haccp';
import { useCan, P } from '../auth/permissions';
import { useIsFullscreen } from '../hooks/useFullscreen';
import { tokens } from '../theme';

/**
 * Висящее оповещение «журналы ХАССП не заполнены» — на любой странице (и на кассе/кухне во весь экран)
 * у всех, у кого есть доступ к журналам. Закрыть нельзя: пропадает само, когда сегодняшние проверки сделаны.
 */
const HaccpReminder: React.FC = () => {
  const can = useCan();
  const allowed = can(P.Haccp, P.HaccpManage);
  const { data } = useHaccpToday(allowed);
  const navigate = useNavigate();
  const fullscreen = useIsFullscreen();

  if (!allowed || !data) return null;
  // Только то, чьё время по графику уже наступило
  const pending = data.tasks.filter(isOverdue);
  if (pending.length === 0) return null;

  const names = pending.map(t => {
    const name = t.objectName ?? t.title;
    return t.due > 1 ? `${name} (${Math.min(t.done, t.due)} из ${t.due})` : name;
  });

  return (
    <Box
      role="alert"
      sx={{
        position: 'sticky',
        top: fullscreen ? 0 : { xs: 60, md: 72 },
        zIndex: (t) => t.zIndex.appBar - 1,
        bgcolor: tokens.chili,
        color: '#fff',
        px: { xs: 2, sm: 3 },
        py: 1,
        display: 'flex',
        alignItems: 'center',
        gap: 1.5,
        boxShadow: '0 2px 8px rgba(0,0,0,0.15)',
      }}
    >
      <HaccpIcon sx={{ flexShrink: 0 }} />
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Typography sx={{ fontWeight: 700, lineHeight: 1.3 }}>
          Журналы ХАССП: пора заполнить {pending.length}
        </Typography>
        <Typography variant="body2" sx={{ opacity: 0.92, lineHeight: 1.3 }} noWrap title={names.join(', ')}>
          {names.join(' · ')}
        </Typography>
      </Box>
      <Button
        variant="contained"
        onClick={() => navigate('/haccp')}
        sx={{ bgcolor: '#fff', color: tokens.chili, flexShrink: 0, fontWeight: 700, '&:hover': { bgcolor: 'rgba(255,255,255,0.88)' } }}
      >
        Заполнить
      </Button>
    </Box>
  );
};

export default HaccpReminder;
