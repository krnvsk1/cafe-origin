import * as React from 'react';
import { FieldHint, TitleWithHint } from '../../components/Hint';
import {
  Box, Typography, Button, TextField, Switch, FormControlLabel, Alert, CircularProgress, Chip,
  Dialog, DialogTitle, DialogContent, IconButton, useMediaQuery, useTheme,
} from '@mui/material';
import { Close as CloseIcon } from '@mui/icons-material';
import { useSaveChzSettings, useChzState, type ChzState } from '../../api/chz';
import { apiError } from '../../api/inventory';

/** Настройки Честного знака: ИНН, место деятельности (ФИАС, КПП), товарные группы, песочница */
export const ChzSettings: React.FC<{ state: ChzState; onDone: () => void }> = ({ state, onDone }) => {
  const save = useSaveChzSettings();
  const [enabled, setEnabled] = React.useState(state.enabled || !state.inn);
  const [inn, setInn] = React.useState(state.inn);
  const [sandbox, setSandbox] = React.useState(state.sandbox);
  const [fiasId, setFiasId] = React.useState(state.fiasId ?? '');
  const [kpp, setKpp] = React.useState(state.kpp ?? '');
  const [groups, setGroups] = React.useState<string[]>(state.productGroups.length ? state.productGroups : ['milk', 'water', 'beer']);
  const [error, setError] = React.useState('');

  const submit = async () => {
    setError('');
    try {
      await save.mutateAsync({ enabled, inn: inn.trim(), sandbox, productGroups: groups, fiasId: fiasId.trim() || null, kpp: kpp.trim() || null });
      onDone();
    } catch (e) {
      setError(apiError(e));
    }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {error && <Alert severity="error">{error}</Alert>}
      <FormControlLabel control={<Switch checked={enabled} onChange={e => setEnabled(e.target.checked)} />} label="Работать с Честным знаком" />
      <TextField
        label="ИНН организации"
        value={inn}
        onChange={e => setInn(e.target.value.replace(/\D/g, '').slice(0, 12))}
        helperText={<FieldHint text="Тот же, что в сертификате КЭП руководителя" />}
        slotProps={{ htmlInput: { inputMode: 'decimal' } }}
        sx={{ maxWidth: 320 }}
      />
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 200px' }, gap: 1.5 }}>
        <TextField
          label="Код ФИАС места деятельности (МОД)"
          value={fiasId}
          onChange={e => setFiasId(e.target.value.trim())}
          placeholder="0c5b2444-70a0-4932-980c-b4dc0d3f02b5"
          helperText={<FieldHint text="Обязателен для пива и воды. ЛК Честного знака → Профиль → Места осуществления деятельности" />}
        />
        <TextField
          label="КПП"
          value={kpp}
          onChange={e => setKpp(e.target.value.replace(/\D/g, '').slice(0, 9))}
          helperText={<FieldHint text={inn.length === 12 ? 'У ИП нет — оставьте пустым' : 'Для организации'} />}
          slotProps={{ htmlInput: { inputMode: 'decimal' } }}
        />
      </Box>
      <Box>
        <Typography sx={{ fontWeight: 600, mb: 1 }}>Товарные группы</Typography>
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
          {state.groups.map(g => {
            const on = groups.includes(g.key);
            return (
              <Chip
                key={g.key}
                label={g.title}
                color={on ? 'primary' : 'default'}
                onClick={() => setGroups(on ? groups.filter(x => x !== g.key) : [...groups, g.key])}
              />
            );
          })}
        </Box>
      </Box>
      <FormControlLabel
        control={<Switch checked={sandbox} onChange={e => setSandbox(e.target.checked)} />}
        label={<TitleWithHint hint="Для проверки без реального вывода кодов"><Typography>Тестовый контур (песочница)</Typography></TitleWithHint>}
        sx={{ alignItems: 'flex-start', '& .MuiSwitch-root': { mt: -0.5 } }}
      />
      <Box sx={{ display: 'flex', gap: 1 }}>
        <Button variant="contained" color="secondary" onClick={submit} disabled={save.isPending}
          startIcon={save.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Сохранить
        </Button>
        {state.enabled && <Button onClick={onDone} sx={{ color: 'text.secondary' }}>Отмена</Button>}
      </Box>
    </Box>
  );
};


/** Настройки Честного знака в окне (из «Настройки интеграций») */
const ChzSettingsDialog: React.FC<{ open: boolean; onClose: () => void }> = ({ open, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const { data: state } = useChzState();
  return (
    <Dialog open={open} onClose={onClose} fullScreen={fullScreen} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', pr: 1.5 }}>
        <Box sx={{ flex: 1 }}>Подключение к Честному знаку</Box>
        <IconButton aria-label="Закрыть" onClick={onClose}><CloseIcon /></IconButton>
      </DialogTitle>
      <DialogContent sx={{ pt: '8px !important', pb: 3 }}>
        {state ? <ChzSettings key={open ? 'open' : 'closed'} state={state} onDone={onClose} /> : <CircularProgress />}
      </DialogContent>
    </Dialog>
  );
};

export default ChzSettingsDialog;
