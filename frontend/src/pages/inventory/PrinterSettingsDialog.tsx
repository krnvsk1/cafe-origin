import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, TextField, Box, Typography, Alert, FormControlLabel, Switch, MenuItem,
} from '@mui/material';
import { DownloadOutlined as DownloadIcon, PrintOutlined as PrintIcon } from '@mui/icons-material';
import { useSavePrinting, useTestPrint, useNewPrintToken, downloadPrintAgent, type PrintingState } from '../../api/printing';
import { apiError } from '../../api/inventory';

/** Кодировки, которые понимают чековые принтеры RONGTA/Xprinter: номер таблицы ESC t и кодировка текста */
const CODE_PAGES = [
  { key: '17-866', label: 'PC866 — таблица 17 (RONGTA, Epson)', codeTable: 17, encoding: 866 as const },
  { key: '7-866', label: 'PC866 — таблица 7', codeTable: 7, encoding: 866 as const },
  { key: '6-866', label: 'PC866 — таблица 6', codeTable: 6, encoding: 866 as const },
  { key: '46-1251', label: 'Windows-1251 — таблица 46', codeTable: 46, encoding: 1251 as const },
  { key: '21-1251', label: 'Windows-1251 — таблица 21', codeTable: 21, encoding: 1251 as const },
];

