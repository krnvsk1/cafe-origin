import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Box, Typography, Button, IconButton, Tooltip,
  CircularProgress, Alert, useMediaQuery, useTheme,
} from '@mui/material';
import { ContentCopy as CopyIcon, Check as CopiedIcon, Close as CloseIcon } from '@mui/icons-material';
import { useFinancePayment, useMarkPaid } from '../../api/finance';
import { fmtMoney, fmtDate, fmtDateTime, apiError } from '../../api/inventory';
import { tokens } from '../../theme';
import { DueLabel } from './DueLabel';
import { SupplierDelayField } from './SupplierDelayField';

// Сколько показывать «Скопировано»
const COPIED_HINT_MS = 1500;

/** Кнопка копирования значения в буфер обмена */
const CopyButton: React.FC<{ value: string; label: string }> = ({ value, label }) => {
  const [copied, setCopied] = React.useState(false);
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), COPIED_HINT_MS);
    } catch {
      setCopied(false); // буфер обмена недоступен — значение можно выделить вручную
    }
  };
  return (
    <Tooltip title={copied ? 'Скопировано' : `Скопировать ${label}`}>
      <IconButton size="small" onClick={copy} aria-label={`Скопировать ${label}`}>
        {copied ? <CopiedIcon fontSize="small" sx={{ color: tokens.herb }} /> : <CopyIcon fontSize="small" />}
      </IconButton>
    </Tooltip>
  );
};

/** Строка реквизита: подпись, значение (выделяется целиком) и копирование */
const Field: React.FC<{ label: string; value: string | null | undefined; wide?: boolean }> = ({ label, value, wide }) => (
  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, py: 0.5, gridColumn: wide ? '1 / -1' : undefined, minWidth: 0 }}>
    <Box sx={{ flex: 1, minWidth: 0 }}>
      <Typography variant="caption" color="text.secondary">{label}</Typography>
      <Typography sx={{ fontWeight: 600, userSelect: 'all', wordBreak: 'break-word', fontVariantNumeric: 'tabular-nums' }}>{value || '—'}</Typography>
    </Box>
    {value && <CopyButton value={value} label={label.toLowerCase()} />}
  </Box>
);

const Section: React.FC<{ title: string; children: React.ReactNode; action?: React.ReactNode }> = ({ title, children, action }) => (
  <Box sx={{ border: '1px solid', borderColor: 'divider', borderRadius: '16px', px: 2, py: 1.5, mb: 1.5 }}>
    <Box sx={{ display: 'flex', alignItems: 'center', mb: 0.5 }}>
      <Typography sx={{ fontWeight: 700, flex: 1 }}>{title}</Typography>
      {action}
    </Box>
    {children}
  </Box>
);

/** Срок оплаты и отсрочка поставщика (для накладных, где поставщик срок не указал) */
const DueSection: React.FC<{
  inn: string | null;
  name: string | null;
  dueDate: string | null;
  daysLeft: number | null;
  paid: boolean;
  dueFromUpd: boolean;
  updDelayDays: number | null;
  supplierDelayDays: number | null;
}> = ({ inn, name, dueDate, daysLeft, paid, dueFromUpd, updDelayDays, supplierDelayDays }) => {
  const source = dueFromUpd
    ? updDelayDays ? `по УПД: отсрочка ${updDelayDays} дн.` : 'срок указан в УПД'
    : supplierDelayDays !== null ? `по отсрочке поставщика: ${supplierDelayDays} дн.` : null;

  return (
    <Section title="Срок оплаты">
      <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1, flexWrap: 'wrap' }}>
        <Typography sx={{ fontWeight: 800, fontSize: '1.2rem' }}>{dueDate ? fmtDate(dueDate) : 'Не указан'}</Typography>
        <DueLabel dueDate={dueDate} daysLeft={daysLeft} paid={paid} />
      </Box>
      {source && <Typography variant="body2" color="text.secondary">{source}</Typography>}
      {inn && !dueFromUpd && (
        <Box sx={{ mt: 1.25 }}>
          <SupplierDelayField inn={inn} name={name} value={supplierDelayDays} />
        </Box>
      )}
    </Section>
  );
};

