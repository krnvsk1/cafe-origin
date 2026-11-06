import * as React from 'react';
import {
  Box, Button, Typography, CircularProgress, Dialog, DialogTitle, DialogContent, DialogActions, TextField,
  Alert, FormControlLabel, Switch, useMediaQuery, useTheme,
} from '@mui/material';
import { Add as AddIcon, LocalShippingOutlined as TruckIcon, ChevronRight as ChevronRightIcon } from '@mui/icons-material';
import {
  useSuppliers, useSaveSupplier, useDeleteSupplier, apiError, fmtMoney, fmtDate,
  type Supplier, type SupplierInput,
} from '../../api/inventory';
import { Panel, ListRow, EmptyState, Pill } from './ui';

export const SupplierDialog: React.FC<{
  open: boolean;
  supplier?: Supplier | null;
  initialName?: string;
  onClose: () => void;
  onSaved?: (s: Supplier) => void;
}> = ({ open, supplier, initialName, onClose, onSaved }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const save = useSaveSupplier();
  const remove = useDeleteSupplier();
  const [form, setForm] = React.useState<SupplierInput>({ name: '', inn: '', phone: '', contactPerson: '', notes: '' });
  const [error, setError] = React.useState('');

  React.useEffect(() => {
    if (!open) return;
    setError('');
    setForm(supplier
      ? { name: supplier.name, inn: supplier.inn ?? '', phone: supplier.phone ?? '', contactPerson: supplier.contactPerson ?? '', notes: supplier.notes ?? '', isArchived: supplier.isArchived }
      : { name: initialName ?? '', inn: '', phone: '', contactPerson: '', notes: '' });
  }, [open, supplier, initialName]);

  const set = (key: keyof SupplierInput, value: string | boolean) => setForm(f => ({ ...f, [key]: value }));

  const handleSave = async () => {
    if (!form.name.trim()) { setError('Введите название'); return; }
    if (form.inn && !/^\d{10}(\d{2})?$/.test(form.inn.trim())) { setError('ИНН — 10 или 12 цифр'); return; }
    try {
      const saved = await save.mutateAsync({ id: supplier?.id, data: form });
      onSaved?.(saved);
      onClose();
    } catch (e) {
      setError(apiError(e));
    }
  };

  const handleDelete = async () => {
    if (!supplier) return;
    try {
      await remove.mutateAsync(supplier.id);
      onClose();
    } catch (e) {
      setError(apiError(e));
    }
  };

  const busy = save.isPending || remove.isPending;

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="xs" fullWidth>
      <DialogTitle>{supplier ? 'Поставщик' : 'Новый поставщик'}</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField label="Название" value={form.name} onChange={e => set('name', e.target.value)} autoFocus placeholder="ООО «Мясной двор»" />
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5 }}>
          <TextField label="ИНН" value={form.inn ?? ''} onChange={e => set('inn', e.target.value.replace(/\D/g, ''))} slotProps={{ htmlInput: { maxLength: 12, inputMode: 'decimal' } }} />
          <TextField label="Телефон" value={form.phone ?? ''} onChange={e => set('phone', e.target.value)} slotProps={{ htmlInput: { inputMode: 'decimal' } }} />
        </Box>
        <TextField label="Контактное лицо" value={form.contactPerson ?? ''} onChange={e => set('contactPerson', e.target.value)} />
        <TextField label="Заметки" value={form.notes ?? ''} onChange={e => set('notes', e.target.value)} multiline minRows={2} placeholder="Дни поставки, условия оплаты…" />
        {supplier && (
          <FormControlLabel control={<Switch checked={!!form.isArchived} onChange={e => set('isArchived', e.target.checked)} />} label="В архиве" />
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        {supplier && (
          <Button onClick={handleDelete} disabled={busy} color="error" sx={{ mr: 'auto' }}>Удалить</Button>
        )}
        <Button onClick={onClose} disabled={busy} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={handleSave} disabled={busy}
          startIcon={save.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}>
          {supplier ? 'Сохранить' : 'Создать'}
        </Button>
      </DialogActions>
    </Dialog>
  );
};

const SuppliersTab: React.FC = () => {
  const [includeArchived, setIncludeArchived] = React.useState(false);
  const { data: suppliers = [], isLoading } = useSuppliers(includeArchived);
  const [editing, setEditing] = React.useState<Supplier | null | undefined>(undefined);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
        <FormControlLabel control={<Switch checked={includeArchived} onChange={e => setIncludeArchived(e.target.checked)} />} label="Показать архив" />
        <Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={() => setEditing(null)} sx={{ ml: 'auto' }}>
          Поставщик
        </Button>
      </Box>

      <Panel padding={false}>
        {isLoading ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress size={32} /></Box>
        ) : suppliers.length === 0 ? (
          <EmptyState
            icon={<TruckIcon />}
            title="Поставщиков пока нет"
            text="Добавьте их здесь или прямо при оформлении прихода"
            action={<Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={() => setEditing(null)}>Добавить поставщика</Button>}
          />
        ) : suppliers.map(s => (
          <ListRow key={s.id} onClick={() => setEditing(s)} muted={s.isArchived}>
            <Box sx={{ flex: 1, minWidth: 0 }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Typography sx={{ fontWeight: 600 }} noWrap>{s.name}</Typography>
                {s.isArchived && <Pill>Архив</Pill>}
              </Box>
              <Typography variant="body2" color="text.secondary" noWrap>
                {[s.inn && `ИНН ${s.inn}`, s.phone, s.contactPerson].filter(Boolean).join(' · ') || 'Нет контактов'}
              </Typography>
            </Box>
            <Box sx={{ textAlign: 'right' }}>
              <Typography sx={{ fontWeight: 700 }}>{fmtMoney(s.receiptsTotal)}</Typography>
              <Typography variant="caption" color="text.secondary">
                {s.receiptsCount > 0 ? `${s.receiptsCount} прих. · ${fmtDate(s.lastReceiptAt)}` : 'приходов не было'}
              </Typography>
            </Box>
            <ChevronRightIcon sx={{ color: 'text.secondary', display: { xs: 'none', sm: 'block' } }} />
          </ListRow>
        ))}
      </Panel>

      <SupplierDialog open={editing !== undefined} supplier={editing} onClose={() => setEditing(undefined)} />
    </Box>
  );
};

export default SuppliersTab;
