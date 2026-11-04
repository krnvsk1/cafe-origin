import * as React from 'react';
import { FieldHint, TitleWithHint } from '../../components/Hint';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, TextField, Switch, FormControlLabel, Alert,
  CircularProgress, Box, Typography, InputAdornment, useMediaQuery, useTheme,
} from '@mui/material';
import { CheckCircleOutline as OkIcon, ErrorOutline as ErrIcon } from '@mui/icons-material';
import { useEgaisSettings, useSaveEgaisSettings, testUtmConnection, useEgaisState } from '../../api/egais';
import { apiError } from '../../api/inventory';
import { tokens } from '../../theme';

/** Подключение к ЕГАИС: адрес УТМ, ФСРАР ID, автопроверка входящих */
const EgaisSettingsDialog: React.FC<{ open: boolean; onClose: () => void }> = ({ open, onClose }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const { data: settings, isLoading } = useEgaisSettings(open);
  const save = useSaveEgaisSettings();
  const { data: state } = useEgaisState();
  const detected = state?.detectedFsrarId ?? null;

  const [enabled, setEnabled] = React.useState(true);
  const [utmUrl, setUtmUrl] = React.useState('http://localhost:8080');
  const [fsrarId, setFsrarId] = React.useState('');
  const [minutes, setMinutes] = React.useState('10');
  const [sendActs, setSendActs] = React.useState(true);
  const [deleteProcessed, setDeleteProcessed] = React.useState(false);
  const [error, setError] = React.useState('');
  const [test, setTest] = React.useState<{ ok: boolean; message: string } | null>(null);
  const [testing, setTesting] = React.useState(false);

  React.useEffect(() => {
    if (!open || !settings) return;
    // Если ЕГАИС ещё не настраивали — сразу предлагаем включить
    setEnabled(settings.savedInAdmin ? settings.enabled : true);
    setUtmUrl(settings.utmUrl || 'http://localhost:8080');
    setFsrarId(settings.fsrarId ?? '');
    setMinutes(String(settings.autoSyncMinutes ?? 10));
    setSendActs(settings.sendActs ?? true);
    setDeleteProcessed(settings.deleteProcessed ?? false);
    setError('');
    setTest(null);
  }, [open, settings]);

  const runTest = async () => {
    setTesting(true);
    setTest(null);
    try {
      setTest(await testUtmConnection(utmUrl.trim()));
    } catch (e) {
      setTest({ ok: false, message: apiError(e) });
    } finally {
      setTesting(false);
    }
  };

  const handleSave = async () => {
    setError('');
    if (enabled && !/^\d{3,12}$/.test(fsrarId.trim())) {
      setError('ФСРАР ID — только цифры, точно как на главной странице УТМ.');
      return;
    }
    try {
      await save.mutateAsync({
        enabled,
        utmUrl: utmUrl.trim(),
        fsrarId: fsrarId.trim(),
        autoSyncMinutes: Math.max(0, parseInt(minutes, 10) || 0),
        sendActs,
        deleteProcessed,
      });
      onClose();
    } catch (e) {
      setError(apiError(e));
    }
  };

  return (
    <Dialog open={open} onClose={() => !save.isPending && onClose()} fullScreen={fullScreen} maxWidth="sm" fullWidth>
      <DialogTitle>Подключение к ЕГАИС</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2.25 }}>
        {isLoading ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>
        ) : (
          <>
            {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}

            <FormControlLabel
              control={<Switch checked={enabled} onChange={e => setEnabled(e.target.checked)} />}
              label={<TitleWithHint hint="Получать накладные на алкоголь и отправлять акты приёмки"><Typography sx={{ fontWeight: 600 }}>Работать с ЕГАИС</Typography></TitleWithHint>}
              sx={{ alignItems: 'flex-start', '& .MuiSwitch-root': { mt: -0.5 } }}
            />

            <Box>
              <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start' }}>
                <TextField
                  label="Адрес УТМ"
                  value={utmUrl}
                  onChange={e => { setUtmUrl(e.target.value); setTest(null); }}
                  placeholder="http://localhost:8080"
                  fullWidth
                  helperText={<FieldHint text="Компьютер в заведении, где установлен УТМ с ключом. Если сервер на другом компьютере — укажите его адрес в сети, например http://192.168.1.20:8080" />}
                />
                <Button onClick={runTest} disabled={testing || !utmUrl.trim()} sx={{ bgcolor: t.surfaceMuted, flexShrink: 0, mt: 0.5, minHeight: 48 }}>
                  {testing ? <CircularProgress size={18} /> : 'Проверить'}
                </Button>
              </Box>
              {test && (
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, mt: 1, color: test.ok ? tokens.herb : tokens.chili }}>
                  {test.ok ? <OkIcon fontSize="small" /> : <ErrIcon fontSize="small" />}
                  <Typography variant="body2" sx={{ color: 'inherit', fontWeight: 600 }}>{test.message}</Typography>
                </Box>
              )}
            </Box>

            <TextField
              label="ФСРАР ID заведения"
              value={fsrarId}
              onChange={e => setFsrarId(e.target.value.replace(/\D/g, '').slice(0, 12))}
              placeholder="030000000000"
              helperText={<FieldHint text="Точно как на главной странице УТМ — по нему ЕГАИС проверяет подпись запросов" />}
              slotProps={{ htmlInput: { inputMode: 'numeric' } }}
            />
            {detected && detected !== fsrarId.trim() && (
              <Alert
                severity="warning"
                action={<Button color="inherit" size="small" onClick={() => setFsrarId(detected)}>Подставить</Button>}
              >
                УТМ указывает в документах ФСРАР ID <b>{detected}</b>. Если в настройках другой — ЕГАИС отклонит запросы («Нарушена целостность конверта»).
              </Alert>
            )}

            <TextField
              label="Проверять входящие"
              type="number"
              value={minutes}
              onChange={e => setMinutes(e.target.value)}
              helperText={<FieldHint text="0 — только по кнопке «Проверить входящие»" />}
              slotProps={{
                input: { startAdornment: <InputAdornment position="start">каждые</InputAdornment>, endAdornment: <InputAdornment position="end">мин</InputAdornment> },
                htmlInput: { min: 0, max: 1440, inputMode: 'numeric' },
              }}
              sx={{ maxWidth: 280 }}
            />

            <Box sx={{ border: '1px solid', borderColor: 'divider', borderRadius: '14px', p: 2, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
              <Typography sx={{ fontWeight: 700 }}>Если с этим УТМ работает iiko или другая программа</Typography>
              <FormControlLabel
                control={<Switch checked={sendActs} onChange={e => setSendActs(e.target.checked)} />}
                label={<TitleWithHint hint="Выключите, если накладные подтверждает iiko — тогда здесь товар только оприходуется на склад"><Typography sx={{ fontWeight: 600 }}>Отправлять акты приёмки из сервиса</Typography></TitleWithHint>}
                sx={{ alignItems: 'flex-start', '& .MuiSwitch-root': { mt: -0.5 } }}
              />
              <FormControlLabel
                control={<Switch checked={deleteProcessed} onChange={e => setDeleteProcessed(e.target.checked)} />}
                label={<TitleWithHint hint="Включайте, только если с УТМ работает один этот сервис — иначе iiko не увидит накладные"><Typography sx={{ fontWeight: 600 }}>Удалять прочитанные документы из УТМ</Typography></TitleWithHint>}
                sx={{ alignItems: 'flex-start', '& .MuiSwitch-root': { mt: -0.5 } }}
              />
            </Box>

            <Box sx={{ bgcolor: t.surfaceMuted, borderRadius: '14px', p: 2 }}>
              <Typography sx={{ fontWeight: 700, mb: 0.75 }}>Если УТМ ещё не установлен</Typography>
              <Box component="ol" sx={{ m: 0, pl: 2.5, color: 'text.secondary', '& li': { mb: 0.5 } }}>
                <li>Скачайте УТМ с egais.ru и установите на компьютер в заведении.</li>
                <li>Вставьте ключ с КЭП (Рутокен или JaCarta) — УТМ подпишет им акты.</li>
                <li>Откройте на этом компьютере http://localhost:8080 — там будет ФСРАР ID.</li>
                <li>Впишите адрес и ФСРАР ID сюда, нажмите «Проверить» и «Сохранить».</li>
              </Box>
            </Box>
          </>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        <Button onClick={onClose} disabled={save.isPending} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button
          variant="contained"
          color="secondary"
          onClick={handleSave}
          disabled={save.isPending || isLoading}
          startIcon={save.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}
        >
          Сохранить
        </Button>
      </DialogActions>
    </Dialog>
  );
};

export default EgaisSettingsDialog;
