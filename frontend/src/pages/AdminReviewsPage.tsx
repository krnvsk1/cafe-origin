import * as React from 'react';
import {
  Box, Typography, Paper, Rating, Chip, LinearProgress, Alert, Button, TextField,
  Pagination, ToggleButton, ToggleButtonGroup, FormControlLabel, Checkbox,
} from '@mui/material';
import { Star as StarIcon } from '@mui/icons-material';
import { useAdminReviews, useReplyReview } from '../api/reviews';
import type { ReviewsFilter, ReviewsAdminResponse } from '../api/reviews';

type ReviewRow = ReviewsAdminResponse['items'][number];

const ReplyBox: React.FC<{ row: ReviewRow }> = ({ row }) => {
  const reply = useReplyReview();
  const [editing, setEditing] = React.useState(false);
  const [text, setText] = React.useState(row.review.adminReply ?? '');

  if (!editing) {
    return row.review.adminReply ? (
      <Alert
        severity="success"
        icon={false}
        sx={{ mt: 1 }}
        action={<Button size="small" onClick={() => setEditing(true)}>Изменить</Button>}
      >
        <strong>Ваш ответ:</strong> {row.review.adminReply}
      </Alert>
    ) : (
      <Button size="small" sx={{ mt: 1 }} onClick={() => setEditing(true)}>Ответить</Button>
    );
  }

  return (
    <Box sx={{ mt: 1 }}>
      <TextField
        fullWidth
        multiline
        minRows={2}
        size="small"
        placeholder="Ответ клиенту — он увидит его в своих заказах"
        value={text}
        onChange={(e) => setText(e.target.value.slice(0, 1000))}
      />
      <Box sx={{ display: 'flex', gap: 1, mt: 1 }}>
        <Button
          size="small"
          variant="contained"
          disabled={reply.isPending}
          onClick={async () => {
            await reply.mutateAsync({ id: row.review.id, reply: text });
            setEditing(false);
          }}
        >
          Сохранить
        </Button>
        <Button size="small" onClick={() => setEditing(false)}>Отмена</Button>
      </Box>
    </Box>
  );
};

const AdminReviewsPage: React.FC = () => {
  const [filter, setFilter] = React.useState<ReviewsFilter>({ page: 1 });
  const { data, isLoading, error } = useAdminReviews(filter);
  const summary = data?.summary;
  const maxCount = Math.max(1, ...(summary?.distribution.map(d => d.count) ?? [1]));

  return (
    <Box sx={{ maxWidth: 960, mx: 'auto' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 3 }}>
        <StarIcon sx={{ color: 'warning.main' }} />
        <Typography variant="h4" fontWeight={700}>Отзывы</Typography>
      </Box>

      {summary && (
        <Paper sx={{ p: 3, mb: 3, display: 'flex', gap: 4, flexWrap: 'wrap', alignItems: 'center' }}>
          <Box sx={{ textAlign: 'center', minWidth: 140 }}>
            <Typography variant="h2" fontWeight={700}>{summary.average.toFixed(1)}</Typography>
            <Rating value={summary.average} precision={0.1} readOnly />
            <Typography variant="body2" color="text.secondary">{summary.count} отзывов</Typography>
          </Box>
          <Box sx={{ flex: 1, minWidth: 220 }}>
            {[...summary.distribution].reverse().map(d => (
              <Box key={d.rating} sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.5 }}>
                <Typography variant="body2" sx={{ width: 16 }}>{d.rating}</Typography>
                <StarIcon sx={{ fontSize: 16, color: 'warning.main' }} />
                <LinearProgress
                  variant="determinate"
                  value={(d.count / maxCount) * 100}
                  color={d.rating <= 2 ? 'error' : d.rating === 3 ? 'warning' : 'success'}
                  sx={{ flex: 1, height: 8, borderRadius: 4 }}
                />
                <Typography variant="body2" color="text.secondary" sx={{ width: 32, textAlign: 'right' }}>{d.count}</Typography>
              </Box>
            ))}
          </Box>
        </Paper>
      )}

      <Paper sx={{ p: 2, mb: 2, display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'center' }}>
        <ToggleButtonGroup
          size="small"
          exclusive
          value={filter.rating ?? 0}
          onChange={(_, v) => v !== null && setFilter(f => ({ ...f, rating: v || undefined, page: 1 }))}
        >
          <ToggleButton value={0}>Все</ToggleButton>
          {[5, 4, 3, 2, 1].map(n => <ToggleButton key={n} value={n}>{n}★</ToggleButton>)}
        </ToggleButtonGroup>
        <FormControlLabel
          control={<Checkbox checked={!!filter.withComment} onChange={(e) => setFilter(f => ({ ...f, withComment: e.target.checked || undefined, page: 1 }))} />}
          label="С комментарием"
        />
        <FormControlLabel
          control={<Checkbox checked={!!filter.unanswered} onChange={(e) => setFilter(f => ({ ...f, unanswered: e.target.checked || undefined, page: 1 }))} />}
          label="Без ответа"
        />
      </Paper>

      {isLoading && <LinearProgress />}
      {error && <Alert severity="error">Не удалось загрузить отзывы</Alert>}
      {data && data.items.length === 0 && (
        <Typography color="text.secondary" sx={{ textAlign: 'center', py: 4 }}>Отзывов пока нет</Typography>
      )}

      {data?.items.map(row => (
        <Paper key={row.review.id} sx={{ p: 2, mb: 1.5 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
            <Rating value={row.review.rating} readOnly size="small" />
            <Typography fontWeight={600}>{row.review.customerName || 'Клиент'}</Typography>
            <Chip size="small" label={`Заказ #${row.review.orderId} · ${row.orderTotal} ₽`} />
            <Typography variant="body2" color="text.secondary" sx={{ ml: 'auto' }}>
              {new Date(row.review.createdAt).toLocaleString('ru-RU')}
            </Typography>
          </Box>
          {row.items.length > 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{row.items.join(', ')}</Typography>
          )}
          {row.review.comment && <Typography sx={{ mt: 1 }}>{row.review.comment}</Typography>}
          <ReplyBox row={row} />
        </Paper>
      ))}

      {data && data.total > data.pageSize && (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 2 }}>
          <Pagination
            count={Math.ceil(data.total / data.pageSize)}
            page={data.page}
            onChange={(_, p) => setFilter(f => ({ ...f, page: p }))}
          />
        </Box>
      )}
    </Box>
  );
};

export default AdminReviewsPage;
