import * as React from 'react';
import { Box, Typography, Button, TextField, Chip, Alert, CircularProgress, IconButton } from '@mui/material';
import { Add as AddIcon, Close as CloseIcon } from '@mui/icons-material';
import { saveHaccpSchedule, useInvalidateHaccp, type HaccpMeta } from '../../api/haccp';
import { apiError } from '../../api/inventory';
import { Panel } from '../inventory/ui';

/**
 * График проверок: к какому времени должна быть каждая запись.
 * Пока время не наступило — напоминания нет; наступило, а записи нет — красная полоса у всех, кто ведёт журналы.
 */
const ScheduleView: React.FC<{ meta: HaccpMeta }> = ({ meta }) => {
  const invalidate = useInvalidateHaccp();
  const journals = meta.journals.filter(j => j.frequency !== 'per_receipt');
  const [schedule, setSchedule] = React.useState<Record<string, string[]>>(meta.schedule);
  const [adding, setAdding] = React.useState<Record<string, string>>({});
  const [busy, setBusy] = React.useState(false);
  const [message, setMessage] = React.useState<{ ok: boolean; text: string } | null>(null);

  React.useEffect(() => { setSchedule(meta.schedule); }, [meta.schedule]);

  const dirty = JSON.stringify(schedule) !== JSON.stringify(meta.schedule);

  const add = (key: string) => {
    const t = adding[key];
    if (!t) return;
    setSchedule(s => ({ ...s, [key]: Array.from(new Set([...(s[key] ?? []), t])).sort() }));
    setAdding(a => ({ ...a, [key]: '' }));
  };
  const remove = (key: string, t: string) => setSchedule(s => ({ ...s, [key]: (s[key] ?? []).filter(x => x !== t) }));

  const save = async () => {
    setBusy(true);
    setMessage(null);
    try {
      await saveHaccpSchedule(schedule);
      await invalidate();
      setMessage({ ok: true, text: 'График сохранён' });
    } catch (e) {
      setMessage({ ok: false, text: apiError(e) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Typography color="text.secondary">
        К какому времени (по Москве) должна быть сделана каждая запись. Для холодильников и контрольных точек каждое время — одна проверка каждого из них.
        Пока время не наступило, напоминания нет; наступило, а записи нет — у всех, кто ведёт журналы, появляется красная полоса.
        Журнал без времени живёт без напоминаний.
      </Typography>
      <Panel padding={false}>
        {journals.map((j, i) => (
          <Box key={j.key} sx={{ px: 2, py: 1.5, borderTop: i ? '1px solid' : 0, borderColor: 'divider',
            display: 'flex', gap: 1.5, alignItems: { xs: 'flex-start', sm: 'center' }, flexDirection: { xs: 'column', sm: 'row' } }}>
            <Typography sx={{ fontWeight: 600, width: { sm: 260 }, flexShrink: 0 }}>{j.short}</Typography>
            <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap', alignItems: 'center', flex: 1 }}>
              {(schedule[j.key] ?? []).map(t => (
                <Chip key={t} label={t} onDelete={() => remove(j.key, t)} deleteIcon={<CloseIcon />} sx={{ fontWeight: 700 }} />
              ))}
              {(schedule[j.key] ?? []).length === 0 && <Typography variant="body2" color="text.secondary">без напоминаний</Typography>}
              <TextField
                size="small"
                type="time"
                value={adding[j.key] ?? ''}
                onChange={e => setAdding(a => ({ ...a, [j.key]: e.target.value }))}
                onKeyDown={e => { if (e.key === 'Enter') add(j.key); }}
                sx={{ width: 120 }}
                slotProps={{ htmlInput: { 'aria-label': `Добавить время: ${j.short}` } }}
              />
              <IconButton aria-label="Добавить время" onClick={() => add(j.key)} disabled={!adding[j.key]}><AddIcon /></IconButton>
            </Box>
          </Box>
        ))}
      </Panel>
      {message && <Alert severity={message.ok ? 'success' : 'error'} onClose={() => setMessage(null)}>{message.text}</Alert>}
      <Box>
        <Button variant="contained" color="secondary" size="large" onClick={save} disabled={busy || !dirty}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Сохранить график
        </Button>
      </Box>
    </Box>
  );
};

export default ScheduleView;
