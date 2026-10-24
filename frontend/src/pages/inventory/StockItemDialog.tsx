import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, TextField, Autocomplete,
  ToggleButtonGroup, ToggleButton, Box, Typography, InputAdornment, Alert, CircularProgress,
  useMediaQuery, useTheme, FormControlLabel, Switch,
} from '@mui/material';
import {
  useSaveStockItem, useStockCategories, apiError, UNIT_LABELS,
  type StockItem, type StockItemInput, type StockUnit, type StockItemKind,
} from '../../api/inventory';

interface Props {
  open: boolean;
  onClose: () => void;
  item?: StockItem | null;
  /** Название, введённое в поиске — для быстрого создания */
  initialName?: string;
  initialKind?: StockItemKind;
  /** Разрешить выбор «полуфабрикат» (в накладных — только сырьё) */
  allowSemi?: boolean;
  onSaved?: (item: StockItem) => void;
}

const UNITS: StockUnit[] = ['kg', 'l', 'pcs'];

const StockItemDialog: React.FC<Props> = ({ open, onClose, item, initialName, initialKind = 'raw', allowSemi = true, onSaved }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const save = useSaveStockItem();
  const { data: categories = [] } = useStockCategories();

  const [form, setForm] = React.useState<StockItemInput>({ name: '', unit: 'kg', kind: 'raw', category: '', minQuantity: 0, lastPrice: null });
  const [error, setError] = React.useState('');

  React.useEffect(() => {
    if (!open) return;
    setError('');
    setForm(item
      ? { name: item.name, unit: item.unit, kind: item.kind, category: item.category ?? '', minQuantity: item.minQuantity, lastPrice: item.lastPrice || null, isArchived: item.isArchived }
      : { name: initialName ?? '', unit: 'kg', kind: initialKind, category: '', minQuantity: 0, lastPrice: null });
  }, [open, item, initialName, initialKind]);

  const set = <K extends keyof StockItemInput>(key: K, value: StockItemInput[K]) => setForm(f => ({ ...f, [key]: value }));

  const handleSave = async () => {
    if (!form.name.trim()) {
      setError('Введите название');
      return;
    }
    try {
      const saved = await save.mutateAsync({ id: item?.id, data: { ...form, name: form.name.trim() } });
      onSaved?.(saved);
      onClose();
    } catch (e) {
      setError(apiError(e));
    }
  };

  const unitLabel = UNIT_LABELS[form.unit];
  const isSemi = form.kind === 'semi';

  return (
    <Dialog open={open} onClose={() => !save.isPending && onClose()} fullScreen={fullScreen} maxWidth="xs" fullWidth>
      <DialogTitle>{item ? 'Позиция склада' : 'Новая позиция'}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2.25, pt: '8px !important' }}>
        {error && <Alert severity="error">{error}</Alert>}

        {allowSemi && (
          <Box>
            <ToggleButtonGroup
              exclusive
              value={form.kind}
              onChange={(_, v) => v && set('kind', v)}
              sx={{ display: 'flex', '& .MuiToggleButton-root': { flex: 1 } }}
            >
              <ToggleButton value="raw">Сырьё</ToggleButton>
              <ToggleButton value="semi">Полуфабрикат</ToggleButton>
            </ToggleButtonGroup>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.75 }}>
              {isSemi
                ? 'Готовите сами по своей техкарте (соус, тесто). На складе не хранится — списывается сырьё из состава.'
                : 'Закупаете у поставщиков: продукты, напитки, упаковка.'}
            </Typography>
          </Box>
        )}

        <TextField
          label="Название"
          value={form.name}
          onChange={e => set('name', e.target.value)}
          autoFocus
          fullWidth
          placeholder={isSemi ? 'Например: Соус чесночный' : 'Например: Филе бедра куриное'}
          slotProps={{ htmlInput: { maxLength: 150 } }}
        />

        <Box>
          <Typography variant="body2" sx={{ fontWeight: 600, mb: 0.75 }}>Единица учёта</Typography>
          <ToggleButtonGroup
            exclusive
            value={form.unit}
            onChange={(_, v) => v && set('unit', v)}
            disabled={!!item && item.quantity !== 0}
            sx={{ display: 'flex', '& .MuiToggleButton-root': { flex: 1 } }}
          >
            {UNITS.map(u => <ToggleButton key={u} value={u}>{UNIT_LABELS[u]}</ToggleButton>)}
          </ToggleButtonGroup>
          {!!item && item.quantity !== 0 && (
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.75 }}>
              Единицу нельзя сменить, пока на складе есть остаток
            </Typography>
          )}
        </Box>

        <Autocomplete
          freeSolo
          options={categories}
          value={form.category ?? ''}
          inputValue={form.category ?? ''}
          onInputChange={(_, v) => set('category', v)}
          renderInput={params => <TextField {...params} label="Категория" placeholder="Мясо, овощи, упаковка…" />}
        />

        {!isSemi && (
          <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5 }}>
            <TextField
              label="Мин. остаток"
              type="number"
              value={form.minQuantity || ''}
              onChange={e => set('minQuantity', Math.max(0, parseFloat(e.target.value) || 0))}
              helperText="Предупредим, когда меньше"
              slotProps={{
                input: { endAdornment: <InputAdornment position="end">{unitLabel}</InputAdornment> },
                htmlInput: { min: 0, step: 'any', inputMode: 'decimal' },
              }}
            />
            <TextField
              label="Цена"
              type="number"
              value={form.lastPrice ?? ''}
              onChange={e => set('lastPrice', e.target.value === '' ? null : Math.max(0, parseFloat(e.target.value) || 0))}
              helperText={item ? 'Последняя закупочная' : 'Для расчёта техкарт до первого прихода'}
              slotProps={{
                input: { endAdornment: <InputAdornment position="end">₽/{unitLabel}</InputAdornment> },
                htmlInput: { min: 0, step: 'any', inputMode: 'decimal' },
              }}
            />
          </Box>
        )}

        {item && (
          <FormControlLabel
            control={<Switch checked={!!form.isArchived} onChange={e => set('isArchived', e.target.checked)} />}
            label="В архиве — скрыть из списков"
          />
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        <Button onClick={onClose} disabled={save.isPending} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button
          variant="contained"
          color="secondary"
          onClick={handleSave}
          disabled={save.isPending}
          startIcon={save.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}
        >
          {item ? 'Сохранить' : 'Создать'}
        </Button>
      </DialogActions>
    </Dialog>
  );
};

export default StockItemDialog;
