import * as React from 'react';
import { Dialog, DialogTitle, DialogContent, DialogActions, Button, TextField, Box, Chip, Alert } from '@mui/material';
import { MAX_GRAMS, MIN_GRAMS, WEIGHT_PRESETS, validGrams, weightLabel } from '../utils/weight';

/** Поправить вес весового блюда: повар взвесил или гость передумал. Сумма пересчитается на сервере. */
const WeightEditDialog: React.FC<{
  item: { name: string; weight?: number | null } | null;
  onClose: () => void;
  onSave: (grams: number) => Promise<unknown>;
}> = ({ item, onClose, onSave }) => {
  const [text, setText] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [err, setErr] = React.useState('');
  React.useEffect(() => { if (item) { setText(String(item.weight ?? '')); setErr(''); } }, [item]);
  const grams = Math.round(Number(text));
  const ok = validGrams(grams);

  const save = async () => {
    setBusy(true);
    setErr('');
    try { await onSave(grams); onClose(); }
    catch (e) {
      const m = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setErr(m || 'Не удалось сохранить вес');
    } finally { setBusy(false); }
  };

  return (
    <Dialog open={!!item} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>Вес: {item?.name}</DialogTitle>
      <DialogContent>
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, mb: 2, mt: 0.5 }}>
          {WEIGHT_PRESETS.map(g => (
            <Chip key={g} label={weightLabel(g)} color={grams === g ? 'primary' : 'default'} onClick={() => setText(String(g))} />
          ))}
        </Box>
        <TextField
          autoFocus fullWidth label="Вес, г" type="number" value={text} onChange={e => setText(e.target.value)}
          inputProps={{ min: MIN_GRAMS, max: MAX_GRAMS, step: 10, inputMode: 'decimal' }}
          error={!!text && !ok} helperText={!text || ok ? 'Сумма в счёте пересчитается' : `от ${MIN_GRAMS} г до ${MAX_GRAMS / 1000} кг`}
          onKeyDown={e => { if (e.key === 'Enter' && ok && !busy) void save(); }}
        />
        {err && <Alert severity="error" sx={{ mt: 1.5 }}>{err}</Alert>}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Отмена</Button>
        <Button variant="contained" onClick={save} disabled={!ok || busy || grams === item?.weight}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

export default WeightEditDialog;
