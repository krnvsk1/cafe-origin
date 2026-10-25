import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, Box, Typography, Button, IconButton, TextField, CircularProgress, Alert,
  useMediaQuery, useTheme,
} from '@mui/material';
import { Close as CloseIcon, ChevronRight as ChevronRightIcon } from '@mui/icons-material';
import { useFinanceSupplier, useSaveSupplierRequisites, type SupplierBank } from '../../api/finance';
import { fmtMoney, fmtDate, apiError } from '../../api/inventory';
import { tokens } from '../../theme';
import { DueLabel } from './DueLabel';
import { SupplierDelayField } from './SupplierDelayField';
import { Section } from './PaymentDialog';

const EMPTY_BANK: SupplierBank = { account: null, bik: null, corrAccount: null, bankName: null };

// Длина реквизитов в цифрах
const ACCOUNT_DIGITS = 20;
const BIK_DIGITS = 9;

const digitsOnly = (value: string, max: number) => value.replace(/\D/g, '').slice(0, max);

/** Реквизиты поставщика: подставляются из УПД, но их можно ввести или поправить вручную */
const RequisitesForm: React.FC<{ inn: string; name: string; bank: SupplierBank }> = ({ inn, name, bank }) => {
  const save = useSaveSupplierRequisites();
  const [form, setForm] = React.useState<SupplierBank>(bank);
  const [error, setError] = React.useState('');
  const [saved, setSaved] = React.useState(false);
  React.useEffect(() => setForm(bank), [bank]);

  const set = (key: keyof SupplierBank, value: string) => {
    setSaved(false);
    setForm(prev => ({ ...prev, [key]: value }));
  };
  const changed = (Object.keys(form) as (keyof SupplierBank)[]).some(k => (form[k] ?? '') !== (bank[k] ?? ''));

  const fieldError = (value: string | null, digits: number) =>
    value && value.length > 0 && value.length !== digits ? `${digits} цифр` : undefined;
  const invalid = !!(fieldError(form.account, ACCOUNT_DIGITS) || fieldError(form.bik, BIK_DIGITS) || fieldError(form.corrAccount, ACCOUNT_DIGITS));

  const submit = async () => {
    setError('');
    try {
      await save.mutateAsync({ inn, name, ...form });
      setSaved(true);
    } catch (e) {
      setError(apiError(e));
    }
  };

  return (
    <>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 1.5, mt: 1 }}>
        <TextField
          size="small" label="Расчётный счёт" value={form.account ?? ''}
          onChange={e => set('account', digitsOnly(e.target.value, ACCOUNT_DIGITS))}
          error={!!fieldError(form.account, ACCOUNT_DIGITS)} helperText={fieldError(form.account, ACCOUNT_DIGITS)}
          sx={{ gridColumn: '1 / -1' }}
        />
        <TextField
          size="small" label="Банк" value={form.bankName ?? ''}
          onChange={e => set('bankName', e.target.value.slice(0, 300))}
          sx={{ gridColumn: '1 / -1' }}
        />
        <TextField
          size="small" label="БИК" value={form.bik ?? ''}
          onChange={e => set('bik', digitsOnly(e.target.value, BIK_DIGITS))}
          error={!!fieldError(form.bik, BIK_DIGITS)} helperText={fieldError(form.bik, BIK_DIGITS)}
        />
        <TextField
          size="small" label="Корр. счёт" value={form.corrAccount ?? ''}
          onChange={e => set('corrAccount', digitsOnly(e.target.value, ACCOUNT_DIGITS))}
          error={!!fieldError(form.corrAccount, ACCOUNT_DIGITS)} helperText={fieldError(form.corrAccount, ACCOUNT_DIGITS)}
        />
      </Box>
      {error && <Alert severity="error" sx={{ mt: 1 }}>{error}</Alert>}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mt: 1.5 }}>
        <Button variant="outlined" onClick={submit} disabled={!changed || invalid || save.isPending}>Сохранить реквизиты</Button>
        {saved && !changed && <Typography variant="body2" sx={{ color: tokens.herb }}>Сохранено</Typography>}
      </Box>
    </>
  );
};

