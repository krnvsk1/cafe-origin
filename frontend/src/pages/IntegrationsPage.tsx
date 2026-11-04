import * as React from 'react';
import { Box, Typography, Button, CircularProgress, TextField, Alert } from '@mui/material';
import { useRestaurantSettings, useSaveBrand } from '../api/hooks';
import { apiError } from '../api/inventory';
import { useLocation } from 'react-router-dom';
import {
  ReceiptLongOutlined as ReceiptIcon, CheckCircleOutline as OkIcon, ErrorOutline as ErrIcon, RemoveCircleOutline as OffIcon, SettingsOutlined as SettingsIcon,
} from '@mui/icons-material';
import { useEgaisState } from '../api/egais';
import { useChzState } from '../api/chz';
import { useEdoState } from '../api/edo';
import { useFiscalState, TAXATION_TYPES, VAT_TYPES, SHIFT_TEXT } from '../api/fiscal';
import { useMercuryState } from '../api/mercury';
import { fmtDateTime } from '../api/inventory';
import { tokens } from '../theme';
import { Panel } from './inventory/ui';
import EgaisSettingsDialog from './inventory/EgaisSettingsDialog';
import ChzSettingsDialog from './inventory/ChzSettingsDialog';
import EdoSettingsDialog from './inventory/EdoSettingsDialog';
import FiscalSettingsDialog from './inventory/FiscalSettingsDialog';
import ReceiptLayoutDialog from './inventory/ReceiptLayoutDialog';
import MercurySettingsDialog from './inventory/MercurySettingsDialog';
import PrinterSettingsDialog from './inventory/PrinterSettingsDialog';
import { usePrintingState, agentOnline } from '../api/printing';
import { TitleWithHint } from '../components/Hint';

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
  extra?: React.ReactNode;
}> = ({ id, title, purpose, status, statusText, details, error, loading, onSetup, extra }) => (
  <Panel sx={{ scrollMarginTop: 96 }}>
    <Box id={id} sx={{ display: 'flex', gap: 2, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      {loading ? <CircularProgress size={28} sx={{ m: 0.25 }} /> : <StatusIcon status={status} />}
      <Box sx={{ flex: '1 1 260px', minWidth: 0 }}>
        <Typography sx={{ fontWeight: 800, fontSize: '1.15rem' }}><TitleWithHint hint={purpose}>{title}</TitleWithHint></Typography>
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
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
        <Button variant={status === 'off' ? 'contained' : 'outlined'} color="secondary" startIcon={<SettingsIcon />} onClick={onSetup}>
          {status === 'off' ? 'Подключить' : 'Настроить'}
        </Button>
        {extra}
      </Box>
    </Box>
  </Panel>
);

/** Название заведения: на сайте, во вкладке браузера, в описании онлайн-оплаты и в Telegram-боте */
const BrandCard: React.FC = () => {
  const { data } = useRestaurantSettings();
  const save = useSaveBrand();
  const [name, setName] = React.useState('');
  const [tagline, setTagline] = React.useState('');
  const [message, setMessage] = React.useState<{ ok: boolean; text: string } | null>(null);

  React.useEffect(() => {
    if (data) { setName(data.brandName ?? ''); setTagline(data.brandTagline ?? ''); }
  }, [data?.brandName, data?.brandTagline]); // eslint-disable-line react-hooks/exhaustive-deps

  const submit = async () => {
    setMessage(null);
    try {
      await save.mutateAsync({ name: name.trim(), tagline: tagline.trim() || null });
      setMessage({ ok: true, text: 'Сохранено — название обновится на сайте' });
    } catch (e) { setMessage({ ok: false, text: apiError(e) }); }
  };

  return (
    <Panel sx={{ scrollMarginTop: 96 }}>
      <Box id="brand" sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        <Box>
          <Typography sx={{ fontWeight: 800, fontSize: '1.15rem' }}>
            <TitleWithHint hint="На сайте и во вкладке браузера, в описании онлайн-оплаты и в Telegram-боте">Название заведения</TitleWithHint>
          </Typography>
        </Box>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 2fr auto' }, gap: 1.5, alignItems: 'start' }}>
          <TextField size="small" label="Название" value={name} onChange={e => setName(e.target.value)} slotProps={{ htmlInput: { maxLength: 60 } }} />
          <TextField size="small" label="Подзаголовок на главной" value={tagline} onChange={e => setTagline(e.target.value)}
            placeholder="Привезём горячим или соберём к вашему приходу" slotProps={{ htmlInput: { maxLength: 200 } }} />
          <Button variant="contained" color="secondary" onClick={submit} disabled={!name.trim() || save.isPending}>Сохранить</Button>
        </Box>
        {message && <Alert severity={message.ok ? 'success' : 'error'} onClose={() => setMessage(null)}>{message.text}</Alert>}
      </Box>
    </Panel>
  );
};

/** Настройка интеграций: ЕГАИС, Честный знак, СБИС — подключение и статус в одном месте */
const IntegrationsPage: React.FC = () => {
  const location = useLocation();
  const { data: egais, isLoading: egaisLoading } = useEgaisState();
  const { data: chz, isLoading: chzLoading } = useChzState();
  const { data: edo, isLoading: edoLoading } = useEdoState();
  const { data: fiscal, isLoading: fiscalLoading } = useFiscalState(true);
  const { data: mercury, isLoading: mercuryLoading } = useMercuryState();
  const { data: printing, isLoading: printingLoading } = usePrintingState();
  const [open, setOpen] = React.useState<'egais' | 'chz' | 'sbis' | 'mercury' | 'kkt' | 'layout' | 'printer' | null>(null);

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
  const fiscalStatus: Status = !fiscal?.enabled ? 'off' : fiscal.shiftError || fiscal.shiftState === 'expired' ? 'error' : 'ok';
  const mercuryConfigured = !!mercury?.enabled && !!mercury.login && mercury.hasPassword && mercury.hasApiKey;
  const mercuryStatus: Status = !mercuryConfigured ? 'off' : mercury!.lastError ? 'error' : 'ok';
  const printerOnline = agentOnline(printing);
  const lastPrint = printing?.recent[0];
  const networkPrinter = printing?.mode === 'network';
  const needsAgent = !!printing && ((printing.enabled && !networkPrinter) || printing.relayDevices);
  const printerStatus: Status = !printing?.enabled && !printing?.relayDevices ? 'off'
    : (!needsAgent || printerOnline) && lastPrint?.status !== 'error' ? 'ok' : 'error';

  return (
    <Box sx={{ py: { xs: 2, sm: 3 }, maxWidth: 1000, mx: 'auto', display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box>
        <Typography variant="h4" component="h1" sx={{ fontSize: { xs: '1.6rem', sm: '2rem' } }}>
          <TitleWithHint hint="Подключение к онлайн-кассе, государственным системам (ЕГАИС, Честный знак, Меркурий) и ЭДО.">Настройка интеграций</TitleWithHint>
        </Typography>
      </Box>

      <BrandCard />

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

      <Card
        id="kkt"
        title="Онлайн-касса АТОЛ"
        purpose="Чек после оплаты на кассе: печать, запись в фискальный накопитель и отправка в ОФД"
        loading={fiscalLoading}
        status={fiscalStatus}
        statusText={fiscalStatus === 'off' ? 'Не подключено' : fiscal?.shiftError ? 'Касса недоступна' : `Касса на связи · ${SHIFT_TEXT[fiscal?.shiftState ?? ''] ?? fiscal?.shiftState ?? ''}`}
        details={fiscal?.enabled ? [
          `АТОЛ Web Server: ${fiscal.url}`,
          `${TAXATION_TYPES.find(t => t.value === fiscal.taxationType)?.label ?? fiscal.taxationType} · ${VAT_TYPES.find(t => t.value === fiscal.vat)?.label ?? fiscal.vat}`,
          `Электронный чек гостю: ${fiscal.sendToCustomer ? 'на телефон, если указан' : 'не отправляется'}`,
          fiscal.staffChecks && `Закрытие на сотрудника (без чека): включено${fiscal.staffChecksManagersOnly ? ', только менеджер' : ''}`,
          fiscal.failedReceipts > 0 && `Не пробито чеков за 2 дня: ${fiscal.failedReceipts} — повторите в «Кассе» → «Заказы смены»`,
        ] : undefined}
        error={fiscal?.enabled ? fiscal.shiftError : null}
        onSetup={() => setOpen('kkt')}
        extra={fiscal && (
          <Button variant="outlined" color="secondary" startIcon={<ReceiptIcon />} onClick={() => setOpen('layout')}>Вид чека</Button>
        )}
      />

      <Card
        id="printer"
        title="Кассовый ПК: принтер счетов и связь"
        purpose="Счёт для гостя на RONGTA перед оплатой; программа на кассовом ПК связывает сервер в интернете с принтером, кассой АТОЛ и УТМ"
        loading={printingLoading}
        status={printerStatus}
        statusText={printerStatus === 'off' ? 'Не подключено'
          : !needsAgent ? `Принтер по сети · ${printing?.host}:${printing?.port}`
          : printerOnline ? `Программа печати на связи${printing?.agentHost ? ` · ${printing.agentHost}` : ''}` : 'Программа печати на кассовом ПК не запущена'}
        details={printing?.enabled || printing?.relayDevices ? [
          printing.relayDevices && `Касса АТОЛ и УТМ ЕГАИС — через программу на кассовом ПК${printing.atolViaDriver ? ' (касса — через драйвер ККТ 10)' : ''}`,
          printing.enabled && (printing.mode === 'agent' ? `Принтер: ${printing.printerName} · ${printing.width} символов в строке` : `Принтер в сети: ${printing.host}:${printing.port} · ${printing.width} символов в строке`),
          lastPrint && `Последняя печать: ${lastPrint.title} — ${lastPrint.status === 'done' ? 'напечатано' : lastPrint.status === 'error' ? 'ошибка' : 'в очереди'} (${fmtDateTime(lastPrint.createdAt)})`,
        ] : undefined}
        error={printing?.enabled && lastPrint?.status === 'error' ? lastPrint.error : null}
        onSetup={() => setOpen('printer')}
      />

      <EgaisSettingsDialog open={open === 'egais'} onClose={() => setOpen(null)} />
      <ChzSettingsDialog open={open === 'chz'} onClose={() => setOpen(null)} />
      {fiscal && <ReceiptLayoutDialog open={open === 'layout'} state={fiscal} onClose={() => setOpen(null)} />}
      {fiscal && <FiscalSettingsDialog open={open === 'kkt'} state={fiscal} onClose={() => setOpen(null)} />}
      {edo && <EdoSettingsDialog open={open === 'sbis'} state={edo} onClose={() => setOpen(null)} />}
      {mercury && <MercurySettingsDialog open={open === 'mercury'} state={mercury} onClose={() => setOpen(null)} />}
      {printing && <PrinterSettingsDialog open={open === 'printer'} state={printing} onClose={() => setOpen(null)} />}
    </Box>
  );
};

export default IntegrationsPage;
