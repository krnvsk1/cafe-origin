import * as React from 'react';
import { Box, Typography, CircularProgress, Alert, TextField, InputAdornment, alpha } from '@mui/material';
import { ChevronRight as ChevronRightIcon, Search as SearchIcon, StorefrontOutlined as SupplierIcon } from '@mui/icons-material';
import { useFinanceSuppliers } from '../../api/finance';
import { fmtMoney, fmtDate, apiError } from '../../api/inventory';
import { Panel, ListRow, EmptyState, Pill } from '../inventory/ui';
import { tokens } from '../../theme';

/** Поставщики в «Финансах»: кому и сколько должны, условия и реквизиты */
const SuppliersView: React.FC<{ onOpen: (inn: string) => void }> = ({ onOpen }) => {
  const { data = [], isLoading, error } = useFinanceSuppliers();
  const [query, setQuery] = React.useState('');

  const q = query.trim().toLowerCase();
  const shown = q ? data.filter(s => s.name.toLowerCase().includes(q) || s.inn.includes(q)) : data;

  return (
    <>
      <TextField
        placeholder="Найти поставщика или ИНН"
        value={query}
        onChange={e => setQuery(e.target.value)}
        size="small"
        sx={{ '& .MuiOutlinedInput-root': { bgcolor: 'background.paper' } }}
        slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon sx={{ color: 'text.secondary' }} /></InputAdornment> } }}
      />
      {error && <Alert severity="error">{apiError(error, 'Не удалось загрузить поставщиков')}</Alert>}
      <Panel padding={false}>
        {isLoading ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>
        ) : shown.length === 0 ? (
          <EmptyState icon={<SupplierIcon />} title={q ? 'Никого не нашлось' : 'Поставщиков пока нет'} text={q ? undefined : 'Появятся вместе с накладными из СБИС.'} />
        ) : shown.map(s => (
          <ListRow key={s.inn} onClick={() => onOpen(s.inn)}>
            <Box sx={{ flex: 1, minWidth: 0 }}>
              <Typography sx={{ fontWeight: 600 }} noWrap>{s.name}</Typography>
              <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap', alignItems: 'center', mt: 0.25 }}>
                <Typography variant="body2" color="text.secondary">ИНН {s.inn}</Typography>
                <Pill>{s.paymentDelayDays !== null ? `отсрочка ${s.paymentDelayDays} дн.` : 'отсрочка не задана'}</Pill>
                {!s.hasBank && <Pill color={tokens.chili} bg={alpha(tokens.chili, 0.1)}>нет реквизитов</Pill>}
              </Box>
            </Box>
            <Box sx={{ textAlign: 'right', flexShrink: 0 }}>
              <Typography sx={{ fontWeight: 700, whiteSpace: 'nowrap' }}>{s.unpaidTotal ? fmtMoney(s.unpaidTotal) : 'не должны'}</Typography>
              <Typography variant="caption" sx={{ whiteSpace: 'nowrap', color: s.overdueTotal ? tokens.chili : 'text.secondary' }}>
                {s.overdueTotal
                  ? `просрочено ${fmtMoney(s.overdueTotal)}`
                  : s.nextDueDate ? `ближайший до ${fmtDate(s.nextDueDate)}`
                  : s.noDueCount ? 'срок не указан' : ''}
              </Typography>
            </Box>
            <ChevronRightIcon sx={{ color: 'text.secondary', display: { xs: 'none', sm: 'block' } }} />
          </ListRow>
        ))}
      </Panel>
    </>
  );
};

export default SuppliersView;