/** Карточка платежа: сумма, срок, реквизиты получателя, назначение платежа, отметка об оплате */
const PaymentDialog: React.FC<{ id: number | null; onClose: () => void }> = ({ id, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const { data: p, isLoading, error } = useFinancePayment(id);
  const markPaid = useMarkPaid();
  const [actionError, setActionError] = React.useState('');

  const togglePaid = async () => {
    if (!p) return;
    setActionError('');
    try {
      await markPaid.mutateAsync({ id: p.id, paid: !p.paidAt });
    } catch (e) {
      setActionError(apiError(e));
    }
  };

  return (
    <Dialog open={id !== null} onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen}>
      <DialogTitle sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, pb: 1 }}>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography sx={{ fontWeight: 800, fontSize: '1.2rem' }}>{p?.supplierName ?? 'Платёж'}</Typography>
          {p && <Typography variant="body2" color="text.secondary">УПД № {p.number ?? '—'} от {fmtDate(p.date)}</Typography>}
        </Box>
        <IconButton onClick={onClose} aria-label="Закрыть"><CloseIcon /></IconButton>
      </DialogTitle>
      <DialogContent>
        {isLoading && <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>}
        {error && <Alert severity="error">Не удалось загрузить платёж</Alert>}
        {p && (
          <>
            <Box sx={{ bgcolor: tokens[theme.palette.mode].surfaceMuted, borderRadius: '16px', px: 2, py: 1.5, mb: 1.5 }}>
              <Typography variant="body2" color="text.secondary">К оплате</Typography>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Typography sx={{ fontWeight: 800, fontSize: '1.6rem', flex: 1 }}>{fmtMoney(p.total)}</Typography>
                <CopyButton value={p.total.toFixed(2)} label="сумму" />
              </Box>
              <Typography variant="body2" color="text.secondary">
                {p.withoutVat ? 'НДС не облагается' : p.vatPhrase ?? (p.vatTotal !== null ? `в т.ч. НДС ${fmtMoney(p.vatTotal)}` : '')}
              </Typography>
            </Box>

            <DueSection
              inn={p.supplierInn}
              name={p.supplierName}
              dueDate={p.dueDate}
              daysLeft={p.daysLeft}
              paid={!!p.paidAt}
              dueFromUpd={p.dueFromUpd}
              updDelayDays={p.updDelayDays}
              supplierDelayDays={p.supplierDelayDays}
            />

            <Section title="Получатель">
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, columnGap: 2 }}>
                <Field label="Наименование" value={p.supplierName} wide />
                <Field label="ИНН" value={p.supplierInn} />
                <Field label="КПП" value={p.supplierKpp} />
                {p.bank ? (
                  <>
                    <Field label="Расчётный счёт" value={p.bank.account} wide />
                    <Field label="Банк" value={p.bank.bankName} wide />
                    <Field label="БИК" value={p.bank.bik} />
                    {/* 20 цифр в половину ширины не помещаются — переносится последняя */}
                    <Field label="Корр. счёт" value={p.bank.corrAccount} wide />
                  </>
                ) : (
                  <Alert severity="info" sx={{ gridColumn: '1 / -1', mt: 1 }}>
                    В УПД этого поставщика нет банковских реквизитов. Возьмите их из договора или счёта — как только они придут в любом его УПД, подставятся сами.
                  </Alert>
                )}
              </Box>
              {p.bankFromSupplier && (
                <Typography variant="caption" color="text.secondary">Реквизиты из прошлых УПД этого поставщика — сверьте со счётом</Typography>
              )}
            </Section>

            {p.paymentPurpose && (
              <Section title="Назначение платежа" action={<CopyButton value={p.paymentPurpose} label="назначение платежа" />}>
                <Typography sx={{ userSelect: 'all', lineHeight: 1.45 }}>{p.paymentPurpose}</Typography>
              </Section>
            )}

            {p.error && <Alert severity="warning">Не удалось разобрать файл накладной: {p.error}</Alert>}
            {actionError && <Alert severity="error" sx={{ mt: 1 }}>{actionError}</Alert>}
          </>
        )}
      </DialogContent>
      {p && (
        <DialogActions sx={{ px: 3, pb: 2.5, gap: 1, flexWrap: 'wrap' }}>
          {p.paidAt && (
            <Typography variant="body2" color="text.secondary" sx={{ flex: 1 }}>
              Оплачено {fmtDateTime(p.paidAt)}{p.paidBy ? ` · ${p.paidBy}` : ''}
            </Typography>
          )}
          <Button
            variant={p.paidAt ? 'text' : 'contained'}
            color={p.paidAt ? 'inherit' : 'secondary'}
            size="large"
            onClick={togglePaid}
            disabled={markPaid.isPending}
          >
            {p.paidAt ? 'Снять отметку об оплате' : 'Отметить оплаченным'}
          </Button>
        </DialogActions>
      )}
    </Dialog>
  );
};

export { CopyButton, Section };
export default PaymentDialog;
