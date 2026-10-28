import * as React from 'react';
import { Box, Typography, Button, CircularProgress } from '@mui/material';
import { useLocation } from 'react-router-dom';
import {
  CheckCircleOutline as OkIcon, ErrorOutline as ErrIcon, RemoveCircleOutline as OffIcon, SettingsOutlined as SettingsIcon,
} from '@mui/icons-material';
import { useEgaisState } from '../api/egais';
import { useChzState } from '../api/chz';
import { useEdoState } from '../api/edo';
import { useMercuryState } from '../api/mercury';
import { fmtDateTime } from '../api/inventory';
import { tokens } from '../theme';
import { Panel } from './inventory/ui';
import EgaisSettingsDialog from './inventory/EgaisSettingsDialog';
import ChzSettingsDialog from './inventory/ChzSettingsDialog';
import EdoSettingsDialog from './inventory/EdoSettingsDialog';
import MercurySettingsDialog from './inventory/MercurySettingsDialog';

type Status = 'ok' | 'error' | 'off';

const StatusIcon: React.FC<{ status: Status }> = ({ status }) =>
  status === 'ok' ? <OkIcon sx={{ color: tokens.herb, fontSize: 32 }} />
    : status === 'error' ? <ErrIcon sx={{ color: tokens.chili, fontSize: 32 }} />
      : <OffIcon sx={{ color: 'text.disabled', fontSize: 32 }} />;

