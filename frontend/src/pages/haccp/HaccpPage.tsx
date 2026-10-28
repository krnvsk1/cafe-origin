import * as React from 'react';
import { Box, Typography, Tabs, Tab, CircularProgress, Alert, Chip, TextField, MenuItem, useMediaQuery, useTheme } from '@mui/material';
import { useNavigate, useParams } from 'react-router-dom';
import { useHaccpMeta, useHaccpToday, isOverdue } from '../../api/haccp';
import { apiError } from '../../api/inventory';
import TodayView from './TodayView';
import JournalView from './JournalView';
import SettingsView from './SettingsView';
import ScheduleView from './ScheduleView';

/**
 * Журналы ХАССП и СанПиН 2.3/2.4.4282-26: «Сегодня» (что заполнить), журналы с историей и печатью, настройка оборудования и сотрудников.
 * Адреса: /haccp, /haccp/journal/:key, /haccp/settings
 */
const HaccpPage: React.FC = () => {
  const theme = useTheme();
  const mobile = useMediaQuery(theme.breakpoints.down('md'));
  const navigate = useNavigate();
  const { section, key } = useParams();
  const { data: meta, isLoading, error } = useHaccpMeta();
  const { data: today } = useHaccpToday();

  if (isLoading) return <Box sx={{ display: 'grid', placeItems: 'center', py: 10 }}><CircularProgress /></Box>;
  if (error || !meta) return <Alert severity="error" sx={{ my: 3 }}>{apiError(error, 'Не удалось загрузить журналы')}</Alert>;

  const tab = section === 'journal' ? 'journal' : (section === 'settings' || section === 'schedule') && meta.canManage ? section : 'today';
  const journal = meta.journals.find(j => j.key === key) ?? meta.journals[0];
  const todo = today?.tasks.filter(isOverdue).length ?? 0;
  const openJournal = (k: string) => navigate(`/haccp/journal/${k}`);

  return (
    <Box sx={{ py: { xs: 2, sm: 3 }, maxWidth: 1240, mx: 'auto', display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box>
        <Typography variant="h4" component="h1" sx={{ fontSize: { xs: '1.6rem', sm: '2rem' } }}>Журналы ХАССП</Typography>
        <Typography color="text.secondary">Производственный контроль по СанПиН 2.3/2.4.4282-26 — электронные журналы с историей правок</Typography>
      </Box>

      <Tabs
        value={tab}
        onChange={(_, v) => navigate(v === 'today' ? '/haccp' : v === 'journal' ? `/haccp/journal/${journal.key}` : `/haccp/${v}`)}
        variant="scrollable"
        allowScrollButtonsMobile
      >
        <Tab value="today" label={<Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>Сегодня{todo > 0 && <Chip size="small" color="error" label={todo} />}</Box>} />
        <Tab value="journal" label="Журналы" />
        {meta.canManage && <Tab value="schedule" label="График" />}
        {meta.canManage && <Tab value="settings" label="Настройка" />}
      </Tabs>

      {tab === 'today' && <TodayView meta={meta} onOpenJournal={openJournal} onOpenSettings={() => navigate('/haccp/settings')} />}

      {tab === 'journal' && (
        <>
          {mobile ? (
            <TextField select label="Журнал" value={journal.key} onChange={e => openJournal(e.target.value)}>
              {meta.journals.map(j => <MenuItem key={j.key} value={j.key}>{j.short}</MenuItem>)}
            </TextField>
          ) : (
            <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
              {meta.journals.map(j => (
                <Chip key={j.key} label={j.short} color={j.key === journal.key ? 'primary' : 'default'} onClick={() => openJournal(j.key)} />
              ))}
            </Box>
          )}
          <JournalView key={journal.key} meta={meta} journal={journal} />
        </>
      )}

      {tab === 'schedule' && <ScheduleView meta={meta} />}
      {tab === 'settings' && <SettingsView meta={meta} />}
    </Box>
  );
};

export default HaccpPage;
