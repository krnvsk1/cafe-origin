import * as React from 'react';
import {
  Box, Button, Dialog, DialogTitle, DialogContent, DialogActions, Rating,
  TextField, Typography, Alert,
} from '@mui/material';
import { StarBorder as StarBorderIcon } from '@mui/icons-material';
import type { Order } from '../types';
import { useMyReviews, useCreateReview } from '../api/reviews';
import { apiError } from '../api/inventory';

const COMPLETED = ['Доставлен', 'Выполнен'];
const LABELS: Record<number, string> = { 1: 'Ужасно', 2: 'Плохо', 3: 'Нормально', 4: 'Хорошо', 5: 'Отлично' };

/**
 * Оценка заказа клиентом: кнопка «Оценить» для полученных заказов
 * или уже поставленная оценка с ответом заведения.
 */
const OrderReviewBlock: React.FC<{ order: Order }> = ({ order }) => {
  const { data: reviews } = useMyReviews();
  const createReview = useCreateReview();
  const [open, setOpen] = React.useState(false);
  const [rating, setRating] = React.useState<number | null>(5);
  const [hover, setHover] = React.useState(-1);
  const [comment, setComment] = React.useState('');
  const [error, setError] = React.useState('');

  if (!COMPLETED.includes(order.status)) return null;
  const review = reviews?.find(r => r.orderId === order.id);

  if (review) {
    return (
      <Box sx={{ mt: 1.5 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography variant="body2" color="text.secondary">Ваша оценка:</Typography>
          <Rating value={review.rating} readOnly size="small" />
        </Box>
        {review.comment && (
          <Typography variant="body2" sx={{ mt: 0.5, fontStyle: 'italic' }}>«{review.comment}»</Typography>
        )}
        {review.adminReply && (
          <Alert severity="info" icon={false} sx={{ mt: 1, py: 0 }}>
            <Typography variant="body2"><strong>Ответ заведения:</strong> {review.adminReply}</Typography>
          </Alert>
        )}
      </Box>
    );
  }

  const handleSubmit = async () => {
    if (!rating) {
      setError('Поставьте оценку');
      return;
    }
    try {
      await createReview.mutateAsync({ orderId: order.id, rating, comment: comment.trim() || undefined });
      setOpen(false);
    } catch (e: unknown) {
      setError(apiError(e, 'Не удалось отправить отзыв'));
    }
  };

  return (
    <>
      <Button
        size="small"
        variant="outlined"
        startIcon={<StarBorderIcon />}
        onClick={() => { setError(''); setOpen(true); }}
        sx={{ mt: 1.5, borderRadius: '9999px' }}
      >
        Оценить заказ
      </Button>
      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle>Как вам заказ #{order.id}?</DialogTitle>
        <DialogContent>
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 1, my: 1 }}>
            <Rating
              value={rating}
              size="large"
              onChange={(_, v) => setRating(v)}
              onChangeActive={(_, v) => setHover(v)}
            />
            <Typography variant="body2" color="text.secondary" sx={{ minHeight: 20 }}>
              {LABELS[hover !== -1 ? hover : rating ?? 0] ?? ''}
            </Typography>
          </Box>
          <TextField
            fullWidth
            multiline
            rows={3}
            label="Комментарий (необязательно)"
            placeholder="Что понравилось, что улучшить?"
            value={comment}
            onChange={(e) => setComment(e.target.value.slice(0, 1000))}
            sx={{ mt: 1 }}
          />
          {error && <Alert severity="error" sx={{ mt: 2 }}>{error}</Alert>}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>Отмена</Button>
          <Button variant="contained" onClick={handleSubmit} disabled={createReview.isPending}>
            Отправить
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
};

export default OrderReviewBlock;
