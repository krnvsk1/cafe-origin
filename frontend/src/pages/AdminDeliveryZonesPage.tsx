import * as React from 'react';
import {
  Box, Typography, Paper, Button, TextField, Switch, FormControlLabel, Alert, List, ListItemButton,
  ListItemText, Chip, Divider, LinearProgress, InputAdornment,
} from '@mui/material';
import { Add as AddIcon, Map as MapIcon, Delete as DeleteIcon, Edit as EditIcon } from '@mui/icons-material';
import { useAllDeliveryZones, useSaveDeliveryZone, useDeleteDeliveryZone } from '../api/zones';
import type { DeliveryZone, DeliveryZoneInput } from '../api/zones';
import { loadYandexMaps, isMapsConfigured } from '../utils/yandexMaps';
import { useRestaurantSettings } from '../api/hooks';

/* eslint-disable @typescript-eslint/no-explicit-any */

const COLORS = ['#0891b2', '#16a34a', '#f59e0b', '#dc2626', '#7c3aed', '#db2777'];

const emptyZone = (sortOrder: number): DeliveryZoneInput => ({
  name: '',
  polygon: [],
  deliveryPrice: 0,
  minOrderAmount: 0,
  freeDeliveryFrom: null,
  deliveryMinutes: 45,
  color: COLORS[sortOrder % COLORS.length],
  isActive: true,
  sortOrder,
});

