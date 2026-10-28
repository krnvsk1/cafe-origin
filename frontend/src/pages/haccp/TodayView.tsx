import * as React from 'react';
import { Box, Typography, Button, CircularProgress, Chip, LinearProgress } from '@mui/material';
import {
  CheckCircle as DoneIcon, RadioButtonUnchecked as TodoIcon, Timelapse as PartialIcon, LocalShippingOutlined as ReceiptIcon,
  BadgeOutlined as BadgeIcon, WarningAmberRounded as WarnIcon, ScheduleOutlined as UpcomingIcon, DoneAll as AllIcon,
} from '@mui/icons-material';
import { useHaccpToday, useHaccpEntries, fmtMsk, todayMsk, isOverdue, type HaccpMeta, type TodayTask } from '../../api/haccp';
import { tokens } from '../../theme';
import { Panel } from '../inventory/ui';
import EntryDialog from './EntryDialog';
import { HealthBatchDialog, PerishableReceiptDialog, MultiObjectDialog } from './BatchDialogs';

const StatusIcon: React.FC<{ status: TodayTask['status'] }> = ({ status }) =>
  status === 'done' || status === 'ok' ? <DoneIcon sx={{ color: tokens.herb }} />
    : status === 'upcoming' ? <UpcomingIcon sx={{ color: 'text.disabled' }} />
      : status === 'partial' ? <PartialIcon sx={{ color: '#C77700' }} />
        : <TodoIcon sx={{ color: tokens.chili }} />;

const statusText = (t: TodayTask) => {
  const next = t.next ? `следующая в ${t.next}` : '';
  if (t.status === 'done') return t.expected > 1 ? `все ${t.expected} проверки сделаны` : 'заполнено';
  if (t.status === 'upcoming') return `к ${t.next}`;
  if (t.status === 'ok') return [`${Math.min(t.done, t.expected)} из ${t.expected}`, next].filter(Boolean).join(' · ');
  return `пора заполнить${t.due > 1 ? ` (${Math.min(t.done, t.due)} из ${t.due})` : ''}${next ? ` · ${next}` : ''}`;
};

