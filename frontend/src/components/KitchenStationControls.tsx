import * as React from 'react';
import {
  TextField, MenuItem, Button, Dialog, DialogTitle, DialogContent, DialogActions, Alert, Box, Typography,
} from '@mui/material';
import { SoupKitchen as KitchenIcon } from '@mui/icons-material';
import { useKitchenStations, useRenameStation } from '../api/staff';
import { useUpdateShawarma } from '../api/hooks';
import { useAuthStore } from '../store/authStore';
import { apiError } from '../api/inventory';
import type { Shawarma } from '../types';

/**
 * Метка кухни у блюда: на какую кухню (горячая, мангал…) оно уходит.
 * У варианта есть пункт «как у карточки» — тогда действует метка карточки.
 */
export const KitchenStationSelect: React.FC<{ item: Shawarma; parent?: Shawarma | null; onError?: (text: string) => void }> = ({ item, parent, onError }) => {
  const { data: stations = [] } = useKitchenStations();
  const update = useUpdateShawarma();
  if (stations.length === 0) return null;

  const inherited = parent?.kitchenStationId ?? stations[0].id;
  const inheritedName = stations.find(s => s.id === inherited)?.name ?? stations[0].name;

  const change = (value: number) => {
    update.mutate(
      { id: item.id, kitchenStationId: value } as Partial<Shawarma> & { id: number },
      { onError: e => onError?.(apiError(e)) },
    );
  };

  return (
    <TextField
      select
      size="small"
      value={item.kitchenStationId ?? 0}
      onChange={e => change(Number(e.target.value))}
      disabled={update.isPending}
      onClick={e => e.stopPropagation()}
      aria-label="Кухня"
      sx={{ minWidth: 150, '& .MuiSelect-select': { py: 0.75 } }}
      slotProps={{ input: { startAdornment: <KitchenIcon fontSize="small" sx={{ mr: 0.75, color: 'text.secondary' }} /> } }}
    >
      <MenuItem value={0}><em>{parent ? `Как у карточки (${inheritedName})` : `По умолчанию (${stations[0].name})`}</em></MenuItem>
      {stations.map(s => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}
    </TextField>
  );
};

/** Кнопка «Кухни» и окно переименования — только для администратора */
export const KitchenStationsButton: React.FC = () => {
  const isAdmin = useAuthStore(s => s.user?.roles?.includes('admin') ?? false);
  const { data: stations = [] } = useKitchenStations();
  const rename = useRenameStation();
  const [open, setOpen] = React.useState(false);
  const [names, setNames] = React.useState<Record<number, string>>({});
  const [error, setError] = React.useState('');

  if (!isAdmin) return null;

  const start = () => {
    setNames(Object.fromEntries(stations.map(s => [s.id, s.name])));
    setError('');
    setOpen(true);
  };

  const save = async () => {
    setError('');
    try {
      for (const s of stations) {
        const n = (names[s.id] ?? '').trim();
        if (n && n !== s.name) await rename.mutateAsync({ id: s.id, name: n });
      }
      setOpen(false);
    } catch (e) { setError(apiError(e)); }
  };

  return (
    <>
      <Button variant="outlined" startIcon={<KitchenIcon />} onClick={start}>Кухни</Button>
      <Dialog open={open} onClose={() => setOpen(false)} maxWidth="xs" fullWidth>
        <DialogTitle>Кухни</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
          <Typography variant="body2" color="text.secondary">
            Блюдо уходит на ту кухню, что выбрана у него в меню. Блюда без метки идут на первую кухню.
          </Typography>
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
            {stations.map((s, i) => (
              <TextField key={s.id} label={`Кухня ${i + 1}`} value={names[s.id] ?? ''} onChange={e => setNames(n => ({ ...n, [s.id]: e.target.value }))}
                slotProps={{ htmlInput: { maxLength: 60 } }} />
            ))}
          </Box>
          {error && <Alert severity="error">{error}</Alert>}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3 }}>
          <Button onClick={() => setOpen(false)} sx={{ color: 'text.secondary' }}>Отмена</Button>
          <Button variant="contained" onClick={save} disabled={rename.isPending}>Сохранить</Button>
        </DialogActions>
      </Dialog>
    </>
  );
};
