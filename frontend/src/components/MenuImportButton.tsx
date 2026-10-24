import * as React from 'react';
import {
  Button, Dialog, DialogTitle, DialogContent, DialogActions, Typography, Alert,
  FormControlLabel, Checkbox, LinearProgress, Box,
} from '@mui/material';
import { CloudDownloadOutlined as ImportIcon } from '@mui/icons-material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '../api/client';
import { useAuthStore } from '../store/authStore';

interface ImportResult {
  categories: number;
  cardsCreated: number;
  cardsUpdated: number;
  variants: number;
  addons: number;
  images: number;
  warnings: string[];
}

/** Кнопка «Импорт с kumashaurma.ru» для администратора */
const MenuImportButton: React.FC = () => {
  const isAdmin = useAuthStore(s => s.hasRole('admin'));
  const queryClient = useQueryClient();
  const [open, setOpen] = React.useState(false);
  const [downloadImages, setDownloadImages] = React.useState(true);

  const importMenu = useMutation<ImportResult, any>({
    mutationFn: () => apiClient
      .post('/api/admin/menu-import', { downloadImages }, { timeout: 5 * 60 * 1000 })
      .then(res => res.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['shawarmas'] });
      queryClient.invalidateQueries({ queryKey: ['addon-categories'] });
      queryClient.invalidateQueries({ queryKey: ['stoplist'] });
    },
  });

  if (!isAdmin) return null;
  const result = importMenu.data;

  return (
    <>
      <Button variant="outlined" startIcon={<ImportIcon />} onClick={() => { importMenu.reset(); setOpen(true); }}>
        Импорт с kumashaurma.ru
      </Button>
      <Dialog open={open} onClose={() => !importMenu.isPending && setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>Импорт меню с kumashaurma.ru</DialogTitle>
        <DialogContent>
          {!result && (
            <>
              <Typography sx={{ mb: 2 }}>
                Загрузим разделы, позиции, размеры с ценами, добавки и фото с сайта. Уже существующие позиции обновятся
                (цены и состав), новые добавятся. Ничего не удаляется, а свои фото у позиций не заменяются.
              </Typography>
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
            <Button variant="contained" onClick={() => importMenu.mutate()} disabled={importMenu.isPending}>
              Импортировать
            </Button>
          )}
        </DialogActions>
      </Dialog>
    </>
  );
};

export default MenuImportButton;
