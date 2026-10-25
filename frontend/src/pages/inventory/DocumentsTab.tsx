import * as React from 'react';
import {
  Box, Button, Typography, CircularProgress, TextField, InputAdornment, ToggleButtonGroup, ToggleButton, Pagination,
} from '@mui/material';
import {
  Add as AddIcon, Search as SearchIcon, ReceiptLongOutlined as ReceiptIcon, ChevronRight as ChevronRightIcon,
  AttachFile as AttachIcon,
} from '@mui/icons-material';
import {
  useStockDocuments, fmtMoney, fmtDate, DOCUMENT_TITLES, WRITEOFF_REASONS,
  type StockDocumentType, type StockDocumentStatus,
} from '../../api/inventory';
import { Panel, ListRow, ListHeader, EmptyState, StatusChip } from './ui';
import DocumentEditor from './DocumentEditor';

const EMPTY_TEXT: Record<StockDocumentType, string> = {
  receipt: 'Внесите накладную от поставщика — остатки и себестоимость обновятся после проведения. Новые позиции можно создавать прямо в накладной.',
  writeoff: 'Списывайте испорченные продукты, просрочку и питание персонала — так остатки будут совпадать с реальностью.',
  inventory: 'Пересчитайте фактические остатки — система покажет расхождения с учётом и выровняет склад.',
};

interface Props {
  type: StockDocumentType;
  /** Открыть документ сразу (например, из истории позиции) */
  openId?: number | null;
  onOpenedId?: () => void;
}

const DocumentsTab: React.FC<Props> = ({ type, openId, onOpenedId }) => {
  const [status, setStatus] = React.useState<StockDocumentStatus | ''>('');
  const [search, setSearch] = React.useState('');
  const [debounced, setDebounced] = React.useState('');
  const [page, setPage] = React.useState(1);
  const [editor, setEditor] = React.useState<{ id: number | null } | null>(null);

  React.useEffect(() => {
    const id = setTimeout(() => { setDebounced(search); setPage(1); }, 250);
    return () => clearTimeout(id);
  }, [search]);

  React.useEffect(() => {
    if (openId) {
      setEditor({ id: openId });
      onOpenedId?.();
    }
  }, [openId, onOpenedId]);

  React.useEffect(() => { setPage(1); setStatus(''); setSearch(''); }, [type]);

  const { data, isLoading, isFetching } = useStockDocuments({ type, status, search: debounced, page });
  const titles = DOCUMENT_TITLES[type];
  const docs = data?.items ?? [];
  const pages = data ? Math.ceil(data.total / data.pageSize) : 1;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
        <TextField
          placeholder={type === 'receipt' ? 'Номер или поставщик' : 'Поиск по комментарию'}
          value={search}
          onChange={e => setSearch(e.target.value)}
          size="small"
          sx={{ flex: '1 1 220px', '& .MuiOutlinedInput-root': { bgcolor: 'background.paper' } }}
          slotProps={{
            input: {
              startAdornment: <InputAdornment position="start"><SearchIcon sx={{ color: 'text.secondary' }} /></InputAdornment>,
              endAdornment: isFetching && !isLoading ? <CircularProgress size={16} /> : undefined,
            },
          }}
        />
        <ToggleButtonGroup exclusive size="small" value={status} onChange={(_, v) => { if (v !== null) { setStatus(v); setPage(1); } }}>
          <ToggleButton value="">Все</ToggleButton>
          <ToggleButton value="draft">Черновики</ToggleButton>
          <ToggleButton value="posted">Проведённые</ToggleButton>
        </ToggleButtonGroup>
        <Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={() => setEditor({ id: null })} sx={{ ml: { md: 'auto' } }}>
          {titles.create}
        </Button>
      </Box>

      <Panel padding={false}>
        {isLoading ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress size={32} /></Box>
        ) : docs.length === 0 ? (
          <EmptyState
            icon={<ReceiptIcon />}
            title={debounced || status ? 'Ничего не найдено' : `${titles.many} пока не создавались`}
            text={debounced || status ? 'Попробуйте изменить фильтры' : EMPTY_TEXT[type]}
            action={!debounced && !status && (
              <Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={() => setEditor({ id: null })}>{titles.create}</Button>
            )}
          />
        ) : (
          <>
            <ListHeader>
              <Box sx={{ width: 120 }}>Дата</Box>
              <Box sx={{ flex: 1 }}>{type === 'receipt' ? 'Накладная' : 'Документ'}</Box>
              <Box sx={{ width: 120 }}>Статус</Box>
              <Box sx={{ width: 140, textAlign: 'right' }}>{type === 'inventory' ? 'Расхождение' : 'Сумма'}</Box>
              <Box sx={{ width: 24 }} />
            </ListHeader>
            {docs.map(d => {
              const title = type === 'receipt'
                ? d.supplierName || 'Без поставщика'
                : type === 'writeoff'
                  ? WRITEOFF_REASONS[d.reason ?? ''] ?? 'Списание'
                  : 'Инвентаризация';
              return (
                <ListRow key={d.id} onClick={() => setEditor({ id: d.id })} muted={d.status === 'cancelled'}>
                  <Box sx={{ width: 120, flexShrink: 0, whiteSpace: 'nowrap', display: { xs: 'none', md: 'block' }, color: 'text.secondary' }}>{fmtDate(d.date)}</Box>
                  <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                      <Typography sx={{ fontWeight: 600 }} noWrap>{title}</Typography>
                      {d.attachmentPath && <AttachIcon sx={{ fontSize: 16, color: 'text.secondary' }} />}
                    </Box>
                    <Typography variant="body2" color="text.secondary" noWrap>
                      <Box component="span" sx={{ display: { md: 'none' } }}>{fmtDate(d.date)} · </Box>
                      {d.number ? `№ ${d.number} · ` : ''}{d.linesCount} поз.{d.comment ? ` · ${d.comment}` : ''}
                    </Typography>
                  </Box>
                  <Box sx={{ width: 120, display: { xs: 'none', md: 'block' } }}><StatusChip status={d.status} /></Box>
                  <Box sx={{ width: { md: 140 }, textAlign: 'right' }}>
                    <Typography sx={{ fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>{fmtMoney(d.total)}</Typography>
                    <Box sx={{ display: { md: 'none' }, mt: 0.25 }}><StatusChip status={d.status} /></Box>
                  </Box>
                  <ChevronRightIcon sx={{ color: 'text.secondary', display: { xs: 'none', sm: 'block' } }} />
                </ListRow>
              );
            })}
          </>
        )}
      </Panel>

      {pages > 1 && (
        <Box sx={{ display: 'flex', justifyContent: 'center' }}>
          <Pagination count={pages} page={page} onChange={(_, p) => setPage(p)} shape="rounded" />
        </Box>
      )}

      <DocumentEditor
        open={!!editor}
        type={type}
        documentId={editor?.id ?? null}
        onClose={() => setEditor(null)}
      />
    </Box>
  );
};

export default DocumentsTab;
