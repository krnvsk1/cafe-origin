import * as React from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Typography,
  Box,
  Button,
  TextField,
  List,
  ListItem,
  ListItemAvatar,
  Avatar,
  ListItemText,
  IconButton,
  Alert,
  CircularProgress,
  Snackbar,
  Slide,
  Paper,
  useTheme,
  useMediaQuery,
  ToggleButton,
  ToggleButtonGroup,
  MenuItem,
} from '@mui/material';
import type { SlideProps } from '@mui/material';
import {
  Close as CloseIcon,
  ArrowBack as ArrowBackIcon
} from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import { useCreateOrder, useRestaurantSettings } from '../api/hooks';
import { buildScheduleDays } from '../utils/schedule';
import AddressMapPicker from './AddressMapPicker';
import PhoneVerify from './PhoneVerify';
import { usePhoneRequired } from '../api/phone';
import { useAuthStore } from '../store/authStore';
import { useDeliveryZones, useDeliveryRules } from '../api/zones';
import type { ZoneCheck } from '../api/zones';
import { isMapsConfigured } from '../utils/yandexMaps';
import type { CreateOrderDto, PromoCodeValidation, PaymentMethod } from '../types';
import { useCartStore } from '../store/cartStore';
import type { DeliveryType } from '../store/orderFlowStore';
import { resolveMediaUrl } from '../utils/media';

const Transition = React.forwardRef<unknown, SlideProps>(function Transition(props, ref) {
  return <Slide direction="up" ref={ref} {...props} />;
});

interface OrderModalProps {
  open: boolean;
  onClose: () => void;
  onBackToCart: () => void;
  /** Открыть вход (для доставки нужен вход и подтверждённый телефон) */
  onLoginClick?: () => void;
  deliveryType: DeliveryType;
  address: string;
  onAddressChange: (addr: string) => void;
  customerName: string;
  onCustomerNameChange: (name: string) => void;
  phone: string;
  onPhoneChange: (phone: string) => void;
  promoInfo?: PromoCodeValidation | null;
  pointsDiscount?: number;
}

