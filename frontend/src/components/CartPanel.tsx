import * as React from 'react';
import { Box, Typography, Button, IconButton, Divider } from '@mui/material';
import { Add as AddIcon, Remove as RemoveIcon, ShoppingBagOutlined as BagIcon } from '@mui/icons-material';
import { useCartStore, useTotalItems, useTotalPrice } from '../store/cartStore';
import { useOrderFlowStore } from '../store/orderFlowStore';
import type { CartItem } from '../types';

import { useMinOrder } from '../api/zones';

const lineTotal = (item: CartItem) => {
  const base = item.selectedChild?.price ?? item.price;
  const addons = item.selectedAddons?.reduce((s, a) => s + a.price * a.quantity, 0) ?? 0;
  return (base + addons) * item.quantity;
};

/** Счётчик количества: − 2 + */
export const QuantityStepper: React.FC<{ value: number; onChange: (v: number) => void; size?: 'small' | 'medium' }> = ({ value, onChange, size = 'small' }) => (
  <Box
    sx={{
      display: 'inline-flex',
      alignItems: 'center',
      bgcolor: 'action.hover',
      borderRadius: '12px',
      height: size === 'small' ? 32 : 40,
    }}
  >
    <IconButton size="small" aria-label="Меньше" onClick={() => onChange(value - 1)}>
      <RemoveIcon fontSize="small" />
    </IconButton>
    <Typography sx={{ minWidth: 20, textAlign: 'center', fontWeight: 700, fontSize: '0.9rem' }}>{value}</Typography>
    <IconButton size="small" aria-label="Больше" onClick={() => onChange(value + 1)}>
      <AddIcon fontSize="small" />
    </IconButton>
  </Box>
);

/**
 * Корзина справа от меню (на широких экранах), как в сервисах доставки:
 * состав с изменением количества, сумма и кнопка оформления.
 */
const CartPanel: React.FC = () => {
  const items = useCartStore(s => s.items);
  const updateQuantity = useCartStore(s => s.updateQuantity);
  const clearCart = useCartStore(s => s.clearCart);
  const totalItems = useTotalItems();
  const totalPrice = useTotalPrice();
  const openCart = useOrderFlowStore(s => s.openCart);
  const minOrder = useMinOrder(useOrderFlowStore(s => s.deliveryType));
  const left = Math.max(0, minOrder - totalPrice);

  return (
    <Box
      component="aside"
      aria-label="Корзина"
      sx={{
        bgcolor: 'background.paper',
        borderRadius: '24px',
        p: 2.5,
        display: 'flex',
        flexDirection: 'column',
        maxHeight: 'calc(100vh - 120px)',
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', mb: 1.5 }}>
        <Typography variant="h5">Корзина</Typography>
        {items.length > 0 && (
          <Button size="small" onClick={clearCart} sx={{ color: 'text.secondary', minHeight: 0, px: 1 }}>
            Очистить
          </Button>
        )}
      </Box>

      {items.length === 0 ? (
        <Box sx={{ textAlign: 'center', py: 5, color: 'text.secondary' }}>
          <BagIcon sx={{ fontSize: 48, opacity: 0.4 }} />
          <Typography sx={{ mt: 1, fontWeight: 600, color: 'text.primary' }}>Здесь пока пусто</Typography>
          <Typography variant="body2">Добавьте блюда из меню — они появятся здесь</Typography>
        </Box>
      ) : (
        <>
          <Box sx={{ overflowY: 'auto', mx: -1, px: 1, flex: 1 }}>
            {items.map((item, idx) => (
              <Box key={item.uniqueKey ?? idx}>
                {idx > 0 && <Divider sx={{ my: 1.5 }} />}
                <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start' }}>
                  <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Typography sx={{ fontWeight: 600, fontSize: '0.92rem', lineHeight: 1.3 }}>
                      {item.name}
                    </Typography>
                    {(item.selectedChild || (item.selectedAddons?.length ?? 0) > 0) && (
                      <Typography variant="body2" color="text.secondary" sx={{ fontSize: '0.8rem' }}>
                        {[item.selectedChild?.name, ...(item.selectedAddons ?? []).map(a => (a.quantity > 1 ? `${a.addonName} ×${a.quantity}` : a.addonName))]
                          .filter(Boolean)
                          .join(', ')}
                      </Typography>
                    )}
                    <Typography sx={{ fontWeight: 700, fontSize: '0.92rem', mt: 0.5 }}>{lineTotal(item)} ₽</Typography>
                  </Box>
                  <QuantityStepper
                    value={item.quantity}
                    onChange={(v) => item.uniqueKey && updateQuantity(item.uniqueKey, v)}
                  />
                </Box>
              </Box>
            ))}
          </Box>

          <Divider sx={{ my: 2 }} />
          {left > 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
              До минимальной суммы заказа не хватает {left} ₽
            </Typography>
          )}
          <Button
            variant="contained"
            color="secondary"
            size="large"
            fullWidth
            onClick={openCart}
            disabled={left > 0}
            sx={{ justifyContent: 'space-between', px: 2.5 }}
          >
            <span>Оформить</span>
            <span>{totalPrice.toLocaleString('ru-RU')} ₽</span>
          </Button>
          <Typography variant="caption" color="text.secondary" sx={{ mt: 1, textAlign: 'center' }}>
            {totalItems} {plural(totalItems, ['позиция', 'позиции', 'позиций'])}
          </Typography>
        </>
      )}
    </Box>
  );
};

export const plural = (n: number, forms: [string, string, string]) => {
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 === 1 && mod100 !== 11) return forms[0];
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return forms[1];
  return forms[2];
};

/** Полоса корзины внизу экрана на телефоне */
export const MobileCartBar: React.FC = () => {
  const totalItems = useTotalItems();
  const totalPrice = useTotalPrice();
  const openCart = useOrderFlowStore(s => s.openCart);
  if (totalItems === 0) return null;

  return (
    <Box
      sx={{
        position: 'fixed',
        left: 0,
        right: 0,
        bottom: 0,
        p: 1.5,
        pb: 'calc(12px + env(safe-area-inset-bottom))',
        zIndex: (t) => t.zIndex.appBar,
        background: (t) => `linear-gradient(to top, ${t.palette.background.default} 70%, transparent)`,
      }}
    >
      <Button
        variant="contained"
        color="secondary"
        size="large"
        fullWidth
        onClick={openCart}
        sx={{ justifyContent: 'space-between', px: 2.5, boxShadow: '0 8px 24px rgba(33,32,31,0.18)' }}
      >
        <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 1 }}>
          <BagIcon fontSize="small" />
          {totalItems} {plural(totalItems, ['позиция', 'позиции', 'позиций'])}
        </Box>
        <span>{totalPrice.toLocaleString('ru-RU')} ₽</span>
      </Button>
    </Box>
  );
};

export default CartPanel;