/** Что заполнить сегодня: проверки по оборудованию, осмотр смены, бракераж, приходы без бракеража, сроки медкнижек */
const TodayView: React.FC<{ meta: HaccpMeta; onOpenJournal: (key: string) => void; onOpenSettings: () => void }> = ({ meta, onOpenJournal, onOpenSettings }) => {
  const { data, isLoading } = useHaccpToday();
  const today = todayMsk();
  const { data: healthToday = [] } = useHaccpEntries('health', today, today, null, false);
  const [entry, setEntry] = React.useState<{ journal: string; objectId: number | null } | null>(null);
  const [healthOpen, setHealthOpen] = React.useState(false);
  const [receipt, setReceipt] = React.useState<number | null | undefined>(undefined);
  const [multi, setMulti] = React.useState<string | null>(null);

  if (isLoading || !data) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;

  const journal = (key: string) => meta.journals.find(j => j.key === key)!;
  const total = data.tasks.reduce((s, t) => s + t.expected, 0);
  const done = data.tasks.reduce((s, t) => s + Math.min(t.done, t.expected), 0);
  const overdue = data.tasks.filter(isOverdue).length;
  // Журналы по оборудованию, где пора внести сразу несколько замеров
  const multiJournals = Array.from(new Set(data.tasks.filter(t => t.objectId && isOverdue(t)).map(t => t.journal)))
    .filter(k => data.tasks.filter(t => t.journal === k && t.objectId && isOverdue(t)).length > 1 && k !== 'fryer');
  const empty = meta.objects.length === 0 && meta.staff.length === 0;

  const fill = (t: TodayTask) => {
    if (t.journal === 'health') setHealthOpen(true);
    else setEntry({ journal: t.journal, objectId: t.objectId });
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {empty && (
        <Panel sx={{ borderLeft: `4px solid ${tokens.saffron}` }}>
          <Typography sx={{ fontWeight: 700 }}>С чего начать</Typography>
          <Typography variant="body2" sx={{ mb: 1.5 }}>
            Добавьте холодильники и склады с нормами температуры, фритюрницы, контрольные точки ХАССП и сотрудников с датами медкнижек — тогда здесь появится список того, что нужно заполнять каждый день.
          </Typography>
          {meta.canManage && <Button variant="contained" color="secondary" onClick={onOpenSettings}>Открыть настройку</Button>}
        </Panel>
      )}

      <Panel>
        <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1, flexWrap: 'wrap', mb: 1 }}>
          <Typography sx={{ fontWeight: 800, fontSize: '1.15rem', flex: 1 }}>Сегодня, {fmtMsk(new Date().toISOString(), false)}</Typography>
          <Typography color={overdue ? tokens.chili : 'text.secondary'} sx={{ fontWeight: overdue ? 700 : 400 }}>
            {overdue ? `пора заполнить: ${overdue}` : `заполнено ${done} из ${total}`}
          </Typography>
          {data.deviations > 0 && <Chip size="small" color="error" icon={<WarnIcon />} label={`отклонений: ${data.deviations}`} />}
        </Box>
        <LinearProgress variant="determinate" value={total ? (done / total) * 100 : 0}
          sx={{ height: 8, borderRadius: 4, bgcolor: 'action.hover', '& .MuiLinearProgress-bar': { bgcolor: tokens.herb } }} />
      </Panel>

      {multiJournals.length > 0 && (
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
          {multiJournals.map(k => (
            <Button key={k} variant="contained" color="secondary" size="large" startIcon={<AllIcon />} onClick={() => setMulti(k)}>
              {journal(k).short}: все разом
            </Button>
          ))}
        </Box>
      )}

      <Panel padding={false}>
        {data.tasks.map((t, i) => (
          <Box key={`${t.journal}-${t.objectId ?? i}`} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, px: 2, py: 1.5, borderTop: i ? '1px solid' : 0, borderColor: 'divider' }}>
            <StatusIcon status={t.status} />
            <Box sx={{ flex: 1, minWidth: 0, cursor: 'pointer' }} onClick={() => onOpenJournal(t.journal)}>
              <Typography sx={{ fontWeight: 600 }}>{t.objectName ?? t.title}</Typography>
              <Typography variant="body2" color="text.secondary">
                {t.objectName ? `${t.title} · ` : ''}{statusText(t)}
                {t.hint ? ` · ${t.hint}` : ''}
              </Typography>
            </Box>
            <Button size="large" variant={isOverdue(t) ? 'contained' : 'text'} color="secondary" onClick={() => fill(t)} sx={{ flexShrink: 0 }}>
              {isOverdue(t) ? 'Заполнить' : t.status === 'done' ? 'Ещё' : 'Внести'}
            </Button>
          </Box>
        ))}
        {data.tasks.length === 0 && <Typography sx={{ p: 2 }} color="text.secondary">Нет проверок по графику — добавьте оборудование и сотрудников, а время задайте во вкладке «График».</Typography>}
      </Panel>

      {data.receipts.length > 0 && (
        <Panel padding={false}>
          <Typography sx={{ fontWeight: 700, px: 2, pt: 1.5 }}>Приходы без бракеража сырья</Typography>
          {data.receipts.map(r => (
            <Box key={r.stockDocumentId} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, px: 2, py: 1.25, borderTop: '1px solid', borderColor: 'divider' }}>
              <ReceiptIcon sx={{ color: 'text.secondary' }} />
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography sx={{ fontWeight: 600 }} noWrap>{r.supplier ?? 'Без поставщика'}</Typography>
                <Typography variant="body2" color="text.secondary">{fmtMsk(r.date, false)}{r.number ? ` · № ${r.number}` : ''} · {r.lines} поз.</Typography>
              </Box>
              <Button variant="outlined" color="secondary" onClick={() => setReceipt(r.stockDocumentId)}>Бракераж</Button>
            </Box>
          ))}
        </Panel>
      )}

      {data.staff.length > 0 && (
        <Panel padding={false}>
          <Typography sx={{ fontWeight: 700, px: 2, pt: 1.5 }}>Медкнижки и аттестация</Typography>
          {data.staff.map((s, i) => (
            <Box key={`${s.staffId}-${i}`} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, px: 2, py: 1.25, borderTop: '1px solid', borderColor: 'divider' }}>
              <BadgeIcon sx={{ color: s.expired ? tokens.chili : '#C77700' }} />
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography sx={{ fontWeight: 600 }}>{s.fullName}</Typography>
                <Typography variant="body2" sx={{ color: s.expired ? tokens.chili : 'text.secondary' }}>
                  {s.what === 'Не указан срок медосмотра' ? s.what : `${s.what} ${s.expired ? 'просрочен(а) с' : 'до'} ${fmtMsk(s.until, false)}`}
                </Typography>
              </Box>
            </Box>
          ))}
        </Panel>
      )}

      {entry && (
        <EntryDialog open meta={meta} journal={journal(entry.journal)} presetObjectId={entry.objectId} onClose={() => setEntry(null)} />
      )}
      <HealthBatchDialog open={healthOpen} meta={meta} alreadyToday={healthToday.filter(e => !e.isAnnulled && e.staffId).map(e => e.staffId!)}
        onClose={() => setHealthOpen(false)} />
      {multi && <MultiObjectDialog open meta={meta} journal={journal(multi)} onClose={() => setMulti(null)} />}
      <PerishableReceiptDialog open={receipt !== undefined} presetReceiptId={receipt} onClose={() => setReceipt(undefined)} />
    </Box>
  );
};

export default TodayView;