const OrderModal: React.FC<OrderModalProps> = ({
  open,
  onClose,
  onBackToCart,
  onLoginClick,
  deliveryType,
  address,
  onAddressChange,
  customerName,
  onCustomerNameChange,
  phone,
  onPhoneChange,
  promoInfo,
  pointsDiscount = 0,
}) => {
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down("sm"));

  const createOrder = useCreateOrder();
  const navigate = useNavigate();

  const cartItems = useCartStore(state => state.items);
  const clearCart = useCartStore(state => state.clearCart);

  const [notes, setNotes] = React.useState('');

  // Заказ ко времени
  const { data: settings } = useRestaurantSettings();
  const [timeMode, setTimeMode] = React.useState<'asap' | 'scheduled'>('asap');
  const [scheduledDay, setScheduledDay] = React.useState('');
  const [scheduledSlot, setScheduledSlot] = React.useState('');
  const scheduleDays = React.useMemo(
    () => (settings && open ? buildScheduleDays(settings) : []),
    [settings, open]
  );
  const isClosedNow = settings ? !settings.isOpenNow : false;

  // Оплата
  const onlineEnabled = !!settings?.onlinePaymentEnabled;
  const [paymentMethod, setPaymentMethod] = React.useState<PaymentMethod | null>(null);
  const effectivePayment: PaymentMethod = paymentMethod ?? (onlineEnabled ? 'online' : 'cash');
  const effectiveTimeMode = isClosedNow ? 'scheduled' : timeMode;
  const currentDay = scheduleDays.find(d => d.key === scheduledDay) ?? scheduleDays[0];

  React.useEffect(() => {
    // Если выбранный слот устарел — сбрасываем
    if (currentDay && scheduledSlot && !currentDay.slots.some(s => s.iso === scheduledSlot)) {
      setScheduledSlot('');
    }
  }, [currentDay, scheduledSlot]);

  const [snackbar, setSnackbar] = React.useState({
    open: false,
    message: '',
    severity: 'success' as 'success' | 'error' | 'info' | 'warning'
  });

  const totalAmount = React.useMemo(() => {
    return cartItems.reduce((sum, item) => {
      const basePrice = item.selectedChild?.price ?? item.price;
      const addonsTotal = item.selectedAddons?.reduce((s, a) => s + a.price * a.quantity, 0) || 0;
      return sum + (basePrice + addonsTotal) * item.quantity;
    }, 0);
  }, [cartItems]);

  const discountAmount = promoInfo?.discountAmount ?? 0;
  // Зоны доставки (если настроены и подключены Яндекс Карты)
  const { data: zones } = useDeliveryZones();
  const useZones = deliveryType === 'Доставка' && isMapsConfigured() && (zones?.length ?? 0) > 0;
  const [coords, setCoords] = React.useState<[number, number] | null>(null);
  const [zoneCheck, setZoneCheck] = React.useState<ZoneCheck | null>(null);
  const deliveryPrice = useZones && zoneCheck?.deliverable ? zoneCheck.deliveryPrice ?? 0 : 0;
  const zoneBlocked = useZones && (!coords || !zoneCheck?.deliverable || !zoneCheck.minOrderReached);
  // Общий минимум на доставку (без зон — проверяем здесь, с зонами его учитывает проверка адреса)
  const { data: deliveryRules } = useDeliveryRules();
  const deliveryMin = deliveryType === 'Доставка' ? deliveryRules?.minOrderAmount ?? 0 : 0;
  const belowMin = deliveryMin > 0 && totalAmount < deliveryMin;

  const finalTotal = Math.max(0, totalAmount - discountAmount - pointsDiscount) + deliveryPrice;

  // Доставка — с подтверждённым по SMS телефоном (когда SMS-сервис подключён)
  const user = useAuthStore(s => s.user);
  const { data: phoneRule } = usePhoneRequired();
  const needPhone = deliveryType === 'Доставка' && !!phoneRule?.required && !(user?.phoneVerified && user.phone);

  const showSnackbar = (message: string, severity: 'success' | 'error' | 'info' | 'warning') => {
    setSnackbar({ open: true, message, severity });
  };

  const handleSubmit = async () => {
    if (!customerName.trim()) {
      showSnackbar('Введите имя клиента', 'error');
      return;
    }
    if (needPhone) {
      showSnackbar(user ? 'Подтвердите телефон по SMS' : 'Для доставки войдите через Telegram', 'error');
      return;
    }
    if (!phone.trim()) {
      showSnackbar('Введите телефон', 'error');
      return;
    }
    if (deliveryType === 'Доставка' && !address.trim()) {
      showSnackbar('Введите адрес доставки', 'error');
      return;
    }
    if (cartItems.length === 0) {
      showSnackbar('Корзина пуста', 'error');
      return;
    }
    if (useZones && !coords) {
      showSnackbar('Отметьте адрес доставки на карте', 'error');
      return;
    }
    if (useZones && zoneCheck && !zoneCheck.deliverable) {
      showSnackbar(zoneCheck.message || 'По этому адресу доставки нет', 'error');
      return;
    }
    if (belowMin) {
      showSnackbar(`Минимальная сумма заказа на доставку — ${deliveryMin} ₽`, 'error');
      return;
    }
    if (useZones && zoneCheck && !zoneCheck.minOrderReached) {
      showSnackbar(`Минимальная сумма заказа для этого адреса — ${zoneCheck.minOrderAmount} ₽`, 'error');
      return;
    }
    if (effectiveTimeMode === 'scheduled' && !scheduledSlot) {
      showSnackbar('Выберите время заказа', 'error');
      return;
    }

    const orderData: CreateOrderDto = {
      customerName: customerName.trim(),
      phone: phone.trim(),
      address: deliveryType === 'Доставка' ? address.trim() : 'Самовывоз',
      notes: notes.trim() || null,
      deliveryType: deliveryType,
      promoCodeId: promoInfo?.promoCodeId ?? null,
      pointsDiscountAmount: pointsDiscount > 0 ? pointsDiscount : undefined,
      scheduledFor: effectiveTimeMode === 'scheduled' ? scheduledSlot : null,
      paymentMethod: effectivePayment,
      latitude: useZones && coords ? coords[0] : undefined,
      longitude: useZones && coords ? coords[1] : undefined,
      items: cartItems.map(item => ({
        dishId: item.selectedChild?.id ?? item.id,
        quantity: item.quantity,
        // У весового вес в названии добавит сервер
        name: item.weight ? item.name : item.selectedChild
          ? `${item.name} — ${item.selectedChild.name}`
          : item.name,
        weight: item.weight,
        price: item.selectedChild?.price ?? item.price,
        selectedAddons: item.selectedAddons?.map(addon => ({
          addonId: addon.addonId,
          quantity: addon.quantity
        }))
      }))
    };

    try {
      const result = await createOrder.mutateAsync(orderData);
      showSnackbar(`Заказ #${result.id} создан успешно!`, 'success');
      clearCart();
      setNotes('');
      setTimeMode('asap');
      setScheduledSlot('');
      setCoords(null);
      onCustomerNameChange('');
      onPhoneChange('');
      onAddressChange('');
      onClose();
      if (result.confirmationUrl) {
        // Переходим на страницу оплаты ЮKassa; после оплаты вернёмся на страницу заказа
        window.location.href = result.confirmationUrl;
        return;
      }
      navigate(`/order/${result.id}/success`);
    } catch (error: any) {
      showSnackbar(error?.response?.data?.message || error.message || 'Ошибка при создании заказа', 'error');
    }
  };

  const handleCloseSnackbar = () => {
    setSnackbar({ ...snackbar, open: false });
  };

  return (
    <>
      <Dialog
        open={open}
        onClose={onClose}
        maxWidth="sm"
        fullScreen={isMobile}
        fullWidth={!isMobile}
        TransitionComponent={Transition}
        PaperProps={{
          sx: {
            borderRadius: isMobile ? 0 : 2,
            bgcolor: 'background.paper',
            border: `1px solid ${theme.palette.divider}`,
            boxShadow: '0 20px 25px -5px rgba(0,0,0,0.05), 0 10px 10px -5px rgba(0,0,0,0.02)',
          }
        }}
      >
        <DialogTitle component="div" sx={{
          position: 'relative',
          py: isMobile ? 2 : 3,
          px: isMobile ? 2 : 3,
          borderBottom: `1px solid ${theme.palette.divider}`,
        }}>
          <IconButton
            onClick={onBackToCart}
            size="small"
            sx={{
              position: 'absolute',
              left: 24,
              top: '50%',
              transform: 'translateY(-50%)',
              border: `1px solid ${theme.palette.divider}`,
              borderRadius: 2,
            }}
          >
            <ArrowBackIcon />
          </IconButton>
          <Typography
            variant={isMobile ? "h6" : "h5"}
            sx={{ fontWeight: 700, letterSpacing: '-0.02em', textAlign: 'center' }}
          >
            Оформление заказа
          </Typography>
          <IconButton
            onClick={onClose}
            size="small"
            sx={{
              position: 'absolute',
              right: 24,
              top: '50%',
              transform: 'translateY(-50%)',
              border: `1px solid ${theme.palette.divider}`,
              borderRadius: 2,
            }}
          >
            <CloseIcon />
          </IconButton>
        </DialogTitle>

        <DialogContent sx={{ p: isMobile ? 2 : 3 }}>
          <Box sx={{ mb: 3 }}>
            <TextField
              fullWidth
              label="Имя клиента "
              value={customerName}
              onChange={(e) => onCustomerNameChange(e.target.value)}
              margin="normal"
              required
              disabled={createOrder.isPending}
              sx={{ '& .MuiOutlinedInput-root': { borderRadius: 3 } }}
            />
            {needPhone && !user ? (
              <Alert severity="info" sx={{ mt: 2, borderRadius: 3 }}
                action={onLoginClick ? <Button color="inherit" size="small" onClick={onLoginClick}>Войти</Button> : undefined}>
                Для доставки войдите через Telegram и подтвердите телефон.
              </Alert>
            ) : needPhone ? (
              <PhoneVerify phone={phone} onPhoneChange={onPhoneChange} disabled={createOrder.isPending} />
            ) : (
              <TextField
                fullWidth
                label="Телефон "
                type="tel"
                inputProps={{ inputMode: 'decimal', autoComplete: 'tel' }}
                value={deliveryType === 'Доставка' && phoneRule?.required ? user?.phone ?? phone : phone}
                onChange={(e) => onPhoneChange(e.target.value)}
                margin="normal"
                required
                disabled={createOrder.isPending || (deliveryType === 'Доставка' && !!phoneRule?.required)}
                helperText={deliveryType === 'Доставка' && phoneRule?.required ? 'Подтверждён по SMS' : undefined}
                sx={{ '& .MuiOutlinedInput-root': { borderRadius: 3 } }}
              />
            )}
            {deliveryType === 'Доставка' && useZones && (
              <AddressMapPicker
                address={address}
                onAddressChange={onAddressChange}
                coords={coords}
                onCoordsChange={setCoords}
                subtotal={totalAmount}
                onZoneCheck={setZoneCheck}
                disabled={createOrder.isPending}
              />
            )}
            {deliveryType === 'Доставка' && !useZones && (
              <TextField
                fullWidth
                label="Адрес доставки *"
                value={address}
                onChange={(e) => onAddressChange(e.target.value)}
                disabled={createOrder.isPending}
                placeholder="ул. Ленина, д. 1, кв. 1"
                sx={{ mt: 2, '& .MuiOutlinedInput-root': { borderRadius: 3 } }}
              />
            )}
          </Box>

          <Box sx={{ mb: 3 }}>
            <Typography variant="subtitle1" sx={{ fontWeight: 600, mb: 1 }}>
              Когда {deliveryType === 'Доставка' ? 'доставить' : 'приготовить'}
            </Typography>
            {isClosedNow && (
              <Alert severity="info" sx={{ mb: 1.5 }}>
                Сейчас мы закрыты{settings ? ` (работаем ${settings.openTime}–${settings.closeTime})` : ''}. Выберите удобное время.
              </Alert>
            )}
            <ToggleButtonGroup
              exclusive
              fullWidth
              size="small"
              value={effectiveTimeMode}
              onChange={(_, v) => v && setTimeMode(v)}
              disabled={createOrder.isPending}
              sx={{ mb: effectiveTimeMode === 'scheduled' ? 2 : 0 }}
            >
              <ToggleButton value="asap" disabled={isClosedNow}>Как можно скорее</ToggleButton>
              <ToggleButton value="scheduled">Ко времени</ToggleButton>
            </ToggleButtonGroup>
            {effectiveTimeMode === 'scheduled' && (
              scheduleDays.length === 0 ? (
                <Typography variant="body2" color="text.secondary">Нет доступного времени</Typography>
              ) : (
                <Box sx={{ display: 'flex', gap: 2 }}>
                  <TextField
                    select
                    label="День"
                    value={currentDay?.key ?? ''}
                    onChange={(e) => { setScheduledDay(e.target.value); setScheduledSlot(''); }}
                    sx={{ flex: 1, '& .MuiOutlinedInput-root': { borderRadius: 3 } }}
                  >
                    {scheduleDays.map(d => (
                      <MenuItem key={d.key} value={d.key}>{d.label}</MenuItem>
                    ))}
                  </TextField>
                  <TextField
                    select
                    label="Время"
                    value={scheduledSlot}
                    onChange={(e) => setScheduledSlot(e.target.value)}
                    sx={{ flex: 1, '& .MuiOutlinedInput-root': { borderRadius: 3 } }}
                    SelectProps={{ MenuProps: { PaperProps: { sx: { maxHeight: 320 } } } }}
                  >
                    {(currentDay?.slots ?? []).map(slot => (
                      <MenuItem key={slot.iso} value={slot.iso}>{slot.label}</MenuItem>
                    ))}
                  </TextField>
                </Box>
              )
            )}
          </Box>

          <Box sx={{ mb: 3 }}>
            <Typography variant="subtitle1" sx={{ fontWeight: 600, mb: 1 }}>Оплата</Typography>
            <ToggleButtonGroup
              exclusive
              fullWidth
              size="small"
              value={effectivePayment}
              onChange={(_, v) => v && setPaymentMethod(v)}
              disabled={createOrder.isPending}
            >
              {onlineEnabled && <ToggleButton value="online">Онлайн (карта, СБП)</ToggleButton>}
              <ToggleButton value="card">Картой при получении</ToggleButton>
              <ToggleButton value="cash">Наличными</ToggleButton>
            </ToggleButtonGroup>
            {effectivePayment === 'online' && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.75 }}>
                После оформления откроется защищённая страница оплаты ЮKassa
              </Typography>
            )}
          </Box>

          <Typography variant="h6" sx={{ fontWeight: 600, mb: 2 }}>
            Ваш заказ {cartItems.length > 0 && `(${cartItems.length})`}
          </Typography>

          {cartItems.length === 0 ? (
            <Typography color="text.secondary" align="center" sx={{ py: 4 }}>
              Корзина пуста
            </Typography>
          ) : (
            <List sx={{ mb: 3 }}>
              {cartItems.map((item) => {
                const basePrice = item.selectedChild?.price ?? item.price;
                const addonsTotal = item.selectedAddons?.reduce((s, a) => s + a.price * a.quantity, 0) || 0;
                const itemTotal = (basePrice + addonsTotal) * item.quantity;
                const displayName = item.selectedChild
                  ? `${item.name} — ${item.selectedChild.name}`
                  : item.name;

                return (
                  <Paper
                    key={item.uniqueKey || item.id}
                    elevation={0}
                    sx={{
                      mb: 1.5,
                      p: 1.5,
                      borderRadius: 4,
                      border: `1px solid ${theme.palette.divider}`,
                      bgcolor: 'background.paper',
                    }}
                  >
                    <ListItem alignItems="flex-start" disablePadding>
                      <ListItemAvatar>
                        <Avatar
                          variant="rounded"
                          sx={{
                            width: 56,
                            height: 56,
                            bgcolor: 'action.hover',
                            borderRadius: 3,
                            mr: 2,
                            border: `1px solid ${theme.palette.divider}`,
                          }}
                        >
                          {item.images?.[0]?.filePath ? (
                            <img
                              src={resolveMediaUrl(item.images[0].filePath)}
                              alt={item.name}
                              style={{
                                width: '100%',
                                height: '100%',
                                objectFit: 'cover',
                                borderRadius: 12,
                              }}
                            />
                          ) : (
                            <Typography component="span" sx={{ fontSize: '1.8rem' }}>
                              🍽️
                            </Typography>
                          )}
                        </Avatar>
                      </ListItemAvatar>
                      <ListItemText
                        primary={
                          <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
                            {displayName}
                          </Typography>
                        }
                        secondary={
                          <Box>
                            {item.selectedAddons && item.selectedAddons.length > 0 && (
                              <Box sx={{ mt: 0.5, mb: 0.5 }}>
                                {item.selectedAddons.map((addon, idx) => (
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

                            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mt: 0.5 }}>
                              <Typography variant="body2" color="text.secondary">
                                {item.quantity} × {basePrice} ₽
                              </Typography>
                              <Typography variant="body2" sx={{ fontWeight: 700, color: 'primary.main' }}>
                                {itemTotal} ₽
                              </Typography>
                            </Box>

                            {item.specialInstructions && (
                              <Typography variant="caption" color="info.main" sx={{ display: 'block', mt: 0.5 }}>
                                ✏️ {item.specialInstructions}
                              </Typography>
                            )}
                          </Box>
                        }
                      />
                    </ListItem>
                  </Paper>
                );
              })}
            </List>
          )}

          <TextField
            fullWidth
            label="Комментарий к заказу"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            disabled={createOrder.isPending}
            multiline
            rows={2}
            placeholder="Пожелания по доставке, звонок в домофон и т.д."
            sx={{ mb: 3, '& .MuiOutlinedInput-root': { borderRadius: 3 } }}
          />

          <Paper
            elevation={0}
            sx={{
              p: 2,
              borderRadius: 4,
              border: `1px solid ${theme.palette.divider}`,
              bgcolor: 'action.hover',
              mb: 2,
            }}
          >
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <Typography variant="h6" sx={{ fontWeight: 600 }}>Итого:</Typography>
              <Typography variant="h4" sx={{ color: 'primary.main', fontWeight: 800, fontSize: isMobile ? '1.5rem' : '2rem' }}>
                {finalTotal} ₽
              </Typography>
            </Box>
            {useZones && zoneCheck?.deliverable && (
              <Box sx={{ display: 'flex', justifyContent: 'space-between', mt: 1 }}>
                <Typography variant="body2" color="text.secondary">Доставка</Typography>
                <Typography variant="body2" fontWeight={600}>{deliveryPrice > 0 ? `${deliveryPrice} ₽` : 'бесплатно'}</Typography>
              </Box>
            )}
            {discountAmount > 0 && (
              <Box sx={{ display: 'flex', justifyContent: 'space-between', mt: 1 }}>
                <Typography variant="body2" color="success.main">Скидка по промокоду</Typography>
                <Typography variant="body2" fontWeight={600} color="success.main">−{discountAmount} ₽</Typography>
              </Box>
            )}
            {pointsDiscount > 0 && (
              <Box sx={{ display: 'flex', justifyContent: 'space-between', mt: 1 }}>
                <Typography variant="body2" color="warning.main">Скидка баллами</Typography>
                <Typography variant="body2" fontWeight={600} color="warning.main">−{pointsDiscount} ₽</Typography>
              </Box>
            )}
            {promoInfo && (
              <Typography variant="caption" color="success.main" sx={{ mt: 0.5, display: 'block' }}>
                🏷️ {promoInfo.code} — {promoInfo.message}
              </Typography>
            )}
          </Paper>

          {/* Убираем заглушку, так как теперь поля ввода есть */}
          {/* <Box sx={{ p: 2, bgcolor: 'action.hover', borderRadius: 3, border: `1px solid ${theme.palette.divider}` }}>
            <Typography variant="body2" color="text.secondary">
              🔐 В будущем здесь будут имя и телефон из профиля
            </Typography>
          </Box> */}
        </DialogContent>

        <DialogActions sx={{
          p: isMobile ? 2 : 3,
          borderTop: `1px solid ${theme.palette.divider}`,
          bgcolor: 'action.hover',
        }}>
          <Button
            variant="contained"
            color="secondary"
            onClick={handleSubmit}
            fullWidth
            disabled={cartItems.length === 0 || createOrder.isPending ||
              (deliveryType === 'Доставка' && !address.trim()) || zoneBlocked || belowMin ||
              !customerName.trim() || needPhone || !phone.trim()
            }
            startIcon={createOrder.isPending ? <CircularProgress size={20} color="inherit" /> : null}
            sx={{
              borderRadius: '9999px',
              py: isMobile ? 1.5 : 1.8,
              fontSize: isMobile ? '1rem' : '1.1rem',
            }}
          >
            {createOrder.isPending ? 'Создание...'
              : belowMin ? `Доставка — от ${deliveryMin} ₽, добавьте ещё ${Math.ceil(deliveryMin - totalAmount)} ₽`
              : effectivePayment === 'online' ? 'Оформить и оплатить' : 'Оформить заказ'}
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar
        open={snackbar.open}
        autoHideDuration={6000}
        onClose={handleCloseSnackbar}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          onClose={handleCloseSnackbar}
          severity={snackbar.severity}
          sx={{ width: '100%', borderRadius: 3 }}
        >
          {snackbar.message}
        </Alert>
      </Snackbar>
    </>
  );
};

export default OrderModal;
