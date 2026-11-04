import * as React from 'react';
import {
  TextField, MenuItem, Button, Dialog, DialogTitle, DialogContent, DialogActions, Alert, Box, Typography, IconButton, Tooltip,
} from '@mui/material';
import {
  SoupKitchen as KitchenIcon, ArrowUpward as UpIcon, ArrowDownward as DownIcon, DeleteOutline as DeleteIcon, Add as AddIcon,
} from '@mui/icons-material';
import { useKitchenStations, useRenameStation, useAddStation, useOrderStations, useDeleteStation } from '../api/staff';
import { useUpdateDish } from '../api/hooks';
import { useAuthStore } from '../store/authStore';
import { apiError } from '../api/inventory';
import type { Dish } from '../types';

/**
 * Метка кухни у блюда: на какую кухню (горячая, мангал…) оно уходит.
 * У варианта есть пункт «как у карточки» — тогда действует метка карточки.
 */
export const KitchenStationSelect: React.FC<{ item: Dish; parent?: Dish | null; onError?: (text: string) => void }> = ({ item, parent, onError }) => {
  const { data: stations = [] } = useKitchenStations();
  const update = useUpdateDish();
  if (stations.length === 0) return null;

  const inherited = parent?.kitchenStationId ?? stations[0].id;
  const inheritedName = stations.find(s => s.id === inherited)?.name ?? stations[0].name;

  const change = (value: number) => {
    update.mutate(
      { id: item.id, kitchenStationId: value } as Partial<Dish> & { id: number },
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

/** Кнопка «Кухни»: добавить, переименовать, расставить по порядку, убрать — только для администратора */
/** compact — только значок (узкий экран) */
export const KitchenStationsButton: React.FC<{ compact?: boolean }> = ({ compact = false }) => {
  const isAdmin = useAuthStore(s => s.user?.roles?.includes('admin') ?? false);
  const { data: stations = [] } = useKitchenStations();
  const rename = useRenameStation();
  const add = useAddStation();
  const order = useOrderStations();
  const remove = useDeleteStation();
  const [open, setOpen] = React.useState(false);
  const [list, setList] = React.useState<{ id: number; name: string }[]>([]);
  const [newName, setNewName] = React.useState('');
  const [error, setError] = React.useState('');
  const [info, setInfo] = React.useState('');

  // Новые и убранные кухни подтягиваются в список, введённые названия не теряются
  React.useEffect(() => {
    if (!open) return;
    setList(prev => {
      const typed = new Map(prev.map(x => [x.id, x.name]));
      const keepOrder = prev.map(x => x.id).filter(id => stations.some(s => s.id === id));
      const added = stations.filter(s => !keepOrder.includes(s.id)).map(s => s.id);
      return [...keepOrder, ...added].map(id => ({ id, name: typed.get(id) ?? stations.find(s => s.id === id)!.name }));
    });
  }, [stations, open]);

  if (!isAdmin) return null;

  const start = () => {
    setList(stations.map(s => ({ id: s.id, name: s.name })));
    setNewName('');
    setError('');
    setInfo('');
    setOpen(true);
  };

  const move = (i: number, d: -1 | 1) => setList(l => {
    const j = i + d;
    if (j < 0 || j >= l.length) return l;
    const copy = [...l];
    [copy[i], copy[j]] = [copy[j], copy[i]];
    return copy;
  });

  const addStation = async () => {
    setError(''); setInfo('');
    const n = newName.trim();
    if (!n) return;
    try { await add.mutateAsync(n); setNewName(''); } catch (e) { setError(apiError(e)); }
  };

  const removeStation = async (id: number, name: string) => {
    setError(''); setInfo('');
    if (!window.confirm(`Убрать кухню «${name}»? Её блюда перейдут на первую кухню в списке.`)) return;
    try {
      const r = await remove.mutateAsync(id) as { movedDishes?: number };
      setList(l => l.filter(x => x.id !== id));
      if (r?.movedDishes) setInfo(`Блюд переведено на первую кухню: ${r.movedDishes}`);
    } catch (e) { setError(apiError(e)); }
  };

  const save = async () => {
    setError('');
    try {
      for (const s of list) {
        const n = s.name.trim();
        const was = stations.find(x => x.id === s.id)?.name;
        if (n && n !== was) await rename.mutateAsync({ id: s.id, name: n });
      }
      const ids = list.map(s => s.id);
      if (ids.join() !== stations.map(s => s.id).join()) await order.mutateAsync(ids);
      setOpen(false);
    } catch (e) { setError(apiError(e)); }
  };

  const busy = rename.isPending || add.isPending || order.isPending || remove.isPending;

  return (
    <>
      {compact ? (
        <Tooltip title="Кухни">
          <IconButton onClick={start} aria-label="Кухни" sx={{ border: 1, borderColor: 'divider' }}><KitchenIcon /></IconButton>
        </Tooltip>
      ) : (
        <Button variant="outlined" startIcon={<KitchenIcon />} onClick={start}>Кухни</Button>
      )}
      <Dialog open={open} onClose={() => setOpen(false)} maxWidth="xs" fullWidth>
        <DialogTitle>Кухни</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
          <Typography variant="body2" color="text.secondary">
            Блюдо уходит на ту кухню, что выбрана у него в меню. Блюда без метки идут на первую кухню в списке.
          </Typography>
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
            {list.map((s, i) => (
              <Box key={s.id} sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                <TextField size="small" label={i === 0 ? 'Кухня 1 · по умолчанию' : `Кухня ${i + 1}`} value={s.name} sx={{ flex: 1 }}
                  onChange={e => setList(l => l.map(x => (x.id === s.id ? { ...x, name: e.target.value } : x)))}
                  slotProps={{ htmlInput: { maxLength: 60 } }} />
                <IconButton size="small" onClick={() => move(i, -1)} disabled={i === 0} aria-label="Выше"><UpIcon fontSize="small" /></IconButton>
                <IconButton size="small" onClick={() => move(i, 1)} disabled={i === list.length - 1} aria-label="Ниже"><DownIcon fontSize="small" /></IconButton>
                <IconButton size="small" onClick={() => removeStation(s.id, s.name)} disabled={busy || list.length === 1} aria-label="Убрать кухню">
                  <DeleteIcon fontSize="small" />
                </IconButton>
              </Box>
            ))}
          </Box>
          <Box sx={{ display: 'flex', gap: 1 }}>
            <TextField size="small" label="Новая кухня" placeholder="Например, Бар" value={newName} sx={{ flex: 1 }}
              onChange={e => setNewName(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') void addStation(); }}
              slotProps={{ htmlInput: { maxLength: 60 } }} />
            <Button startIcon={<AddIcon />} onClick={addStation} disabled={busy || !newName.trim()}>Добавить</Button>
          </Box>
          {info && <Alert severity="info">{info}</Alert>}
          {error && <Alert severity="error">{error}</Alert>}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3 }}>
          <Button onClick={() => setOpen(false)} sx={{ color: 'text.secondary' }}>Закрыть</Button>
          <Button variant="contained" onClick={save} disabled={busy}>Сохранить</Button>
        </DialogActions>
      </Dialog>
    </>
  );
};