const AdminDeliveryZonesPage: React.FC = () => {
  const { data: zones, isLoading } = useAllDeliveryZones();
  const { data: settings } = useRestaurantSettings();
  const save = useSaveDeliveryZone();
  const remove = useDeleteDeliveryZone();

  const [editingId, setEditingId] = React.useState<number | 'new' | null>(null);
  const [form, setForm] = React.useState<DeliveryZoneInput>(emptyZone(0));
  const [error, setError] = React.useState('');

  const containerRef = React.useRef<HTMLDivElement>(null);
  const mapRef = React.useRef<any>(null);
  const collectionRef = React.useRef<any>(null);
  const editorRef = React.useRef<any>(null);
  const [mapReady, setMapReady] = React.useState(false);
  const [mapError, setMapError] = React.useState('');

  // Карта
  React.useEffect(() => {
    if (!isMapsConfigured()) return;
    let destroyed = false;
    loadYandexMaps()
      .then(ymaps => {
        if (destroyed || !containerRef.current || mapRef.current) return;
        mapRef.current = new ymaps.Map(containerRef.current, {
          center: [settings?.latitude ?? 55.751244, settings?.longitude ?? 37.618423],
          zoom: 11,
          controls: ['zoomControl', 'searchControl'],
        }, { suppressMapOpenBlock: true });
        collectionRef.current = new ymaps.GeoObjectCollection();
        mapRef.current.geoObjects.add(collectionRef.current);
        setMapReady(true);
      })
      .catch((e: Error) => setMapError(e.message));
    return () => {
      destroyed = true;
      mapRef.current?.destroy?.();
      mapRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Отрисовка: все зоны; редактируемая — с редактором вершин
  React.useEffect(() => {
    const ymaps = window.ymaps;
    const collection = collectionRef.current;
    if (!ymaps || !collection || !mapReady) return;

    collection.removeAll();
    editorRef.current = null;

    (zones ?? []).forEach(z => {
      if (editingId === z.id) return;
      collection.add(new ymaps.Polygon([z.polygon], { hintContent: z.name }, {
        fillColor: z.color + (z.isActive ? '33' : '11'),
        strokeColor: z.color,
        strokeWidth: 2,
        strokeStyle: z.isActive ? 'solid' : 'dash',
      }));
    });

    if (editingId !== null) {
      const polygon = new ymaps.Polygon([form.polygon], {}, {
        fillColor: form.color + '55',
        strokeColor: form.color,
        strokeWidth: 3,
        editorDrawingCursor: 'crosshair',
      });
      collection.add(polygon);
      // Сохраняем вершины в форму при каждом изменении
      polygon.geometry.events.add('change', () => {
        const ring = (polygon.geometry.getCoordinates()[0] ?? []) as [number, number][];
        // У замкнутого контура последняя точка повторяет первую — убираем её
        const cleaned = ring.length > 1 && ring[0][0] === ring[ring.length - 1][0] && ring[0][1] === ring[ring.length - 1][1]
          ? ring.slice(0, -1)
          : ring;
        setForm(f => ({ ...f, polygon: cleaned }));
      });
      editorRef.current = polygon.editor;
      if (form.polygon.length === 0) polygon.editor.startDrawing();
      else {
        polygon.editor.startEditing();
        mapRef.current?.setBounds(polygon.geometry.getBounds(), { checkZoomRange: true, zoomMargin: 40 });
      }
    }
    // form.polygon намеренно не в зависимостях — иначе редактор пересоздавался бы на каждую точку
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [zones, editingId, mapReady, form.color]);

  const startNew = () => {
    setError('');
    setForm(emptyZone(zones?.length ?? 0));
    setEditingId('new');
  };

  const startEdit = (z: DeliveryZone) => {
    setError('');
    const { id: _id, ...rest } = z;
    void _id;
    setForm(rest);
    setEditingId(z.id);
  };

  const handleSave = async () => {
    setError('');
    try {
      await save.mutateAsync({ id: editingId === 'new' ? undefined : editingId ?? undefined, data: form });
      setEditingId(null);
    } catch (e: any) {
      setError(e?.response?.data?.message || 'Не удалось сохранить зону');
    }
  };

  const handleDelete = async () => {
    if (typeof editingId !== 'number') return;
    if (!window.confirm(`Удалить зону «${form.name}»?`)) return;
    await remove.mutateAsync(editingId);
    setEditingId(null);
  };

  const num = (v: string) => (v === '' ? 0 : Math.max(0, Number(v)));

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 2 }}>
        <MapIcon color="primary" />
        <Typography variant="h4" fontWeight={700} sx={{ flex: 1 }}>Зоны доставки</Typography>
        <Button variant="contained" startIcon={<AddIcon />} onClick={startNew} disabled={!isMapsConfigured() || editingId !== null}>
          Новая зона
        </Button>
      </Box>

      {!isMapsConfigured() && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          Чтобы рисовать зоны и показывать карту клиентам, задайте переменную <code>VITE_YANDEX_MAPS_API_KEY</code> (ключ JavaScript API Яндекс Карт) и пересоберите фронтенд.
        </Alert>
      )}
      {mapError && <Alert severity="error" sx={{ mb: 2 }}>{mapError}</Alert>}
      {(zones?.length ?? 0) === 0 && !isLoading && (
        <Alert severity="info" sx={{ mb: 2 }}>
          Пока зон нет — доставка принимается по любому адресу. Как только появится первая активная зона, адрес нужно будет отмечать на карте.
        </Alert>
      )}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '340px 1fr' }, gap: 2 }}>
        <Paper sx={{ p: 2 }}>
          {isLoading && <LinearProgress />}
          {editingId === null ? (
            <List dense disablePadding>
              {(zones ?? []).map(z => (
                <ListItemButton key={z.id} onClick={() => startEdit(z)} sx={{ borderRadius: 2, mb: 0.5 }}>
                  <Box sx={{ width: 14, height: 14, borderRadius: '4px', bgcolor: z.color, mr: 1.5, flexShrink: 0 }} />
                  <ListItemText
                    primary={z.name}
                    secondary={`${z.deliveryPrice} ₽ · от ${z.minOrderAmount} ₽ · ~${z.deliveryMinutes} мин${z.freeDeliveryFrom ? ` · бесплатно от ${z.freeDeliveryFrom} ₽` : ''}`}
                  />
                  {!z.isActive && <Chip size="small" label="выкл" />}
                  <EditIcon fontSize="small" sx={{ ml: 1, color: 'text.secondary' }} />
                </ListItemButton>
              ))}
            </List>
          ) : (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
              <Typography variant="subtitle1" fontWeight={700}>{editingId === 'new' ? 'Новая зона' : 'Редактирование'}</Typography>
              <Alert severity="info" icon={false} sx={{ py: 0 }}>
                {form.polygon.length === 0
                  ? 'Кликайте по карте, чтобы поставить вершины зоны. Двойной клик — закончить.'
                  : 'Перетаскивайте вершины, чтобы изменить границы.'}
              </Alert>
              <TextField size="small" label="Название" value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} />
              <TextField size="small" type="number" label="Стоимость доставки" value={form.deliveryPrice}
                onChange={e => setForm(f => ({ ...f, deliveryPrice: num(e.target.value) }))}
                InputProps={{ endAdornment: <InputAdornment position="end">₽</InputAdornment> }} />
              <TextField size="small" type="number" label="Минимальная сумма заказа" value={form.minOrderAmount}
                onChange={e => setForm(f => ({ ...f, minOrderAmount: num(e.target.value) }))}
                InputProps={{ endAdornment: <InputAdornment position="end">₽</InputAdornment> }} />
              <TextField size="small" type="number" label="Бесплатная доставка от (необязательно)" value={form.freeDeliveryFrom ?? ''}
                onChange={e => setForm(f => ({ ...f, freeDeliveryFrom: e.target.value === '' ? null : num(e.target.value) }))}
                InputProps={{ endAdornment: <InputAdornment position="end">₽</InputAdornment> }} />
              <TextField size="small" type="number" label="Время доставки" value={form.deliveryMinutes}
                onChange={e => setForm(f => ({ ...f, deliveryMinutes: Math.max(1, num(e.target.value)) }))}
                InputProps={{ endAdornment: <InputAdornment position="end">мин</InputAdornment> }} />
              <Box sx={{ display: 'flex', gap: 1 }}>
                {COLORS.map(c => (
                  <Box key={c} onClick={() => setForm(f => ({ ...f, color: c }))}
                    sx={{ width: 28, height: 28, borderRadius: '8px', bgcolor: c, cursor: 'pointer', outline: form.color === c ? '3px solid' : 'none', outlineColor: 'text.primary' }} />
                ))}
              </Box>
              <FormControlLabel control={<Switch checked={form.isActive} onChange={e => setForm(f => ({ ...f, isActive: e.target.checked }))} />} label="Зона активна" />
              {error && <Alert severity="error">{error}</Alert>}
              <Divider />
              <Box sx={{ display: 'flex', gap: 1 }}>
                {typeof editingId === 'number' && (
                  <Button color="error" startIcon={<DeleteIcon />} onClick={handleDelete}>Удалить</Button>
                )}
                <Box sx={{ flex: 1 }} />
                <Button onClick={() => setEditingId(null)}>Отмена</Button>
                <Button variant="contained" onClick={handleSave} disabled={save.isPending || form.polygon.length < 3 || !form.name.trim()}>
                  Сохранить
                </Button>
              </Box>
            </Box>
          )}
        </Paper>

        <Paper sx={{ overflow: 'hidden', minHeight: 480 }}>
          <Box ref={containerRef} sx={{ width: '100%', height: { xs: 420, md: 600 } }} />
        </Paper>
      </Box>
    </Box>
  );
};

export default AdminDeliveryZonesPage;
