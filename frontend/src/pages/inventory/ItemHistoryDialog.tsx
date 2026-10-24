import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, IconButton, Box, Typography, CircularProgress, Button,
  useMediaQuery, useTheme,
} from '@mui/material';
import { Close as CloseIcon, EditOutlined as EditIcon } from '@mui/icons-material';
import {
  useStockMovements, fmtQty, fmtMoney, fmtDateTime, MOVEMENT_LABELS, UNIT_LABELS, type StockItem,
} from '../../api/inventory';
import { tokens } from '../../theme';
import { EmptyState, Pill } from './ui';

interface Props {
  item: StockItem | null;
  onClose: () => void;
  onEdit?: (item: StockItem) => void;
  onOpenDocument?: (id: number) => void;
}

/** Карточка позиции: остаток, себестоимость и история движений */
const ItemHistoryDialog: React.FC<Props> = ({ item, onClose, onEdit, onOpenDocument }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const t = tokens[theme.palette.mode];
  const { data: movements = [], isLoading } = useStockMovements(item?.id ?? null);

  return (
    <Dialog open={!!item} onClose={onClose} fullScreen={fullScreen} maxWidth="sm" fullWidth>
      {item && (
        <>
          <DialogTitle sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, pr: 1.5 }}>
            <Box sx={{ flex: 1, minWidth: 0 }}>
              {item.name}
              <Typography color="text.secondary" sx={{ fontWeight: 400 }}>{item.category || 'Без категории'}</Typography>
            </Box>
            {onEdit && (
              <IconButton aria-label="Изменить позицию" onClick={() => onEdit(item)}>
                <EditIcon />
              </IconButton>
            )}
            <IconButton aria-label="Закрыть" onClick={onClose}><CloseIcon /></IconButton>
          </DialogTitle>
          <DialogContent>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 1, mb: 2.5 }}>
              {[
                { label: 'Остаток', value: fmtQty(item.quantity, item.unit), danger: item.isLow },
                { label: 'Себестоимость', value: `${fmtMoney(item.unitCost)}/${UNIT_LABELS[item.unit]}` },
                { label: 'На сумму', value: fmtMoney(item.value) },
              ].map(s => (
                <Box key={s.label} sx={{ bgcolor: t.surfaceMuted, borderRadius: '14px', p: 1.5 }}>
                  <Typography variant="caption" color="text.secondary">{s.label}</Typography>
                  <Typography sx={{ fontWeight: 700, color: s.danger ? tokens.chili : 'text.primary' }}>{s.value}</Typography>
                </Box>
              ))}
            </Box>

            <Typography sx={{ fontWeight: 700, mb: 1 }}>История движений</Typography>
            {isLoading ? (
              <Box sx={{ display: 'grid', placeItems: 'center', py: 4 }}><CircularProgress size={28} /></Box>
            ) : movements.length === 0 ? (
              <EmptyState title="Движений пока не было" text="Здесь появятся приходы, продажи, списания и инвентаризации" />
            ) : (
              <Box>
                {movements.map(m => (
                  <Box key={m.id} sx={{ display: 'flex', alignItems: 'center', gap: 1.5, py: 1.25, borderTop: '1px solid', borderColor: 'divider' }}>
                    <Box sx={{ flex: 1, minWidth: 0 }}>
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                        <Pill>{MOVEMENT_LABELS[m.type]}</Pill>
                        <Typography variant="body2" color="text.secondary">{fmtDateTime(m.createdAt)}</Typography>
                      </Box>
                      {m.comment && (
                        m.documentId && onOpenDocument ? (
                          <Button
                            size="small"
                            onClick={() => onOpenDocument(m.documentId!)}
                            sx={{ p: 0, minHeight: 0, mt: 0.25, color: 'text.primary', textDecoration: 'underline', textUnderlineOffset: 3 }}
                          >
                            {m.comment}
                          </Button>
                        ) : (
                          <Typography variant="body2" sx={{ mt: 0.25 }}>{m.comment}</Typography>
                        )
                      )}
                    </Box>
                    <Box sx={{ textAlign: 'right' }}>
                      <Typography sx={{ fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: m.quantity > 0 ? tokens.herb : 'text.primary' }}>
                        {m.quantity > 0 ? '+' : ''}{fmtQty(m.quantity, item.unit)}
                      </Typography>
                      <Typography variant="caption" color="text.secondary">остаток {fmtQty(m.balanceAfter)}</Typography>
                    </Box>
                  </Box>
                ))}
              </Box>
            )}
          </DialogContent>
        </>
      )}
    </Dialog>
  );
};

export default ItemHistoryDialog;
