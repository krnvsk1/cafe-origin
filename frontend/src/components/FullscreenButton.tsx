import * as React from 'react';
import { IconButton, Tooltip } from '@mui/material';
import { Fullscreen as FullscreenIcon, FullscreenExit as FullscreenExitIcon } from '@mui/icons-material';
import { useIsFullscreen, toggleFullscreen } from '../hooks/useFullscreen';

/** Кнопка «На весь экран» для рабочих экранов (официант, кухня, касса): прячет шапку и подвал, где можно — и панели браузера */
const FullscreenButton: React.FC = () => {
  const isFullscreen = useIsFullscreen();
  return (
    <Tooltip title={isFullscreen ? 'Выйти из полноэкранного режима' : 'На весь экран'}>
      <IconButton onClick={toggleFullscreen} aria-label={isFullscreen ? 'Выйти из полноэкранного режима' : 'На весь экран'}>
        {isFullscreen ? <FullscreenExitIcon /> : <FullscreenIcon />}
      </IconButton>
    </Tooltip>
  );
};

export default FullscreenButton;
