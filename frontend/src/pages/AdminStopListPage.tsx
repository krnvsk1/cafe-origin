import * as React from 'react';
import {
  Box, Typography, Tabs, Tab, TextField, InputAdornment, Paper, Switch,
  Chip, Alert, LinearProgress, FormControlLabel, Checkbox, Divider,
} from '@mui/material';
import { Search as SearchIcon, Block as BlockIcon } from '@mui/icons-material';
import { useStopList, useSetAvailability } from '../api/stoplist';

interface Row {
  id: number;
  name: string;
  group: string;
  price: number;
  isAvailable: boolean;
}

const AdminStopListPage: React.FC = () => {
  const { data, isLoading, error } = useStopList();
  const setAvailability = useSetAvailability();
  const [tab, setTab] = React.useState<'items' | 'addons'>('items');
  const [search, setSearch] = React.useState('');
  const [onlyStopped, setOnlyStopped] = React.useState(false);

  const rows: Row[] = React.useMemo(() => {
    if (!data) return [];
    if (tab === 'items') {
      return data.items
        // Карточки с вариантами не продаются сами по себе — стоп ставится на варианты
        .filter(i => !i.hasChildren)
        .map(i => ({ id: i.id, name: i.name, group: i.parentName ?? 'Без категории', price: i.price, isAvailable: i.isAvailable }));
    }
    return data.addons.map(a => ({ id: a.id, name: a.name, group: a.categoryName, price: a.price, isAvailable: a.isAvailable }));
  }, [data, tab]);

  const filtered = rows.filter(r =>
    (!onlyStopped || !r.isAvailable) &&
    (search.trim() === '' || `${r.name} ${r.group}`.toLowerCase().includes(search.trim().toLowerCase()))
  );

  const groups = React.useMemo(() => {
    const map = new Map<string, Row[]>();
    filtered.forEach(r => {
      if (!map.has(r.group)) map.set(r.group, []);
      map.get(r.group)!.push(r);
    });
    return Array.from(map.entries());
  }, [filtered]);

  const stoppedCount = (data?.items.filter(i => !i.hasChildren && !i.isAvailable).length ?? 0)
    + (data?.addons.filter(a => !a.isAvailable).length ?? 0);

  return (
    <Box sx={{ maxWidth: 900, mx: 'auto' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 1 }}>
        <BlockIcon color="error" />
        <Typography variant="h4" fontWeight={700}>Стоп-лист</Typography>
        {stoppedCount > 0 && <Chip color="error" label={`В стопе: ${stoppedCount}`} />}
      </Box>
      <Typography color="text.secondary" sx={{ mb: 3 }}>
        Выключите позицию, если она закончилась, — клиенты сразу перестанут её видеть и заказывать.
      </Typography>

      <Paper sx={{ p: 2, mb: 2 }}>
        <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}>
          <Tab value="items" label="Позиции меню" />
          <Tab value="addons" label="Добавки" />
        </Tabs>
        <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'center' }}>
          <TextField
            size="small"
            placeholder="Поиск"
            value={search}
            onChange={e => setSearch(e.target.value)}
            sx={{ flex: 1, minWidth: 200 }}
            InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon /></InputAdornment> }}
          />
          <FormControlLabel
            control={<Checkbox checked={onlyStopped} onChange={e => setOnlyStopped(e.target.checked)} />}
            label="Только в стопе"
          />
        </Box>
      </Paper>

      {isLoading && <LinearProgress />}
      {error && <Alert severity="error">Не удалось загрузить стоп-лист</Alert>}
      {setAvailability.isError && <Alert severity="error" sx={{ mb: 2 }}>Не удалось сохранить изменение</Alert>}

      {!isLoading && groups.length === 0 && (
        <Typography color="text.secondary" sx={{ textAlign: 'center', py: 4 }}>Ничего не найдено</Typography>
      )}

      {groups.map(([group, items]) => (
        <Paper key={group} sx={{ mb: 2, overflow: 'hidden' }}>
          <Typography variant="subtitle2" color="text.secondary" sx={{ px: 2, pt: 1.5, pb: 1, textTransform: 'uppercase' }}>
            {group}
          </Typography>
          <Divider />
          {items.map((r, idx) => (
            <Box key={r.id}>
              {idx > 0 && <Divider />}
              <Box
                sx={{
                  display: 'flex', alignItems: 'center', px: 2, py: 1.25, gap: 2,
                  opacity: r.isAvailable ? 1 : 0.65,
                }}
              >
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography fontWeight={600} noWrap>{r.name}</Typography>
                  <Typography variant="body2" color="text.secondary">{r.price} ₽</Typography>
                </Box>
                {!r.isAvailable && <Chip size="small" color="error" variant="outlined" label="В стопе" />}
                <Switch
                  checked={r.isAvailable}
                  onChange={e => setAvailability.mutate({ kind: tab, id: r.id, isAvailable: e.target.checked })}
                  inputProps={{ 'aria-label': `Доступность: ${r.name}` }}
                />
              </Box>
            </Box>
          ))}
        </Paper>
      ))}
    </Box>
  );
};

export default AdminStopListPage;
