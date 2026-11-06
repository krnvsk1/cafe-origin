import * as React from 'react';
import {
  Typography,
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  Alert,
  Grid2 as Grid,
  Chip,
  IconButton,
  Collapse,
  List,
  ListItem,
  ListItemText,
  Divider,
  Menu,
  MenuItem,
  Paper,
  TextField,
  InputAdornment,
  useTheme,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Snackbar,
  Tooltip
} from '@mui/material';
import {
  ExpandMore as ExpandMoreIcon,
  ExpandLess as ExpandLessIcon,
  MoreVert as MoreVertIcon,
  Refresh as RefreshIcon,
  Person as PersonIcon,
  Phone as PhoneIcon,
  Numbers as NumbersIcon,
  Delete as DeleteIcon,
  Edit as EditIcon,
  Replay as ReplayIcon
} from '@mui/icons-material';
import { Link } from 'react-router-dom';
import PeriodPicker, { lastDays, type Period } from '../components/PeriodPicker';
import { useOrders, useMyOrders, useUpdateOrderStatus, useDeleteOrder, useUpdateOrder } from '../api/hooks';
import { formatScheduled } from '../utils/schedule';
import { useRepeatOrder, describeRepeatResult } from '../api/reorder';
import OrderReviewBlock from '../components/OrderReviewBlock';
import { useAuthStore } from '../store/authStore';
import type { Order, OrderItem, OrderStatus, OrderItemAddon, UpdateOrderDto } from '../types';
import { plural } from '../components/CartPanel';
import { getStatusColor } from '../utils/statusColors';
import { useCan, P } from '../auth/permissions';

// Компонент для отображения статуса с цветом
const StatusChip: React.FC<{ status: OrderStatus }> = ({ status }) => {
  const theme = useTheme();
  const { bg, color } = getStatusColor(status, theme.palette.mode);

  return (
    <Chip
      label={status}
      sx={{
        bgcolor: bg,
        color: color,
        fontWeight: 500,
        minWidth: 100
      }}
    />
  );
};

