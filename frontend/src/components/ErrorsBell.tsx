import * as React from 'react';
import { IconButton, Badge, Tooltip, Snackbar, Alert, Button } from '@mui/material';
import { ReportProblemOutlined as ErrorIcon } from '@mui/icons-material';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { apiClient } from '../api/client';
import { useCan, P } from '../auth/permissions';

interface ErrorsInfo {
  enabled: boolean;
  count: number;
  latest: { at: string; message: string; source: string | null; requestId: string | null }[];
  now: string;
}

const KEY = 'errorsSeenAt';
const readSeen = () => { try { return localStorage.getItem(KEY); } catch { return null; } };
const writeSeen = (v: string) => { try { localStorage.setItem(KEY, v); } catch { /* приватный режим */ } };

/**
 * Оповещение об ошибках сервера (включается в «Состоянии системы»): значок с числом новых ошибок
 * и всплывающее сообщение о свежей ошибке. Видят те, кому доступна «Система».
 */
const ErrorsBell: React.FC = () => {
  const can = useCan();
  const allowed = can(P.Settings);
  const navigate = useNavigate();
  // С какого момента ошибки считаются новыми: впервые — с момента открытия сайта, а не за сутки назад
  const [seen, setSeen] = React.useState<string>(() => readSeen() ?? new Date().toISOString());
  React.useEffect(() => { if (!readSeen()) writeSeen(seen); }, [seen]);

  const { data } = useQuery<ErrorsInfo>({
    queryKey: ['system-errors', seen],
    queryFn: () => apiClient.get('/api/admin/system/errors', { params: { since: seen } }).then(r => r.data),
    enabled: allowed,
    refetchInterval: 60_000,
    refetchIntervalInBackground: true,
    staleTime: 30_000,
  });

  // Всплывающее сообщение — только о новой ошибке с прошлой проверки
  const lastToasted = React.useRef<string | null>(null);
  const [toast, setToast] = React.useState<string | null>(null);
  React.useEffect(() => {
    const top = data?.latest[0];
    if (!data?.enabled || !top) return;
    if (lastToasted.current === null) { lastToasted.current = top.at; setToast(top.message); return; }
    if (top.at > lastToasted.current) { lastToasted.current = top.at; setToast(top.message); }
  }, [data]);

  if (!allowed || !data?.enabled || data.count === 0) return null;

  const open = () => {
    const now = data.now ?? new Date().toISOString();
    writeSeen(now);
    setSeen(now);
    setToast(null);
    navigate('/admin/system?errors=1');
  };

  return (
    <>
      <Tooltip title={`Новых ошибок на сервере: ${data.count}`}>
        <IconButton onClick={open} aria-label={`Ошибки на сервере: ${data.count}`} sx={{ color: 'error.main' }}>
          <Badge badgeContent={data.count} color="error" max={99}>
            <ErrorIcon />
          </Badge>
        </IconButton>
      </Tooltip>
      <Snackbar open={!!toast} autoHideDuration={10000} onClose={() => setToast(null)} anchorOrigin={{ vertical: 'top', horizontal: 'right' }}>
        <Alert severity="error" variant="filled" onClose={() => setToast(null)}
          action={<Button color="inherit" size="small" onClick={open}>Открыть</Button>}
          sx={{ maxWidth: 420, '& .MuiAlert-message': { overflow: 'hidden', textOverflow: 'ellipsis', display: '-webkit-box', WebkitLineClamp: 3, WebkitBoxOrient: 'vertical' } }}>
          Ошибка на сервере: {toast}
        </Alert>
      </Snackbar>
    </>
  );
};

export default ErrorsBell;
