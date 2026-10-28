import * as React from 'react';
import { Box, Typography, Button, Alert } from '@mui/material';
import { CheckCircleOutline as OkIcon, ErrorOutline as ErrIcon, SettingsOutlined as SettingsIcon } from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import { useCan, P } from '../../auth/permissions';
import { tokens } from '../../theme';

/**
 * Статус подключения интеграции во вкладке склада (ЕГАИС, Честный знак, СБИС).
 * Сами настройки — на странице «Настройка интеграций»; сюда только ссылка для тех, у кого есть право.
 */
const IntegrationStatus: React.FC<{
  ok: boolean;
  /** Подключено, но с проблемой (нет связи) — красным, а не серым */
  failed?: boolean;
  title: string;
  details?: string | null;
  error?: string | null;
  warning?: string | null;
  /** Якорь раздела на странице настроек: egais, chz, sbis */
  section?: string;
  children?: React.ReactNode;
}> = ({ ok, failed, title, details, error, warning, section, children }) => {
  const can = useCan();
  const navigate = useNavigate();
  const canSettings = can(P.Settings);
  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
        <Box sx={{ flex: 1, minWidth: 240 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            {ok ? <OkIcon sx={{ color: tokens.herb }} /> : <ErrIcon sx={{ color: failed ? tokens.chili : 'text.secondary' }} />}
            <Typography sx={{ fontWeight: 700 }}>{title}</Typography>
          </Box>
          {details && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{details}</Typography>}
          {error && <Typography variant="body2" sx={{ color: tokens.chili, mt: 0.5 }}>{error}</Typography>}
          {!ok && !canSettings && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>Попросите администратора настроить подключение.</Typography>}
        </Box>
        {canSettings && (
          <Button
            startIcon={<SettingsIcon />}
            variant={ok ? 'text' : 'contained'}
            color={ok ? 'inherit' : 'secondary'}
            onClick={() => navigate(`/admin/integrations${section ? `#${section}` : ''}`)}
            sx={ok ? { color: 'text.secondary' } : undefined}
          >
            {ok ? 'Настройка интеграций' : 'Подключить'}
          </Button>
        )}
        {children}
      </Box>
      {warning && <Alert severity="warning" sx={{ mt: 1.5 }}>{warning}</Alert>}
    </Box>
  );
};

export default IntegrationStatus;
