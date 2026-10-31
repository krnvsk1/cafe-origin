import * as React from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions, Button, Typography, Box, CircularProgress, Alert,
  Table, TableHead, TableBody, TableRow, TableCell, useMediaQuery, useTheme,
} from '@mui/material';
import { MenuBookOutlined as RecipeIcon } from '@mui/icons-material';
import { useKitchenTechCard, type KitchenOrderItem, type KitchenTechCard } from '../api/staff';
import { fmtQty, fmtPortions, type StockUnit } from '../api/inventory';
import { resolveMediaUrl } from '../utils/media';

const qty = (value: number, unit: string) => (unit === 'portion' ? fmtPortions(value) : fmtQty(value, (unit || null) as StockUnit | null));

const CardSection: React.FC<{ card: KitchenTechCard; heading?: string; portions: number }> = ({ card, heading, portions }) => (
  <Box sx={{ mb: 3 }}>
    <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1, flexWrap: 'wrap', mb: 1 }}>
      <Typography sx={{ fontWeight: 700, fontSize: '1.05rem' }}>{heading ?? card.title}</Typography>
      {card.outputWeight ? <Typography variant="body2" color="text.secondary">выход {card.outputWeight} г</Typography> : null}
    </Box>
    {card.lines.length > 0 && (
      <Table size="small" sx={{ '& td, & th': { px: 1 } }}>
        <TableHead>
          <TableRow>
            <TableCell>Ингредиент</TableCell>
            <TableCell align="right">Брутто</TableCell>
            <TableCell align="right">Нетто</TableCell>
            {portions > 1 && <TableCell align="right">На {portions} порц.</TableCell>}
          </TableRow>
        </TableHead>
        <TableBody>
          {card.lines.map((line, i) => (
            <TableRow key={`${line.stockItemId ?? `d${line.dishId}`}-${i}`}>
              <TableCell sx={{ fontWeight: 500 }}>{line.name}</TableCell>
              <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>{qty(line.gross, line.unit)}</TableCell>
              <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                {qty(line.net, line.unit)}
                {line.lossPercent > 0 && (
                  <Typography component="span" variant="caption" color="text.secondary"> (−{line.lossPercent}%)</Typography>
                )}
              </TableCell>
              {portions > 1 && (
                <TableCell align="right" sx={{ whiteSpace: 'nowrap', fontWeight: 700 }}>{qty(line.net * portions, line.unit)}</TableCell>
              )}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    )}
    {card.instructions && (
      <Box sx={{ mt: 1.5 }}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 0.5 }}>Технология</Typography>
        <Typography sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.5 }}>{card.instructions}</Typography>
      </Box>
    )}
  </Box>
);

/** Техкарта позиции заказа на кухне: состав и технология блюда, выбранных добавок и полуфабрикатов */
const KitchenTechCardDialog: React.FC<{ item: KitchenOrderItem | null; onClose: () => void }> = ({ item, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const { data, isLoading, error } = useKitchenTechCard(item);
  const portions = item?.quantity ?? 1;
  const empty = !!data && !data.dish && data.addons.length === 0;

  return (
    <Dialog open={!!item} onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen}>
      <DialogTitle sx={{ pb: 0.5 }}>
        {item?.name}
        {portions > 1 && <Typography component="span" color="text.secondary" sx={{ ml: 1 }}>× {portions}</Typography>}
      </DialogTitle>
      <DialogContent>
        {isLoading && <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>}
        {error && <Alert severity="error">Не удалось загрузить техкарту</Alert>}
        {/* Сначала фото блюда — повар сверяет подачу */}
        {data?.image && (
          <Box component="img" src={resolveMediaUrl(data.image)} alt={item?.name ?? 'Фото блюда'}
            sx={{ width: '100%', maxHeight: { xs: 260, sm: 340 }, objectFit: 'cover', borderRadius: 3, mb: 2, display: 'block', bgcolor: 'action.hover' }} />
        )}
        {empty && (
          <Box sx={{ textAlign: 'center', py: 5, color: 'text.secondary' }}>
            <RecipeIcon sx={{ fontSize: 44, mb: 1 }} />
            <Typography sx={{ fontWeight: 700, color: 'text.primary' }}>Техкарта не заполнена</Typography>
            <Typography variant="body2">Её заполняют на складе: «Блюда и техкарты».</Typography>
          </Box>
        )}
        {data?.dish && <CardSection card={data.dish} heading={data.addons.length ? 'Блюдо' : data.dish.title} portions={portions} />}
        {data?.addons.map(card => <CardSection key={card.title} card={card} heading={`Добавка: ${card.title}`} portions={portions} />)}
        {data && data.semis.length > 0 && (
          <>
            <Typography variant="overline" color="text.secondary">Полуфабрикаты и блюда из состава</Typography>
            {data.semis.map(card => <CardSection key={card.title} card={card} portions={1} />)}
          </>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose} variant="contained" size="large">Закрыть</Button>
      </DialogActions>
    </Dialog>
  );
};

export default KitchenTechCardDialog;
