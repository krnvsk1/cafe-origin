import * as React from 'react';
import { Box, Typography, Tabs, Tab } from '@mui/material';
import { Navigate, useNavigate, useParams } from 'react-router-dom';
import { useInventorySummary, fmtMoney, type TechCardTarget, type StockItem } from '../../api/inventory';
import { StatTile } from './ui';
import StockTab from './StockTab';
import DocumentsTab from './DocumentsTab';
import DishesTab from './DishesTab';
import SuppliersTab from './SuppliersTab';
import ReportsTab from './ReportsTab';
import EgaisTab from './EgaisTab';
import ChzTab from './ChzTab';
import EdoTab from './EdoTab';
import MercuryTab from './MercuryTab';
import TechCardEditor from './TechCardEditor';
import DocumentEditor from './DocumentEditor';
import { useCan } from '../../auth/permissions';
import { INVENTORY_SECTIONS, inventoryPath, sectionOf, type InventoryTab } from './sections';

/**
 * Раздел склада по адресу: /admin/stock, /admin/documents/writeoffs, /admin/egais и т.д. (см. sections.ts).
 * Внутри раздела — только его вкладки; сводка — на «Остатках».
 */
const InventoryPage: React.FC<{ section: string }> = ({ section: sectionKey }) => {
  const navigate = useNavigate();
  const { tab: tabParam } = useParams();
  const section = INVENTORY_SECTIONS.find(s => s.key === sectionKey)!;
  const tab: InventoryTab = section.tabs.find(t => t.value === tabParam)?.value ?? section.tabs[0].value;
  const showSummary = section.key === 'stock';
  const { data: summary } = useInventorySummary({ enabled: showSummary });

  const [lowOnly, setLowOnly] = React.useState(false);
  const [card, setCard] = React.useState<{ target: TechCardTarget; id: number } | null>(null);
  const [openDocId, setOpenDocId] = React.useState<number | null>(null);

  const go = (value: InventoryTab) => navigate(inventoryPath(value));
  const openSemiCard = (item: StockItem) => setCard({ target: 'semi', id: item.id });

  return (
    <Box sx={{ py: { xs: 2, sm: 3 }, maxWidth: 1240, mx: 'auto' }}>
      <Box sx={{ mb: 2 }}>
        <Typography variant="h4" component="h1" sx={{ fontSize: { xs: '1.75rem', sm: '2.1rem' } }}>{section.title}</Typography>
        <Typography color="text.secondary">{section.subtitle}</Typography>
      </Box>

      {showSummary && (
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
            hint="заполнено — подробнее в «Техкартах»"
            onClick={() => go('dishes')}
          />
        </Box>
      )}

      {section.tabs.length > 1 && (
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
          {section.tabs.map(t => <Tab key={t.value} value={t.value} label={t.label} />)}
        </Tabs>
      )}

      {tab === 'stock' && <StockTab mode="stock" lowOnlyDefault={lowOnly} onOpenDocument={setOpenDocId} />}
      {tab === 'items' && <StockTab mode="catalog" onOpenDocument={setOpenDocId} onOpenSemiCard={openSemiCard} />}
      {tab === 'receipts' && <DocumentsTab type="receipt" />}
      {tab === 'writeoffs' && <DocumentsTab type="writeoff" />}
      {tab === 'counts' && <DocumentsTab type="inventory" />}
      {tab === 'edo' && <EdoTab onOpenReceipt={setOpenDocId} />}
      {tab === 'dishes' && <DishesTab onOpenCard={(target, id) => setCard({ target, id })} />}
      {tab === 'reports' && <ReportsTab />}
      {tab === 'egais' && <EgaisTab />}
      {tab === 'mercury' && <MercuryTab />}
      {tab === 'chz' && <ChzTab />}
      {tab === 'suppliers' && <SuppliersTab />}

      {card && <TechCardEditor open target={card.target} id={card.id} onClose={() => setCard(null)} />}
      <DocumentEditor open={openDocId !== null} type="receipt" documentId={openDocId} onClose={() => setOpenDocId(null)} />
    </Box>
  );
};

/** Старые адреса /admin/inventory и /admin/inventory/<вкладка> — на новый раздел (первый доступный, если вкладка не указана) */
export const LegacyInventoryRedirect: React.FC = () => {
  const { tab } = useParams();
  const can = useCan();
  const known = sectionOf(tab);
  if (known && tab) return <Navigate to={inventoryPath(tab as InventoryTab)} replace />;
  const first = INVENTORY_SECTIONS.find(s => can(...s.perms));
  return <Navigate to={first ? first.base : '/'} replace />;
};

export default InventoryPage;
