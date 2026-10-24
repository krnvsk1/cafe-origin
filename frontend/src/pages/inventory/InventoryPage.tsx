import * as React from 'react';
import { Box, Typography, Tabs, Tab } from '@mui/material';
import { useNavigate, useParams } from 'react-router-dom';
import { useInventorySummary, fmtMoney, type TechCardTarget, type StockItem } from '../../api/inventory';
import { StatTile } from './ui';
import StockTab from './StockTab';
import DocumentsTab from './DocumentsTab';
import DishesTab from './DishesTab';
import SuppliersTab from './SuppliersTab';
import ReportsTab from './ReportsTab';
import TechCardEditor from './TechCardEditor';
import DocumentEditor from './DocumentEditor';

const TABS = [
  { value: 'stock', label: 'Остатки' },
  { value: 'receipts', label: 'Приходы' },
  { value: 'writeoffs', label: 'Списания' },
  { value: 'counts', label: 'Инвентаризация' },
  { value: 'dishes', label: 'Блюда и техкарты' },
  { value: 'items', label: 'Номенклатура' },
  { value: 'suppliers', label: 'Поставщики' },
  { value: 'reports', label: 'Отчёты' },
] as const;

type TabValue = typeof TABS[number]['value'];

/**
 * Раздел «Склад»: остатки, документы движения, техкарты блюд и отчёты по себестоимости
 */
const InventoryPage: React.FC = () => {
  const navigate = useNavigate();
  const { tab: tabParam } = useParams();
  const tab: TabValue = (TABS.find(t => t.value === tabParam)?.value ?? 'stock');
  const { data: summary } = useInventorySummary();

  const [lowOnly, setLowOnly] = React.useState(false);
  const [card, setCard] = React.useState<{ target: TechCardTarget; id: number } | null>(null);
  const [openDocId, setOpenDocId] = React.useState<number | null>(null);

  const go = (value: TabValue) => navigate(value === 'stock' ? '/admin/inventory' : `/admin/inventory/${value}`);

  const openSemiCard = (item: StockItem) => setCard({ target: 'semi', id: item.id });

  return (
    <Box sx={{ py: { xs: 2, sm: 3 }, maxWidth: 1240, mx: 'auto' }}>
      <Typography variant="h4" component="h1" sx={{ fontSize: { xs: '1.75rem', sm: '2.1rem' }, mb: 2 }}>Склад</Typography>

      {/* Сводка */}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: { xs: 1, sm: 1.5 }, mb: 2.5 }}>
        <StatTile
          label="На складе на сумму"
          value={summary ? fmtMoney(summary.stockValue) : '…'}
          hint={summary ? `${summary.itemsCount} позиций` : undefined}
          onClick={() => { setLowOnly(false); go('stock'); }}
        />
        <StatTile
          label="Заканчивается"
          value={summary ? summary.lowCount : '…'}
          hint={summary && summary.negativeCount > 0 ? `в минусе: ${summary.negativeCount}` : 'ниже минимального остатка'}
          tone={summary && (summary.lowCount > 0 || summary.negativeCount > 0) ? 'danger' : 'default'}
          onClick={() => { setLowOnly(true); go('stock'); }}
        />
        <StatTile
          label="Черновики документов"
          value={summary ? summary.draftsCount : '…'}
          hint="ещё не проведены"
          tone={summary && summary.draftsCount > 0 ? 'warning' : 'default'}
          onClick={() => go('receipts')}
        />
        <StatTile
          label="Техкарты блюд"
          value={summary ? `${summary.dishCardsCount}` : '…'}
          hint="заполнено — подробнее в «Блюдах»"
          onClick={() => go('dishes')}
        />
      </Box>

      <Tabs
        value={tab}
        onChange={(_, v) => go(v)}
        variant="scrollable"
        scrollButtons={false}
        sx={{
          mb: 2.5,
          minHeight: 40,
          mx: { xs: -2, sm: 0 },
          px: { xs: 2, sm: 0 },
          '& .MuiTabs-indicator': { display: 'none' },
          '& .MuiTabs-flexContainer': { gap: 1 },
          '& .MuiTab-root': {
            minHeight: 40,
            px: 2,
            borderRadius: '12px',
            bgcolor: 'background.paper',
            color: 'text.primary',
            '&.Mui-selected': { bgcolor: 'text.primary', color: 'background.paper' },
          },
        }}
      >
        {TABS.map(t => <Tab key={t.value} value={t.value} label={t.label} />)}
      </Tabs>

      {tab === 'stock' && <StockTab mode="stock" lowOnlyDefault={lowOnly} onOpenDocument={setOpenDocId} />}
      {tab === 'receipts' && <DocumentsTab type="receipt" />}
      {tab === 'writeoffs' && <DocumentsTab type="writeoff" />}
      {tab === 'counts' && <DocumentsTab type="inventory" />}
      {tab === 'dishes' && <DishesTab onOpenCard={(target, id) => setCard({ target, id })} />}
      {tab === 'items' && <StockTab mode="catalog" onOpenDocument={setOpenDocId} onOpenSemiCard={openSemiCard} />}
      {tab === 'suppliers' && <SuppliersTab />}
      {tab === 'reports' && <ReportsTab />}

      {card && <TechCardEditor open target={card.target} id={card.id} onClose={() => setCard(null)} />}
      <DocumentEditor open={openDocId !== null} type="receipt" documentId={openDocId} onClose={() => setOpenDocId(null)} />
    </Box>
  );
};

export default InventoryPage;