/** Карточка поставщика в «Финансах»: долг, отсрочка, реквизиты и неоплаченные накладные */
const SupplierDialog: React.FC<{ inn: string | null; onClose: () => void; onOpenPayment: (id: number) => void }> = ({ inn, onClose, onOpenPayment }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const { data: s, isLoading, error } = useFinanceSupplier(inn);
  const unpaid = s?.invoices.filter(i => !i.paidAt) ?? [];

  return (
    <Dialog open={!!inn} onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen}>
      <DialogTitle sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, pb: 1 }}>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography sx={{ fontWeight: 800, fontSize: '1.2rem' }}>{s?.name ?? 'Поставщик'}</Typography>
          {s && <Typography variant="body2" color="text.secondary">ИНН {s.inn}{s.kpp ? ` · КПП ${s.kpp}` : ''}</Typography>}
        </Box>
        <IconButton onClick={onClose} aria-label="Закрыть"><CloseIcon /></IconButton>
      </DialogTitle>
      <DialogContent>
        {isLoading && <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>}
        {error && <Alert severity="error">Не удалось загрузить поставщика</Alert>}
        {s && (
          <>
            <Box sx={{ bgcolor: tokens[theme.palette.mode].surfaceMuted, borderRadius: '16px', px: 2, py: 1.5, mb: 1.5 }}>
              <Typography variant="body2" color="text.secondary">К оплате</Typography>
              <Typography sx={{ fontWeight: 800, fontSize: '1.6rem' }}>{fmtMoney(s.unpaidTotal)}</Typography>
              <Typography variant="body2" color="text.secondary">
                {unpaid.length ? `${unpaid.length} неоплаченных из ${s.invoices.length}` : `Всё оплачено · накладных ${s.invoices.length}`}
              </Typography>
            </Box>

            <Section title="Условия оплаты">
              <Box sx={{ mt: 1 }}>
                <SupplierDelayField inn={s.inn} name={s.name} value={s.paymentDelayDays} />
              </Box>
            </Section>

            <Section title="Реквизиты для оплаты">
              <Typography variant="body2" color="text.secondary">
                Подставляются из УПД поставщика, если он их указывает. Можно ввести из договора или счёта.
              </Typography>
              <RequisitesForm inn={s.inn} name={s.name} bank={s.bank ?? EMPTY_BANK} />
            </Section>

            <Section title="Неоплаченные накладные">
              {unpaid.length === 0 ? (
                <Typography variant="body2" color="text.secondary" sx={{ py: 1 }}>Нет</Typography>
              ) : unpaid.map(i => (
                <Box
                  key={i.id}
                  component="button"
                  type="button"
                  onClick={() => onOpenPayment(i.id)}
                  sx={{
                    display: 'flex', alignItems: 'center', gap: 1.5, width: '100%', py: 1, px: 0,
                    border: 0, borderTop: '1px solid', borderColor: 'divider', bgcolor: 'transparent',
                    color: 'inherit', font: 'inherit', textAlign: 'left', cursor: 'pointer',
                    '&:first-of-type': { borderTop: 0 },
                  }}
                >
                  <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Typography sx={{ fontWeight: 600 }} noWrap>№ {i.number ?? '—'} от {fmtDate(i.date)}</Typography>
                    {i.dueDate && <Typography variant="body2" color="text.secondary">до {fmtDate(i.dueDate)}</Typography>}
                  </Box>
                  <Box sx={{ textAlign: 'right', display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 0.5 }}>
                    <Typography sx={{ fontWeight: 700, whiteSpace: 'nowrap' }}>{fmtMoney(i.total)}</Typography>
                    <DueLabel dueDate={i.dueDate} daysLeft={i.daysLeft} paid={false} />
                  </Box>
                  <ChevronRightIcon sx={{ color: 'text.secondary' }} />
                </Box>
              ))}
            </Section>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
};

export default SupplierDialog;