const PrinterSettingsDialog: React.FC<{ open: boolean; state: PrintingState; onClose: () => void }> = ({ open, state, onClose }) => {
  const save = useSavePrinting();
  const test = useTestPrint();
  const newToken = useNewPrintToken();
  const [enabled, setEnabled] = React.useState(state.enabled);
  const [mode, setMode] = React.useState<'agent' | 'network'>(state.mode ?? 'agent');
  const [host, setHost] = React.useState(state.host ?? '');
  const [port, setPort] = React.useState(String(state.port ?? 9100));
  const [printerName, setPrinterName] = React.useState(state.printerName);
  const [width, setWidth] = React.useState(String(state.width));
  const [codePage, setCodePage] = React.useState(`${state.codeTable}-${state.encoding}`);
  const [footer, setFooter] = React.useState(state.footer ?? '');
  const [msg, setMsg] = React.useState<{ ok: boolean; text: string } | null>(null);

  React.useEffect(() => {
    if (!open) return;
    setEnabled(state.enabled);
    setMode(state.mode ?? 'agent');
    setHost(state.host ?? '');
    setPort(String(state.port ?? 9100));
    setPrinterName(state.printerName);
    setWidth(String(state.width));
    setCodePage(CODE_PAGES.some(c => c.key === `${state.codeTable}-${state.encoding}`) ? `${state.codeTable}-${state.encoding}` : '17-866');
    setFooter(state.footer ?? '');
    setMsg(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const cp = CODE_PAGES.find(c => c.key === codePage) ?? CODE_PAGES[0];
  const input = () => ({ enabled, mode, host: host.trim() || null, port: Number(port) || 9100, printerName: printerName.trim(), width: Number(width) || 48, codeTable: cp.codeTable, encoding: cp.encoding, footer: footer.trim() || null });

  const handleSave = async (thenTest = false) => {
    setMsg(null);
    try {
      await save.mutateAsync(input());
      if (thenTest) {
        await test.mutateAsync();
        setMsg({ ok: true, text: 'Проверочная страница отправлена — она напечатается, если программа печати запущена на кассовом ПК' });
      } else onClose();
    } catch (e) { setMsg({ ok: false, text: apiError(e) }); }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Принтер счетов</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Typography variant="body2" color="text.secondary">
          Официант печатает счёт для гостя перед оплатой на чековом принтере (RONGTA RP33x: USB и LAN).
        </Typography>
        <FormControlLabel control={<Switch checked={enabled} onChange={e => setEnabled(e.target.checked)} />} label="Печатать счета для гостей" />
        <TextField select label="Как подключён принтер" value={mode} onChange={e => setMode(e.target.value as 'agent' | 'network')}>
          <MenuItem value="network">Сетевой кабель в роутер (LAN) — печатает сервер</MenuItem>
          <MenuItem value="agent">USB к кассовому ПК — печатает программа на ПК</MenuItem>
        </TextField>
        {mode === 'network' ? (
          <Box sx={{ display: 'flex', gap: 2 }}>
            <TextField label="IP-адрес принтера" value={host} onChange={e => setHost(e.target.value)} placeholder="192.168.1.87" sx={{ flex: 1 }}
              helperText="Есть на тестовой странице: выключите принтер, зажмите FEED и включите" />
            <TextField label="Порт" value={port} onChange={e => setPort(e.target.value.replace(/\D/g, ''))} sx={{ width: 110 }} />
          </Box>
        ) : (
          <TextField label="Имя принтера в Windows" value={printerName} onChange={e => setPrinterName(e.target.value)}
            helperText="Как в «Параметры → Принтеры и сканеры» на кассовом ПК. В Interpos он назывался «Printer bar»." />
        )}
        <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
          <TextField select label="Ширина ленты" value={width} onChange={e => setWidth(e.target.value)} sx={{ minWidth: 200 }}>
            <MenuItem value="48">80 мм — 48 символов</MenuItem>
            <MenuItem value="42">80 мм — 42 символа</MenuItem>
            <MenuItem value="32">58 мм — 32 символа</MenuItem>
          </TextField>
          <TextField select label="Русские буквы" value={codePage} onChange={e => setCodePage(e.target.value)} sx={{ flex: 1, minWidth: 240 }}
            helperText="Если в проверочной странице кракозябры — выберите другую">
            {CODE_PAGES.map(c => <MenuItem key={c.key} value={c.key}>{c.label}</MenuItem>)}
          </TextField>
        </Box>
        <TextField label="Текст внизу счёта" value={footer} onChange={e => setFooter(e.target.value)} inputProps={{ maxLength: 200 }} />

        {mode === 'network' && (
          <Alert severity="info">
            Сервер должен быть в той же сети, что и принтер (сейчас — ваш Мак в сети кафе). Когда сервер переедет в интернет,
            переключитесь на «USB к кассовому ПК» или оставьте принтер в сети и запустите программу печати на кассовом ПК.
          </Alert>
        )}
        {mode === 'agent' && <Box sx={{ bgcolor: 'action.hover', borderRadius: 2, p: 2 }}>
          <Typography sx={{ fontWeight: 700, mb: 0.5 }}>Программа печати на кассовом ПК</Typography>
          <Typography variant="body2" component="ol" sx={{ pl: 2.5, m: 0 }}>
            <li>Скачайте её здесь и перенесите на кассовый ПК (флешкой или почтой), например в папку C:\Cafe.</li>
            <li>Правой кнопкой по файлу → «Выполнить с помощью PowerShell». Откроется окно — его можно свернуть.</li>
            <li>Программа сама добавится в автозагрузку и будет запускаться вместе с Windows.</li>
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            Скачивайте с того адреса сайта, по которому кассовый ПК видит сервер: программа запомнит его.
          </Typography>
          <Box sx={{ display: 'flex', gap: 1, mt: 1.5, flexWrap: 'wrap' }}>
            <Button variant="outlined" startIcon={<DownloadIcon />} onClick={() => downloadPrintAgent().catch(e => setMsg({ ok: false, text: apiError(e) }))}>
              Скачать программу
            </Button>
            <Button color="inherit" onClick={async () => {
              if (!window.confirm('Старая программа перестанет печатать — её нужно будет скачать заново. Продолжить?')) return;
              try { await newToken.mutateAsync(); setMsg({ ok: true, text: 'Ключ сменён — скачайте программу заново' }); } catch (e) { setMsg({ ok: false, text: apiError(e) }); }
            }}>Сменить ключ</Button>
          </Box>
        </Box>}
        {msg && <Alert severity={msg.ok ? 'success' : 'error'}>{msg.text}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2, flexWrap: 'wrap', gap: 1 }}>
        <Button startIcon={<PrintIcon />} onClick={() => handleSave(true)} disabled={!enabled || save.isPending || test.isPending}>Проверить печать</Button>
        <Box sx={{ flex: 1 }} />
        <Button onClick={onClose}>Отмена</Button>
        <Button variant="contained" onClick={() => handleSave(false)} disabled={save.isPending || (mode === 'agent' ? !printerName.trim() : !host.trim())}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

export default PrinterSettingsDialog;