const Card: React.FC<{
  id: string;
  title: string;
  purpose: string;
  status: Status;
  statusText: string;
  details?: (string | null | undefined | false)[];
  error?: string | null;
  loading?: boolean;
  onSetup: () => void;
}> = ({ id, title, purpose, status, statusText, details, error, loading, onSetup }) => (
  <Panel sx={{ scrollMarginTop: 96 }}>
    <Box id={id} sx={{ display: 'flex', gap: 2, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      {loading ? <CircularProgress size={28} sx={{ m: 0.25 }} /> : <StatusIcon status={status} />}
      <Box sx={{ flex: '1 1 260px', minWidth: 0 }}>
        <Typography sx={{ fontWeight: 800, fontSize: '1.15rem' }}>{title}</Typography>
        <Typography variant="body2" color="text.secondary">{purpose}</Typography>
        {!loading && (
          <>
            <Typography sx={{ fontWeight: 700, mt: 1, color: status === 'ok' ? tokens.herb : status === 'error' ? tokens.chili : 'text.secondary' }}>
              {statusText}
            </Typography>
            {details?.filter(Boolean).map(d => <Typography key={String(d)} variant="body2">{d}</Typography>)}
            {error && <Typography variant="body2" sx={{ color: tokens.chili, mt: 0.5 }}>{error}</Typography>}
          </>
        )}
      </Box>
      <Button variant={status === 'off' ? 'contained' : 'outlined'} color="secondary" startIcon={<SettingsIcon />} onClick={onSetup}>
        {status === 'off' ? 'Подключить' : 'Настроить'}
      </Button>
    </Box>
  </Panel>
);

/** Настройка интеграций: ЕГАИС, Честный знак, СБИС — подключение и статус в одном месте */
const IntegrationsPage: React.FC = () => {
  const location = useLocation();
  const { data: egais, isLoading: egaisLoading } = useEgaisState();
  const { data: chz, isLoading: chzLoading } = useChzState();
  const { data: edo, isLoading: edoLoading } = useEdoState();
  const { data: mercury, isLoading: mercuryLoading } = useMercuryState();
  const [open, setOpen] = React.useState<'egais' | 'chz' | 'sbis' | 'mercury' | null>(null);

  // Переход из вкладки склада: /admin/integrations#chz — прокручиваем к нужной карточке
  React.useEffect(() => {
    const id = location.hash.replace('#', '');
    if (!id) return;
    const t = setTimeout(() => document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 200);
    return () => clearTimeout(t);
  }, [location.hash]);

  const egaisStatus: Status = !egais?.enabled ? 'off' : egais.reachable && !egais.lastSyncError ? 'ok' : 'error';
  const chzConfigured = !!chz?.enabled && !!chz.inn;
  const chzStatus: Status = !chzConfigured ? 'off' : chz!.fiasId ? 'ok' : 'error';
  const edoConfigured = !!edo?.enabled && !!edo.login && edo.hasPassword;
  const edoStatus: Status = !edoConfigured ? 'off' : edo!.lastError ? 'error' : 'ok';
  const mercuryConfigured = !!mercury?.enabled && !!mercury.login && mercury.hasPassword && mercury.hasApiKey;
  const mercuryStatus: Status = !mercuryConfigured ? 'off' : mercury!.lastError ? 'error' : 'ok';

  return (
    <Box sx={{ py: { xs: 2, sm: 3 }, maxWidth: 1000, mx: 'auto', display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box>
        <Typography variant="h4" component="h1" sx={{ fontSize: { xs: '1.6rem', sm: '2rem' } }}>Настройка интеграций</Typography>
        <Typography color="text.secondary">Подключение к государственным системам (ЕГАИС, Честный знак, Меркурий) и ЭДО. Работа с документами — во вкладках склада.</Typography>
      </Box>

      <Card
        id="egais"
        title="ЕГАИС"
        purpose="Приёмка пива по ТТН через УТМ: накладные, акты приёма, расхождения"
        loading={egaisLoading}
        status={egaisStatus}
        statusText={egaisStatus === 'off' ? 'Не подключено' : egais?.reachable ? 'Подключено · УТМ на связи' : 'УТМ недоступен'}
        details={egais?.enabled ? [
          `УТМ: ${egais.utmUrl}`,
          `ФСРАР ID: ${egais.fsrarId ?? 'не указан'}`,
          `Автопроверка входящих: ${egais.autoSyncMinutes ? `каждые ${egais.autoSyncMinutes} мин` : 'выключена'}`,
          `Последняя проверка: ${egais.lastSyncAt ? fmtDateTime(egais.lastSyncAt) : 'ещё не было'}`,
        ] : undefined}
        error={egais?.enabled ? egais.lastSyncError : null}
        onSetup={() => setOpen('egais')}
      />

      <Card
        id="chz"
        title="Честный знак"
        purpose="Вывод из оборота маркированного товара (молочка, вода, пиво…) с подписью КЭП"
        loading={chzLoading}
        status={chzStatus}
        statusText={chzStatus === 'off' ? 'Не подключено' : chzStatus === 'ok' ? 'Подключено' : 'Подключено, но не указано место деятельности'}
        details={chzConfigured ? [
          `ИНН: ${chz!.inn}${chz!.kpp ? ` · КПП ${chz!.kpp}` : ''}`,
          `Место деятельности (ФИАС): ${chz!.fiasId ?? 'не указано — Честный знак отклонит вывод пива и воды'}`,
          `Товарные группы: ${chz!.productGroups.map(g => chz!.groups.find(x => x.key === g)?.title ?? g).join(', ')}`,
          chz!.sandbox && 'Тестовый контур (песочница)',
          `Вход по КЭП: ${chz!.signedIn ? 'выполнен' : 'не выполнен — каждый сотрудник входит своим сертификатом во вкладке склада'}`,
        ] : undefined}
        onSetup={() => setOpen('chz')}
      />

      <Card
        id="sbis"
        title="СБИС (Saby)"
        purpose="Входящие электронные накладные (УПД) от поставщиков — приход на склад и платежи"
        loading={edoLoading}
        status={edoStatus}
        statusText={edoStatus === 'off' ? 'Не подключено' : edoStatus === 'ok' ? 'Подключено' : 'Ошибка загрузки'}
        details={edoConfigured ? [
          `Пользователь: ${edo!.login}${edo!.account ? ` · аккаунт ${edo!.account}` : ''}`,
          `Автозагрузка: ${edo!.autoSyncMinutes ? `каждые ${edo!.autoSyncMinutes} мин` : 'только вручную'}`,
          `Последняя загрузка: ${edo!.lastSyncAt ? fmtDateTime(edo!.lastSyncAt) : 'ещё не было'}`,
        ] : undefined}
        error={edoConfigured ? edo!.lastError : null}
        onSetup={() => setOpen('sbis')}
      />

      <Card
        id="mercury"
        title="Меркурий (ВетИС)"
        purpose="Гашение входящих ветеринарных документов (ВСД) на мясо, курицу, молочку"
        loading={mercuryLoading}
        status={mercuryStatus}
        statusText={mercuryStatus === 'off' ? 'Не подключено' : mercuryStatus === 'ok' ? 'Подключено' : 'Ошибка загрузки'}
        details={mercuryConfigured ? [
          `Пользователь ВетИС: ${mercury!.initiatorLogin} · площадка ${mercury!.enterpriseGuid}`,
          mercury!.sandbox && 'Тестовый контур',
          `Автозагрузка: ${mercury!.autoSyncMinutes ? `каждые ${mercury!.autoSyncMinutes} мин` : 'только вручную'}`,
          `Последняя загрузка: ${mercury!.lastSyncAt ? fmtDateTime(mercury!.lastSyncAt) : 'ещё не было'}`,
        ] : undefined}
        error={mercuryConfigured ? mercury!.lastError : null}
        onSetup={() => setOpen('mercury')}
      />

      <EgaisSettingsDialog open={open === 'egais'} onClose={() => setOpen(null)} />
      <ChzSettingsDialog open={open === 'chz'} onClose={() => setOpen(null)} />
      {edo && <EdoSettingsDialog open={open === 'sbis'} state={edo} onClose={() => setOpen(null)} />}
      {mercury && <MercurySettingsDialog open={open === 'mercury'} state={mercury} onClose={() => setOpen(null)} />}
    </Box>
  );
};

export default IntegrationsPage;
