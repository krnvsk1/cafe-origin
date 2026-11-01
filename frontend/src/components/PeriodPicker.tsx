import * as React from 'react';
import { Box, TextField, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { todayMsk } from '../api/haccp';

/** Период в днях по Москве, обе даты включительно (yyyy-MM-dd) */
export interface Period {
  from: string;
  to: string;
}

const addDays = (d: string, n: number) => {
  const x = new Date(d + 'T12:00:00Z');
  x.setUTCDate(x.getUTCDate() + n);
  return x.toISOString().slice(0, 10);
};

const PRESETS = [
  { key: 'today', label: 'Сегодня', days: 1 },
  { key: '7', label: '7 дней', days: 7 },
  { key: '30', label: '30 дней', days: 30 },
  { key: '90', label: '3 месяца', days: 90 },
] as const;

/** Последние n дней, считая сегодня */
export const lastDays = (days: number): Period => {
  const to = todayMsk();
  return { from: addDays(to, -(days - 1)), to };
};

const presetOf = (p: Period) => {
  if (p.to !== todayMsk()) return null;
  return PRESETS.find(x => addDays(p.to, -(x.days - 1)) === p.from)?.key ?? null;
};

/** Быстрый выбор («Сегодня», «7 дней»…) и свои даты «с — по» */
const PeriodPicker: React.FC<{ value: Period; onChange: (p: Period) => void }> = ({ value, onChange }) => {
  const preset = presetOf(value);
  return (
    <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
      <ToggleButtonGroup
        size="small"
        exclusive
        value={preset}
        onChange={(_, key: string | null) => {
          const p = PRESETS.find(x => x.key === key);
          if (p) onChange(lastDays(p.days));
        }}
      >
        {PRESETS.map(p => <ToggleButton key={p.key} value={p.key} sx={{ px: 1.5 }}>{p.label}</ToggleButton>)}
      </ToggleButtonGroup>
      <Box sx={{ display: 'flex', gap: 1 }}>
        <TextField
          type="date" size="small" label="С" value={value.from}
          onChange={e => e.target.value && onChange({ from: e.target.value, to: e.target.value > value.to ? e.target.value : value.to })}
          slotProps={{ inputLabel: { shrink: true } }} sx={{ width: 160 }}
        />
        <TextField
          type="date" size="small" label="По" value={value.to}
          onChange={e => e.target.value && onChange({ from: e.target.value < value.from ? e.target.value : value.from, to: e.target.value })}
          slotProps={{ inputLabel: { shrink: true } }} sx={{ width: 160 }}
        />
      </Box>
    </Box>
  );
};

export default PeriodPicker;
