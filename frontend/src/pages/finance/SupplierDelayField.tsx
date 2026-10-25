import * as React from 'react';
import { Box, Button, TextField, InputAdornment } from '@mui/material';
import { useSaveSupplierTerms } from '../../api/finance';
import { apiError } from '../../api/inventory';

const MAX_DELAY_DAYS = 365;

/** Отсрочка поставщика: срок оплаты для всех его накладных, где поставщик не указал срок в УПД */
export const SupplierDelayField: React.FC<{ inn: string; name: string | null; value: number | null }> = ({ inn, name, value }) => {
  const save = useSaveSupplierTerms();
  const [days, setDays] = React.useState(value?.toString() ?? '');
  const [error, setError] = React.useState('');
  React.useEffect(() => setDays(value?.toString() ?? ''), [value]);

  const submit = async () => {
    setError('');
    const parsed = days.trim() === '' ? null : Number(days);
    if (parsed !== null && (!Number.isInteger(parsed) || parsed < 0 || parsed > MAX_DELAY_DAYS)) {
      setError(`Отсрочка — целое число дней от 0 до ${MAX_DELAY_DAYS}`);
      return;
    }
    try {
      await save.mutateAsync({ inn, name, paymentDelayDays: parsed });
    } catch (e) {
      setError(apiError(e));
    }
  };

  return (
    <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      <TextField
        size="small"
        label="Отсрочка поставщика"
        value={days}
        onChange={e => setDays(e.target.value.replace(/\D/g, '').slice(0, 3))}
        error={!!error}
        helperText={error || 'Для всех его накладных, где срок не указан в УПД'}
        sx={{ width: { xs: '100%', sm: 260 } }}
        slotProps={{ input: { endAdornment: <InputAdornment position="end">дней</InputAdornment> } }}
      />
      <Button
        variant="outlined"
        onClick={submit}
        disabled={save.isPending || days === (value?.toString() ?? '')}
        sx={{ mt: { sm: 0.25 } }}
      >
        Сохранить
      </Button>
    </Box>
  );
};
