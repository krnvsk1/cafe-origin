import * as React from 'react';
import { Box, TextField, Button, Alert, Typography, CircularProgress, InputAdornment } from '@mui/material';
import { Search as SearchIcon, MyLocation as MyLocationIcon } from '@mui/icons-material';
import { loadYandexMaps, geocodeAddress, reverseGeocode, isSuggestConfigured } from '../utils/yandexMaps';
import { useDeliveryZones, checkDeliveryZone } from '../api/zones';
import type { ZoneCheck } from '../api/zones';
import { useRestaurantSettings } from '../api/hooks';

/* eslint-disable @typescript-eslint/no-explicit-any */

interface Props {
  address: string;
  onAddressChange: (address: string) => void;
  coords: [number, number] | null;
  onCoordsChange: (coords: [number, number] | null) => void;
  subtotal: number;
  onZoneCheck: (check: ZoneCheck | null) => void;
  disabled?: boolean;
}

/**
 * Выбор адреса доставки на Яндекс Карте: поиск, клик по карте, перетаскивание метки.
 * Рисует зоны доставки и сообщает, в какую зону попал адрес и сколько стоит доставка.
 */
const AddressMapPicker: React.FC<Props> = ({
  address, onAddressChange, coords, onCoordsChange, subtotal, onZoneCheck, disabled,
}) => {
  const { data: zones } = useDeliveryZones();
  const { data: settings } = useRestaurantSettings();
  const containerRef = React.useRef<HTMLDivElement>(null);
  const inputRef = React.useRef<HTMLInputElement>(null);
  const mapRef = React.useRef<any>(null);
  const placemarkRef = React.useRef<any>(null);
  const zonesCollectionRef = React.useRef<any>(null);
  const [mapError, setMapError] = React.useState('');
  const [searching, setSearching] = React.useState(false);
  const [check, setCheck] = React.useState<ZoneCheck | null>(null);
  const [mapReady, setMapReady] = React.useState(false);

  // Колбэки держим в ref, чтобы обработчики карты всегда видели свежие версии
  const handlersRef = React.useRef({ onAddressChange, onCoordsChange });
  handlersRef.current = { onAddressChange, onCoordsChange };

  const setPoint = React.useCallback(async (point: [number, number], knownAddress?: string) => {
    handlersRef.current.onCoordsChange(point);
    const ymaps = window.ymaps;
    const map = mapRef.current;
    if (ymaps && map) {
      if (!placemarkRef.current) {
        placemarkRef.current = new ymaps.Placemark(point, {}, { preset: 'islands#redDotIcon', draggable: true });
        placemarkRef.current.events.add('dragend', () => {
          void setPoint(placemarkRef.current.geometry.getCoordinates());
        });
        map.geoObjects.add(placemarkRef.current);
      } else {
        placemarkRef.current.geometry.setCoordinates(point);
      }
      map.setCenter(point, Math.max(map.getZoom(), 15), { duration: 300 });
    }
    const resolved = knownAddress ?? (await reverseGeocode(point).catch(() => null));
    if (resolved) handlersRef.current.onAddressChange(resolved);
  }, []);

  // Инициализация карты
  React.useEffect(() => {
    let destroyed = false;
    loadYandexMaps()
      .then((ymaps) => {
        if (destroyed || !containerRef.current || mapRef.current) return;
        const center: [number, number] = coords ?? [settings?.latitude ?? 55.751244, settings?.longitude ?? 37.618423];
        const map = new ymaps.Map(containerRef.current, {
          center,
          zoom: coords ? 15 : 11,
          controls: ['zoomControl', 'geolocationControl'],
        }, { suppressMapOpenBlock: true });
        mapRef.current = map;
        zonesCollectionRef.current = new ymaps.GeoObjectCollection();
        map.geoObjects.add(zonesCollectionRef.current);
        setMapReady(true);

        map.events.add('click', (e: any) => {
          if (!disabled) void setPoint(e.get('coords'));
        });
        if (coords) void setPoint(coords, address || undefined);

        if (isSuggestConfigured() && inputRef.current) {
          const suggest = new ymaps.SuggestView(inputRef.current, { results: 5 });
          suggest.events.add('select', (e: any) => {
            const value = e.get('item').value as string;
            handlersRef.current.onAddressChange(value);
            void geocodeAddress(value).then(r => r && setPoint(r.coords, r.address));
          });
        }
      })
      .catch((err: Error) => setMapError(err.message));

    return () => {
      destroyed = true;
      mapRef.current?.destroy?.();
      mapRef.current = null;
      placemarkRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Отрисовка зон
  React.useEffect(() => {
    const ymaps = window.ymaps;
    const collection = zonesCollectionRef.current;
    if (!ymaps || !collection || !zones) return;
    collection.removeAll();
    zones.forEach(z => {
      collection.add(new ymaps.Polygon([z.polygon], { hintContent: `${z.name}: ${z.deliveryPrice} ₽, ~${z.deliveryMinutes} мин` }, {
        fillColor: z.color + '33',
        strokeColor: z.color,
        strokeWidth: 2,
        interactivityModel: 'default#transparent', // клики проходят на карту
      }));
    });
  }, [zones, mapReady]);

  // Проверка зоны при смене точки или суммы
  React.useEffect(() => {
    if (!coords) {
      setCheck(null);
      onZoneCheck(null);
      return;
    }
    let cancelled = false;
    checkDeliveryZone(coords[0], coords[1], subtotal)
      .then(result => {
        if (cancelled) return;
        setCheck(result);
        onZoneCheck(result);
      })
      .catch(() => { /* сеть — проверим при оформлении */ });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [coords?.[0], coords?.[1], subtotal]);

  const handleSearch = async () => {
    if (!address.trim()) return;
    setSearching(true);
    try {
      const result = await geocodeAddress(address);
      if (result) await setPoint(result.coords, result.address);
      else setMapError('Адрес не найден — уточните его или отметьте точку на карте');
    } catch {
      setMapError('Не удалось найти адрес');
    } finally {
      setSearching(false);
    }
  };

  return (
    <Box sx={{ mt: 2 }}>
      <TextField
        fullWidth
        inputRef={inputRef}
        label="Адрес доставки *"
        value={address}
        onChange={(e) => { onAddressChange(e.target.value); setMapError(''); }}
        onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void handleSearch(); } }}
        onBlur={() => { if (!coords && address.trim()) void handleSearch(); }}
        disabled={disabled}
        placeholder="Улица, дом"
        helperText="Найдите адрес или нажмите на карте. Квартиру и подъезд укажите в комментарии."
        sx={{ '& .MuiOutlinedInput-root': { borderRadius: 3 } }}
        InputProps={{
          endAdornment: (
            <InputAdornment position="end">
              <Button size="small" onClick={handleSearch} disabled={disabled || searching} startIcon={searching ? <CircularProgress size={14} /> : <SearchIcon />}>
                Найти
              </Button>
            </InputAdornment>
          ),
        }}
      />
      {mapError && <Alert severity="warning" sx={{ mt: 1 }}>{mapError}</Alert>}
      <Box
        ref={containerRef}
        sx={{ height: 260, borderRadius: 3, overflow: 'hidden', mt: 1.5, border: (t) => `1px solid ${t.palette.divider}`, bgcolor: 'action.hover' }}
      />
      {!coords && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'flex', alignItems: 'center', gap: 0.5, mt: 0.5 }}>
          <MyLocationIcon sx={{ fontSize: 14 }} /> Отметьте точку доставки на карте
        </Typography>
      )}
      {check && !check.deliverable && <Alert severity="error" sx={{ mt: 1 }}>{check.message}</Alert>}
      {check?.deliverable && (
        <Alert severity={check.minOrderReached ? 'success' : 'warning'} sx={{ mt: 1 }}>
          {check.zoneName}: доставка {check.deliveryPrice ? `${check.deliveryPrice} ₽` : 'бесплатно'}, ~{check.deliveryMinutes} мин
          {!check.minOrderReached && ` · минимальная сумма заказа ${check.minOrderAmount} ₽`}
          {check.minOrderReached && !!check.deliveryPrice && !!check.freeDeliveryFrom ? ` · бесплатно от ${check.freeDeliveryFrom} ₽` : ''}
        </Alert>
      )}
    </Box>
  );
};

export default AddressMapPicker;
