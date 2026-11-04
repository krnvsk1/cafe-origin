import * as React from 'react';
import {
  Button, IconButton, Tooltip, Dialog, DialogTitle, DialogContent, DialogActions, Typography, Alert,
  FormControlLabel, Checkbox, LinearProgress, Box, TextField,
} from '@mui/material';
import { CloudDownloadOutlined as ImportIcon } from '@mui/icons-material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '../api/client';
import { useCan, P } from '../auth/permissions';

interface ImportResult {
  categories: number;
  cardsCreated: number;
  cardsUpdated: number;
  variants: number;
  addons: number;
  images: number;
  warnings: string[];
}

const URL_KEY = 'menu-import-url';

/** Кнопка «Импорт меню с сайта» для администратора: адрес страницы меню вводится один раз и запоминается */
/** compact — только значок (узкий экран) */
const MenuImportButton: React.FC<{ compact?: boolean }> = ({ compact = false }) => {
  const isAdmin = useCan()(P.MenuImport);
  const queryClient = useQueryClient();
  const [open, setOpen] = React.useState(false);
  const [downloadImages, setDownloadImages] = React.useState(true);
  const [sourceUrl, setSourceUrl] = React.useState(() => { try { return localStorage.getItem(URL_KEY) ?? ''; } catch { return ''; } });

  const importMenu = useMutation<ImportResult, any>({
    mutationFn: () => apiClient
      .post('/api/admin/menu-import', { sourceUrl: sourceUrl.trim(), downloadImages }, { timeout: 5 * 60 * 1000 })
      .then(res => res.data),
    onSuccess: () => {
      try { localStorage.setItem(URL_KEY, sourceUrl.trim()); } catch { /* ignore */ }
      queryClient.invalidateQueries({ queryKey: ['dishes'] });
      queryClient.invalidateQueries({ queryKey: ['addon-categories'] });
      queryClient.invalidateQueries({ queryKey: ['stoplist'] });
    },
  });

  if (!isAdmin) return null;
  const result = importMenu.data;

  return (
    <>
      {compact ? (
        <Tooltip title="Импорт с сайта">
          <IconButton onClick={() => { importMenu.reset(); setOpen(true); }} aria-label="Импорт с сайта" sx={{ border: 1, borderColor: 'divider' }}>
            <ImportIcon />
          </IconButton>
        </Tooltip>
      ) : (
        <Button variant="outlined" startIcon={<ImportIcon />} onClick={() => { importMenu.reset(); setOpen(true); }}>
          Импорт с сайта
        </Button>
      )}
      <Dialog open={open} onClose={() => !importMenu.isPending && setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>Импорт меню с сайта</DialogTitle>
        <DialogContent>
          {!result && (
            <>
              <Typography sx={{ mb: 2 }}>
                Загрузим разделы, позиции, размеры с ценами, добавки и фото с сайта. Уже существующие позиции обновятся
                (цены и состав), новые добавятся. Ничего не удаляется, а свои фото у позиций не заменяются.
              </Typography>
              <TextField fullWidth size="small" label="Адрес страницы меню" placeholder="https://…" value={sourceUrl}
                onChange={e => setSourceUrl(e.target.value)} sx={{ mb: 1 }}
                helperText="Сайты на платформе FoodPicasso" />
              <FormControlLabel
                control={<Checkbox checked={downloadImages} onChange={(e) => setDownloadImages(e.target.checked)} />}
                label="Скачать фото к себе (надёжнее, чем ссылки на сайт)"
              />
            </>
          )}
          {importMenu.isPending && (
            <Box sx={{ mt: 2 }}>
              <LinearProgress />
              <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                Загружаем меню и фото — это может занять минуту
              </Typography>
            </Box>
          )}
          {importMenu.isError && (
            <Alert severity="error" sx={{ mt: 2 }}>
              {importMenu.error?.response?.data?.message || 'Не удалось импортировать меню'}
            </Alert>
          )}
          {result && (
            <Alert severity="success">
              Готово: {result.categories} разделов, новых позиций {result.cardsCreated}, обновлено {result.cardsUpdated},
              вариантов {result.variants}, новых добавок {result.addons}, фото {result.images}.
            </Alert>
          )}
          {result && result.warnings.length > 0 && (
            <Alert severity="warning" sx={{ mt: 1 }}>
              {result.warnings.slice(0, 5).map(w => <div key={w}>{w}</div>)}
              {result.warnings.length > 5 && <div>и ещё {result.warnings.length - 5}</div>}
            </Alert>
          )}
        </DialogContent>
        <DialogActions sx={{ p: 2.5 }}>
          <Button onClick={() => setOpen(false)} disabled={importMenu.isPending}>
            {result ? 'Закрыть' : 'Отмена'}
          </Button>
          {!result && (
            <Button variant="contained" onClick={() => importMenu.mutate()} disabled={importMenu.isPending || !/^https:\/\/\S+/.test(sourceUrl.trim())}>
              Импортировать
            </Button>
          )}
        </DialogActions>
      </Dialog>
    </>
  );
};

export default MenuImportButton;