// Диалог редактирования заказа
const EditOrderDialog: React.FC<{
  open: boolean;
  order: Order | null;
  onClose: () => void;
  onSave: (id: number, data: UpdateOrderDto) => void;
  loading: boolean;
}> = ({ open, order, onClose, onSave, loading }) => {
  const [formData, setFormData] = React.useState<UpdateOrderDto>({
    customerName: '',
    phone: '',
    address: '',
    notes: '',
    status: 'Новый'
  });

  React.useEffect(() => {
    if (order) {
      setFormData({
        customerName: order.customerName,
        phone: order.phone,
        address: order.address,
        notes: order.notes,
        status: order.status
      });
    }
  }, [order]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (order) {
      onSave(order.id, formData);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <form onSubmit={handleSubmit}>
        <DialogTitle>Редактировать заказ #{order?.id}</DialogTitle>
        <DialogContent>
          <TextField
            fullWidth
            label="Имя клиента"
            value={formData.customerName}
            onChange={(e) => setFormData({ ...formData, customerName: e.target.value })}
            margin="normal"
            required
          />
          <TextField
            fullWidth
            label="Телефон"
            value={formData.phone}
            type="tel"
            inputProps={{ inputMode: 'tel' }}
            onChange={(e) => setFormData({ ...formData, phone: e.target.value })}
            margin="normal"
            required
          />
          <TextField
            fullWidth
            label="Адрес"
            value={formData.address}
            onChange={(e) => setFormData({ ...formData, address: e.target.value })}
            margin="normal"
            placeholder="Оставьте пустым для самовывоза"
          />
          <TextField
            fullWidth
            label="Примечание"
            value={formData.notes || ''}
            onChange={(e) => setFormData({ ...formData, notes: e.target.value })}
            margin="normal"
            multiline
            rows={2}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Отмена</Button>
          <Button type="submit" variant="contained" disabled={loading}>
            {loading ? <CircularProgress size={24} /> : 'Сохранить'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
};

// Компонент одной карточки заказа
type OrderSource = 'online' | 'pos' | 'waiter' | 'banquet';
const SOURCE_LABEL: Record<OrderSource, string> = { online: 'Сайт', pos: 'Касса', waiter: 'Официант', banquet: 'Банкет' };
const sourceOf = (o: Order): OrderSource => (o.source === 'pos' || o.source === 'waiter' || o.source === 'banquet' ? o.source : 'online');
const rub = (v: number) => `${v.toLocaleString('ru-RU', { maximumFractionDigits: 2 })} ₽`;

// Подачи официанта на экране «Все заказы» показываем одним счётом стола: блюда всех подач, общая сумма
const STAGE: OrderStatus[] = ['Новый', 'Принят', 'Готовится', 'Готов', 'В пути', 'Доставлен', 'Выполнен'];
const lineTotal = (i: OrderItem) => (i.price + (i.selectedAddons ?? []).reduce((s, a) => s + a.price * a.quantity, 0)) * i.quantity;

const groupByCheck = (list: Order[]): Order[] => {
  const result: Order[] = [];
  const checks = new Map<number, Order[]>();
  for (const o of list) {
    if (sourceOf(o) !== 'waiter' || o.checkId == null) { result.push(o); continue; }
    // Блюдо могли перенести в другой счёт — раскладываем по счёту блюда
    const byCheck = new Map<number, OrderItem[]>();
    for (const it of o.orderItems ?? []) {
      const c = it.checkId ?? o.checkId;
      if (!byCheck.has(c)) byCheck.set(c, []);
      byCheck.get(c)!.push(it);
    }
    if (byCheck.size === 0) byCheck.set(o.checkId, []);
    byCheck.forEach((items, c) => {
      if (!checks.has(c)) checks.set(c, []);
      checks.get(c)!.push({ ...o, checkId: c, orderItems: items });
    });
  }
  checks.forEach((rounds, checkId) => {
    rounds.sort((a, b) => a.createdAt.localeCompare(b.createdAt));
    const live = rounds.filter(r => r.status !== 'Отменён');
    const status: OrderStatus = live.length === 0 ? 'Отменён'
      : STAGE[Math.min(...live.map(r => Math.max(0, STAGE.indexOf(r.status))))];
    const items = live.flatMap(r => r.orderItems ?? []);
    const first = rounds[0];
    const last = rounds[rounds.length - 1];
    result.push({
      ...first,
      checkId,
      status,
      orderItems: items,
      total: Math.round(items.reduce((sum, it) => sum + lineTotal(it), 0) * 100) / 100,
      createdAt: first.createdAt,
      completedAt: live.every(r => r.completedAt) ? live.map(r => r.completedAt!).sort().pop() ?? null : null,
      notes: rounds.map(r => r.notes).filter(Boolean).join('; ') || null,
      rounds,
      // В списке счёт стоит там же, где его последняя подача
      id: last.id,
    });
  });
  return result.sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id - a.id);
};

const OrderCard: React.FC<{ 
  order: Order;
  isAdmin: boolean;
  onDelete: (id: number) => void;
  onEdit: (order: Order) => void;
  onRepeatOrder: (order: Order) => void;
}> = ({ order, isAdmin, onDelete, onEdit, onRepeatOrder }) => {
  const canManage = useCan()(P.OrdersManage);
  const [expanded, setExpanded] = React.useState(false);
  const [anchorEl, setAnchorEl] = React.useState<null | HTMLElement>(null);
  const updateStatus = useUpdateOrderStatus();

  const handleStatusChange = async (newStatus: OrderStatus) => {
    try {
      await updateStatus.mutateAsync({
        id: order.id,
        status: { status: newStatus }
      });
    } catch (error) {
      console.error('Ошибка обновления статуса:', error);
    }
    setAnchorEl(null);
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleString('ru-RU', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  return (
    <Card sx={{ mb: 2 }}>
      <CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
          <Box sx={{ flex: 1 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 1 }}>
              <Typography variant="h6" component="span">
                {order.rounds ? `Счёт #${order.checkId}` : `Заказ #${order.id}`}
              </Typography>
              <StatusChip status={order.status} />
              <Chip size="small" variant="outlined" label={SOURCE_LABEL[sourceOf(order)]} />
            </Box>

            <Grid container spacing={2}>
              <Grid size={{ xs: 12, sm: 6 }}>
                {sourceOf(order) === 'waiter' ? (
                  <Typography variant="body2" color="text.secondary">
                    <strong>{order.customerName}</strong> · в зале
                    {order.rounds && ` · ${order.rounds.length} ${plural(order.rounds.length, ['подача', 'подачи', 'подач'])} на кухню`}
                  </Typography>
                ) : (
                  <>
                    <Typography variant="body2" color="text.secondary">
                      Клиент: <strong>{order.customerName}</strong>
                    </Typography>
                    {order.phone && (
                      <Typography variant="body2" color="text.secondary">
                        Телефон: <strong>{order.phone}</strong>
                      </Typography>
                    )}
                    <Typography variant="body2" color="text.secondary">
                      {order.deliveryType === 'Доставка' || (!order.deliveryType && order.address) ? 'Адрес' : 'Получение'}: <strong>{order.address || 'Самовывоз'}</strong>
                    </Typography>
                  </>
                )}
              </Grid>
              <Grid size={{ xs: 12, sm: 6 }}>
                <Typography variant="body2" color="text.secondary">
                  Создан: <strong>{formatDate(order.createdAt)}</strong>
                </Typography>
                {order.paymentMethod === 'online' && (
                  <Typography variant="body2" color={order.paymentStatus === 'paid' ? 'success.main' : 'warning.main'}>
                    Онлайн-оплата: <strong>{order.paymentStatus === 'paid' ? 'оплачен' : order.paymentStatus === 'refunded' ? 'возврат' : 'не оплачен'}</strong>
                  </Typography>
                )}
                {order.scheduledFor && (
                  <Typography variant="body2" color="warning.main">
                    Ко времени: <strong>{formatScheduled(order.scheduledFor)}</strong>
                  </Typography>
                )}
                {order.completedAt && (
                  <Typography variant="body2" color="text.secondary">
                    Завершён: <strong>{formatDate(order.completedAt)}</strong>
                  </Typography>
                )}
                {order.notes && (
                  <Typography variant="body2" color="text.secondary">
                    Примечание: <strong>{order.notes}</strong>
                  </Typography>
                )}
              </Grid>
            </Grid>
          </Box>

          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', ml: 2 }}>
            <Typography 
              variant="h5" 
              gutterBottom
              sx={{ 
                color: 'primary.main',
                fontWeight: 'bold'
              }}
            >
              {rub(order.total)}
            </Typography>
            
            <Box sx={{ display: 'flex', gap: 1 }}>
              <IconButton 
                size="small" 
                onClick={() => setExpanded(!expanded)}
                title={expanded ? "Скрыть состав" : "Показать состав"}
              >
                {expanded ? <ExpandLessIcon /> : <ExpandMoreIcon />}
              </IconButton>
              
              {/* Кнопки админа */}
              {!isAdmin && (
                <Tooltip title="Повторить заказ">
                  <IconButton
                    size="small"
                    onClick={() => onRepeatOrder(order)}
                    color="success"
                  >
                    <ReplayIcon />
                  </IconButton>
                </Tooltip>
              )}

              {isAdmin && canManage && !order.rounds && (
                <>
                  <Tooltip title="Редактировать заказ">
                    <IconButton 
                      size="small" 
                      onClick={() => onEdit(order)}
                      color="primary"
                    >
                      <EditIcon />
                    </IconButton>
                  </Tooltip>
                  
                  <Tooltip title="Удалить заказ">
                    <IconButton 
                      size="small" 
                      onClick={() => onDelete(order.id)}
                      color="error"
                    >
                      <DeleteIcon />
                    </IconButton>
                  </Tooltip>

                  <IconButton 
                    size="small" 
                    onClick={(e) => setAnchorEl(e.currentTarget)}
                    title="Изменить статус"
                    disabled={updateStatus.isPending}
                  >
                    <MoreVertIcon />
                  </IconButton>

                  <Menu
                    anchorEl={anchorEl}
                    open={Boolean(anchorEl)}
                    onClose={() => setAnchorEl(null)}
                  >
                    {['Новый', 'Принят', 'Готовится', 'Готов', 'В пути', 'Доставлен', 'Отменён'].map((status) => (
                      <MenuItem 
                        key={status} 
                        onClick={() => handleStatusChange(status as OrderStatus)}
                        selected={order.status === status}
                      >
                        {status}
                      </MenuItem>
                    ))}
                  </Menu>
                </>
              )}
            </Box>
          </Box>
        </Box>

        {!isAdmin && <OrderReviewBlock order={order} />}

        <Collapse in={expanded}>
          <Box sx={{ mt: 3 }}>
            {order.rounds && (
              <Box sx={{ mb: 1.5 }}>
                <Typography variant="subtitle2" gutterBottom>Подачи на кухню:</Typography>
                {order.rounds.map(r => (
                  <Typography key={r.id} variant="body2" color="text.secondary">
                    #{r.id} · {formatDate(r.createdAt)} · {r.status} · {(r.orderItems ?? []).map(i => `${i.quantity}× ${i.name}`).join(', ') || '—'}
                  </Typography>
                ))}
              </Box>
            )}
            <Typography variant="subtitle2" gutterBottom>
              {order.rounds ? 'Состав счёта:' : 'Состав заказа:'}
            </Typography>
            <Paper 
              variant="outlined" 
              sx={{ 
                bgcolor: 'action.hover'
              }}
            >
              <List dense>
              {(order.orderItems || []).map((item, index) => (
                <React.Fragment key={item.id}>
                  <ListItem>
                    <ListItemText
                      primary={item.name}
                      secondary={
                        <Box>
                          <Typography variant="body2" color="text.secondary">
                            {item.quantity} × {item.price} ₽
                          </Typography>
                          
                          {/* Добавки под товаром */}
                          {item.selectedAddons && item.selectedAddons.length > 0 && (
                            <Box sx={{ mt: 0.5, ml: 1 }}>
                              {item.selectedAddons.map((addon: OrderItemAddon, idx: number) => (
                                <Typography 
                                  key={idx} 
                                  variant="caption" 
                                  display="block" 
                                  color="text.secondary"
                                  sx={{ lineHeight: 1.4 }}
                                >
                                  • {addon.addonName} {addon.quantity > 1 ? `×${addon.quantity}` : ''} +{addon.price * addon.quantity} ₽
                                </Typography>
                              ))}
                            </Box>
                          )}
                        </Box>
                      }
                    />
                    <Typography variant="body2" fontWeight="500">
                      {item.subtotal} ₽
                    </Typography>
                  </ListItem>
                  {index < (order.orderItems || []).length - 1 && <Divider />}
                </React.Fragment>
              ))}
              <Divider />
              <ListItem>
                <ListItemText primary="Итого" />
                <Typography 
                  variant="subtitle1" 
                  fontWeight="bold" 
                  sx={{ color: 'primary.main' }}
                >
                  {order.total} ₽
                </Typography>
              </ListItem>
            </List>
            </Paper>
          </Box>
        </Collapse>
      </CardContent>
    </Card>
  );
};

// Основной компонент страницы
const OrdersPage: React.FC = () => {
  const theme = useTheme();
  const { isAuthenticated, user } = useAuthStore();
  const can = useCan();
  const isAdmin = isAuthenticated && can(P.OrdersView, P.OrdersManage, P.Kitchen, P.Delivery);
  
  // Используем разные хуки в зависимости от роли
  const [period, setPeriod] = React.useState<Period>(() => lastDays(7));
  const allOrdersQuery = useOrders(period, isAdmin);
  const myOrdersQuery = useMyOrders(!isAdmin && isAuthenticated);
  
  // Выбираем данные в зависимости от роли
  // Подачи официанта — одним счётом стола
  const orders = React.useMemo(
    () => groupByCheck(isAdmin ? allOrdersQuery.data || [] : myOrdersQuery.data || []),
    [isAdmin, allOrdersQuery.data, myOrdersQuery.data],
  );
  const isLoading = isAdmin ? allOrdersQuery.isLoading : myOrdersQuery.isLoading;
  const error = isAdmin ? allOrdersQuery.error : myOrdersQuery.error;
  const refetch = isAdmin ? allOrdersQuery.refetch : myOrdersQuery.refetch;
  
  // Мутации
  const deleteOrder = useDeleteOrder();
  const updateOrder = useUpdateOrder();
  
  // Повтор заказа: актуальные цены и наличие берутся с сервера
  const repeatOrder = useRepeatOrder();

  const handleRepeatOrder = async (order: Order) => {
    try {
      const result = await repeatOrder.mutateAsync(order.id);
      const { message, severity } = describeRepeatResult(order.id, result);
      setSnackbar({ open: true, message, severity });
    } catch {
      setSnackbar({ open: true, message: 'Не удалось повторить заказ', severity: 'error' });
    }
  };

  // Состояния для диалогов
  const [editDialog, setEditDialog] = React.useState<{
    open: boolean;
    order: Order | null;
  }>({ open: false, order: null });
  
  const [snackbar, setSnackbar] = React.useState({
    open: false,
    message: '',
    severity: 'success' as 'success' | 'error' | 'warning'
  });

  const [statusFilter, setStatusFilter] = React.useState<OrderStatus | 'all'>('all');
  const [sourceFilter, setSourceFilter] = React.useState<OrderSource | 'all'>('all');
  const [searchId, setSearchId] = React.useState('');
  const [searchName, setSearchName] = React.useState('');
  const [searchPhone, setSearchPhone] = React.useState('');

  // Обработчики
  const handleDeleteOrder = async (id: number) => {
    if (window.confirm(`Удалить заказ #${id}? Это действие нельзя отменить.`)) {
      try {
        await deleteOrder.mutateAsync(id);
        setSnackbar({ open: true, message: 'Заказ удалён', severity: 'success' });
      } catch (error) {
        setSnackbar({ open: true, message: 'Ошибка при удалении заказа', severity: 'error' });
      }
    }
  };

  const handleEditOrder = (order: Order) => {
    setEditDialog({ open: true, order });
  };

  const handleSaveEdit = async (id: number, data: UpdateOrderDto) => {
    try {
      await updateOrder.mutateAsync({ id, data });
      setSnackbar({ open: true, message: 'Заказ обновлён', severity: 'success' });
      setEditDialog({ open: false, order: null });
    } catch (error) {
      setSnackbar({ open: true, message: 'Ошибка при обновлении заказа', severity: 'error' });
    }
  };

  // Фильтрация заказов
  const filteredOrders = React.useMemo(() => {
    let filtered = orders;
    
    // Для обычного пользователя показываем только его заказы
    // (фильтрация на клиенте по userId, если сервер вернул все заказы)
    if (!isAdmin && user) {
      filtered = orders.filter(order => order.userId === user.id);
    }
    
    return filtered.filter(order => {
      if (statusFilter !== 'all' && order.status !== statusFilter) return false;
      if (sourceFilter !== 'all' && sourceOf(order) !== sourceFilter) return false;
      if (searchId && !order.id.toString().includes(searchId)) return false;
      if (searchName && !order.customerName.toLowerCase().includes(searchName.toLowerCase())) return false;
      if (searchPhone && !order.phone.includes(searchPhone)) return false;
      return true;
    });
  }, [orders, statusFilter, sourceFilter, searchId, searchName, searchPhone, isAdmin, user]);

  const stats = React.useMemo(() => {
    const total = orders.reduce((sum, order) => sum + order.total, 0);
    const newCount = orders.filter(o => o.status === 'Новый').length;
    const preparingCount = orders.filter(o => o.status === 'Готовится').length;
    const readyCount = orders.filter(o => o.status === 'Готов').length;
    return { total, newCount, preparingCount, readyCount };
  }, [orders]);

  const handleClearSearch = () => {
    setSearchId('');
    setSearchName('');
    setSearchPhone('');
  };

  const isSearchActive = searchId || searchName || searchPhone;

  if (error) {
    return (
      <Alert 
        severity="error" 
        sx={{ mt: 2 }}
        action={
          <Button color="inherit" size="small" onClick={() => refetch()}>
            Повторить
          </Button>
        }
      >
        Ошибка загрузки заказов: {error.message}
      </Alert>
    );
  }

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
        <Typography variant="h4" component="h1">
          {isAdmin ? 'Все заказы' : 'Мои заказы'}
        </Typography>
        <Box sx={{ display: 'flex', gap: 2 }}>
          <Button
            variant="outlined"
            startIcon={<RefreshIcon />}
            onClick={() => refetch()}
            disabled={isLoading}
          >
            Обновить
          </Button>
          {/* Кнопка нового заказа — только для покупателя */}
          {!isAdmin && (
            <Button
              variant="contained"
              component={Link}
              to="/order"
              sx={{
                bgcolor: 'primary.main',
                '&:hover': { bgcolor: 'primary.dark' }
              }}
            >
              Новый заказ
            </Button>
          )}
        </Box>
      </Box>

      {/* Фильтры - только для админов */}
      {isAdmin && (
        <Paper sx={{ p: 3, mb: 3 }}>
          <Box sx={{ mb: 2 }}>
            <PeriodPicker value={period} onChange={setPeriod} />
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.75 }}>
              Заказы за выбранные дни; заказы в работе видны всегда
            </Typography>
          </Box>
          <Grid container spacing={2} alignItems="center">
            <Grid size={{ xs: 12, sm: 6, md: 3 }}>
              <TextField
                fullWidth
                variant="outlined"
                label="Поиск по номеру"
                placeholder="Например: 123"
                value={searchId}
                onChange={(e) => setSearchId(e.target.value)}
                InputProps={{
                  startAdornment: (
                    <InputAdornment position="start">
                      <NumbersIcon />
                    </InputAdornment>
                  ),
                }}
                size="small"
              />
            </Grid>
            <Grid size={{ xs: 12, sm: 6, md: 3 }}>
              <TextField
                fullWidth
                variant="outlined"
                label="Поиск по имени"
                placeholder="Имя клиента"
                value={searchName}
                onChange={(e) => setSearchName(e.target.value)}
                InputProps={{
                  startAdornment: (
                    <InputAdornment position="start">
                      <PersonIcon />
                    </InputAdornment>
                  ),
                }}
                size="small"
              />
            </Grid>
            <Grid size={{ xs: 12, sm: 6, md: 3 }}>
              <TextField
                fullWidth
                variant="outlined"
                label="Поиск по телефону"
                inputProps={{ inputMode: 'tel' }}
                placeholder="+7..."
                value={searchPhone}
                onChange={(e) => setSearchPhone(e.target.value)}
                InputProps={{
                  startAdornment: (
                    <InputAdornment position="start">
                      <PhoneIcon />
                    </InputAdornment>
                  ),
                }}
                size="small"
              />
            </Grid>
            <Grid size={{ xs: 12, sm: 6, md: 3 }}>
              <Button
                fullWidth
                variant="outlined"
                onClick={handleClearSearch}
                disabled={!isSearchActive}
                sx={{ height: '40px' }}
              >
                Сбросить фильтры
              </Button>
            </Grid>
          </Grid>
        </Paper>
      )}

      {/* Статистика - только для админов */}
      {!isLoading && orders.length > 0 && isAdmin && (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, 1fr)', sm: 'repeat(3, 1fr)', md: 'repeat(5, 1fr)' }, gap: 2, mb: 3 }}>
          <Paper sx={{ p: 2, minWidth: 120 }}>
            <Typography variant="body2" color="text.secondary">Всего заказов</Typography>
            <Typography variant="h6">{orders.length}</Typography>
          </Paper>
          <Paper sx={{ p: 2, minWidth: 120, bgcolor: theme.palette.mode === 'light' ? '#e3f2fd' : '#1e3a5f' }}>
            <Typography variant="body2" color="text.secondary">Новые</Typography>
            <Typography variant="h6" sx={{ color: theme.palette.mode === 'light' ? '#1976d2' : '#90caf9' }}>
              {stats.newCount}
            </Typography>
          </Paper>
          <Paper sx={{ p: 2, minWidth: 120, bgcolor: theme.palette.mode === 'light' ? '#fff3e0' : '#663c00' }}>
            <Typography variant="body2" color="text.secondary">Готовятся</Typography>
            <Typography variant="h6" sx={{ color: theme.palette.mode === 'light' ? '#f57c00' : '#ffb74d' }}>
              {stats.preparingCount}
            </Typography>
          </Paper>
          <Paper sx={{ p: 2, minWidth: 120, bgcolor: theme.palette.mode === 'light' ? '#e8f5e9' : '#1b5e20' }}>
            <Typography variant="body2" color="text.secondary">Готовы</Typography>
            <Typography variant="h6" sx={{ color: theme.palette.mode === 'light' ? '#2e7d32' : '#81c784' }}>
              {stats.readyCount}
            </Typography>
          </Paper>
          <Paper sx={{ p: 2, minWidth: 120 }}>
            <Typography variant="body2" color="text.secondary">Выручка</Typography>
            <Typography variant="h6" sx={{ color: 'primary.main' }}>
              {stats.total.toLocaleString('ru-RU')} ₽
            </Typography>
          </Paper>
        </Box>
      )}

      {/* Откуда заказ */}
      {!isLoading && isAdmin && orders.length > 0 && (
        <Box sx={{ display: 'flex', gap: 1, mb: 1, flexWrap: 'wrap', alignItems: 'center' }}>
          <Typography variant="body2" color="text.secondary" sx={{ mr: 0.5 }}>Откуда:</Typography>
          {(['all', 'online', 'pos', 'waiter', 'banquet'] as const).map(src => (
            <Chip
              key={src}
              label={src === 'all' ? 'Все' : `${SOURCE_LABEL[src]} · ${orders.filter(o => sourceOf(o) === src).length}`}
              onClick={() => setSourceFilter(src)}
              color={sourceFilter === src ? 'primary' : 'default'}
              variant={sourceFilter === src ? 'filled' : 'outlined'}
            />
          ))}
        </Box>
      )}

      {/* Фильтр по статусам */}
      {!isLoading && orders.length > 0 && (
        <Box sx={{ display: 'flex', gap: 1, mb: 2, flexWrap: 'wrap' }}>
          <Chip
            label="Все"
            onClick={() => setStatusFilter('all')}
            color={statusFilter === 'all' ? 'primary' : 'default'}
          />
          {['Новый', 'Принят', 'Готовится', 'Готов', 'В пути', 'Доставлен', 'Выполнен', 'Отменён'].map(status => (
            <Chip
              key={status}
              label={status}
              onClick={() => setStatusFilter(status as OrderStatus)}
              color={statusFilter === status ? 'primary' : 'default'}
              variant={statusFilter === status ? 'filled' : 'outlined'}
            />
          ))}
        </Box>
      )}

      {isSearchActive && (
        <Box sx={{ mb: 2, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Typography variant="body2" color="text.secondary">
            Найдено заказов: {filteredOrders.length}
          </Typography>
          {filteredOrders.length === 0 && (
            <Button size="small" onClick={handleClearSearch}>
              Сбросить поиск
            </Button>
          )}
        </Box>
      )}

      {isLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
          <CircularProgress />
          <Typography sx={{ ml: 2 }}>Загрузка заказов...</Typography>
        </Box>
      ) : filteredOrders.length > 0 ? (
        <Box>
          {filteredOrders.map((order) => (
            <OrderCard 
              key={order.id} 
              order={order} 
              isAdmin={isAdmin}
              onDelete={handleDeleteOrder}
              onEdit={handleEditOrder}
              onRepeatOrder={handleRepeatOrder}
            />
          ))}
        </Box>
      ) : (
        <Alert severity="info" sx={{ mt: 2 }}>
          {isSearchActive 
            ? 'Ничего не найдено по заданным критериям' 
            : statusFilter !== 'all' 
              ? `Нет заказов со статусом "${statusFilter}"`
              : isAdmin 
                ? 'Заказов пока нет.' 
                : isAuthenticated 
                  ? 'У вас пока нет заказов. Оформите свой первый заказ!'
                  : 'Войдите в аккаунт, чтобы видеть свои заказы'}
        </Alert>
      )}

      {!isLoading && filteredOrders.length > 0 && (
        <Box sx={{ mt: 3, textAlign: 'center' }}>
          <Typography variant="body2" color="text.secondary">
            Показано {filteredOrders.length} из {orders.length} заказов
          </Typography>
        </Box>
      )}

      {/* Диалог редактирования */}
      <EditOrderDialog
        open={editDialog.open}
        order={editDialog.order}
        onClose={() => setEditDialog({ open: false, order: null })}
        onSave={handleSaveEdit}
        loading={updateOrder.isPending}
      />

      {/* Уведомления */}
      <Snackbar
        open={snackbar.open}
        autoHideDuration={4000}
        onClose={() => setSnackbar({ ...snackbar, open: false })}
      >
        <Alert severity={snackbar.severity} sx={{ width: '100%' }}>
          {snackbar.message}
        </Alert>
      </Snackbar>
    </Box>
  );
};

export default OrdersPage;