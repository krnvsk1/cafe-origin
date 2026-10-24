import React, { useState, useEffect, useMemo, useRef } from 'react';
import {
  Dialog,
  Typography,
  Box,
  Button,
  IconButton,
  TextField,
  Skeleton,
  useTheme,
  useMediaQuery,
  Radio,
  Checkbox,
  ButtonBase,
} from '@mui/material';
import {
  Close as CloseIcon,
  Add as AddIcon,
  Remove as RemoveIcon,
  Check as CheckIcon,
} from '@mui/icons-material';
import type { Shawarma, Addon, SelectedAddon, AddonCategory } from '../types';
import { useShawarmaAddons } from '../hooks/useAddons';
import { resolveMediaUrl } from '../utils/media';
import { tokens } from '../theme';

interface ProductModalProps {
  open: boolean;
  onClose: () => void;
  product: Shawarma | null;
  onAddToCart: (product: Shawarma, quantity: number, selectedAddons: SelectedAddon[], instructions: string, selectedChild?: Shawarma) => void;
}

// Выбор одной опции: явно одна (maxSelections = 1)
const isSingleChoice = (c: AddonCategory) => c.maxSelections === 1;

/**
 * Карточка товара: фото, выбор варианта (размера), добавки, комментарий, количество.
 * Обязательные группы подсвечиваются, если их не выбрали.
 */
const ProductModal: React.FC<ProductModalProps> = ({ open, onClose, product, onAddToCart }) => {
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
  const muted = theme.palette.mode === 'light' ? tokens.light.surfaceMuted : tokens.dark.surfaceMuted;

  const [quantity, setQuantity] = useState(1);
  // Выбранные добавки по группам: id группы → список
  const [selected, setSelected] = useState<Map<number, SelectedAddon[]>>(new Map());
  const [instructions, setInstructions] = useState('');
  const [selectedChild, setSelectedChild] = useState<Shawarma | null>(null);
  const [missing, setMissing] = useState<number[]>([]);
  const [added, setAdded] = useState(false);
  const groupRefs = useRef(new Map<number, HTMLElement>());

  // Доступные варианты, от дешёвого к дорогому
  const children = useMemo(
    () => (product?.children ?? []).filter(c => c.isAvailable).sort((a, b) => a.price - b.price),
    [product?.children]
  );
  const hasVariants = children.length > 0;

  // При открытии — сброс и выбор первого варианта
  useEffect(() => {
    if (open && product) {
      setQuantity(1);
      setSelected(new Map());
      setInstructions('');
      setMissing([]);
      setAdded(false);
      setSelectedChild(children[0] ?? null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, product?.id]);

  // Добавки зависят от варианта
  const activeProductId = selectedChild?.id ?? product?.id ?? 0;
  const { data: addonCategories = [], isLoading: addonsLoading } = useShawarmaAddons(open ? activeProductId : undefined);

  // При смене варианта добавки сбрасываются (у разных вариантов могут быть разные добавки)
  useEffect(() => {
    setSelected(new Map());
    setMissing([]);
  }, [activeProductId]);

  if (!product) return null;

  const selectedList = Array.from(selected.values()).flat();
  const addonsTotal = selectedList.reduce((sum, a) => sum + a.price * a.quantity, 0);
  const basePrice = selectedChild?.price ?? product.price;
  const totalPrice = (basePrice + addonsTotal) * quantity;

  const image = product.images?.find(i => i.isPrimary)?.filePath ?? product.images?.[0]?.filePath;
  const imageUrl = image ? resolveMediaUrl(image) : '';
  const description = product.description || selectedChild?.description || '';

  const toSelected = (addon: Addon, category: AddonCategory, qty = 1): SelectedAddon => ({
    addonId: addon.id,
    addonName: addon.name,
    price: addon.price,
    quantity: qty,
    categoryId: category.id,
    categoryName: category.name,
  });

  const toggleAddon = (addon: Addon, category: AddonCategory) => {
    const next = new Map(selected);
    const current = next.get(category.id) ?? [];
    const isOn = current.some(s => s.addonId === addon.id);

    if (isSingleChoice(category)) {
      // Радио: выбор заменяет предыдущий; повторный клик по выбранной снимает выбор, если группа не обязательная
      next.set(category.id, isOn && !category.isRequired ? [] : [toSelected(addon, category)]);
    } else if (isOn) {
      next.set(category.id, current.filter(s => s.addonId !== addon.id));
    } else {
      if (category.maxSelections > 0 && current.length >= category.maxSelections) return;
      next.set(category.id, [...current, toSelected(addon, category)]);
    }
    setSelected(next);
    setMissing(m => m.filter(id => id !== category.id));
  };

  const changeAddonQty = (addon: Addon, category: AddonCategory, delta: number) => {
    const next = new Map(selected);
    const current = [...(next.get(category.id) ?? [])];
    const i = current.findIndex(s => s.addonId === addon.id);
    if (i < 0) return;
    const qty = current[i].quantity + delta;
    if (qty < 1) {
      current.splice(i, 1);
    } else {
      if (addon.maxQuantity && qty > addon.maxQuantity) return;
      current[i] = { ...current[i], quantity: qty };
    }
    next.set(category.id, current);
    setSelected(next);
  };

  const handleAdd = () => {
    // Обязательные группы: выбрано не меньше minSelections (минимум 1)
    const notChosen = addonCategories
      .filter(c => c.isRequired && (selected.get(c.id)?.length ?? 0) < Math.max(1, c.minSelections))
      .map(c => c.id);
    if (notChosen.length > 0) {
      setMissing(notChosen);
      groupRefs.current.get(notChosen[0])?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      return;
    }

    onAddToCart(product, quantity, selectedList, instructions.trim(), selectedChild ?? undefined);
    setAdded(true);
    setTimeout(onClose, 450);
  };

  const groupHint = (c: AddonCategory) => {
    if (isSingleChoice(c)) return c.isRequired ? 'Выберите один вариант' : 'Можно выбрать один';
    if (c.isRequired) return c.minSelections > 1 ? `Выберите минимум ${c.minSelections}` : 'Выберите хотя бы одно';
    if (c.maxSelections > 0) return `Можно выбрать до ${c.maxSelections}`;
    return 'По желанию';
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      fullScreen={isMobile}
      fullWidth
      maxWidth="md"
      aria-labelledby="product-title"
      PaperProps={{ sx: { overflow: 'hidden', maxHeight: isMobile ? '100%' : 'min(820px, 92vh)' } }}
    >
      <IconButton
        onClick={onClose}
        aria-label="Закрыть"
        sx={{ position: 'absolute', top: 12, right: 12, zIndex: 2, bgcolor: 'background.paper', boxShadow: 1, '&:hover': { bgcolor: 'background.paper' } }}
      >
        <CloseIcon />
      </IconButton>

      <Box sx={{ display: 'flex', flexDirection: { xs: 'column', sm: 'row' }, minHeight: 0, flex: 1 }}>
        {/* Фото */}
        <Box
          sx={{
            flex: { sm: '0 0 44%' },
            bgcolor: muted,
            display: 'grid',
            placeItems: 'center',
            aspectRatio: { xs: '4 / 3', sm: 'auto' },
            maxHeight: { xs: '42vh', sm: 'none' },
          }}
        >
          {imageUrl ? (
            <Box component="img" src={imageUrl} alt={product.name} sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
          ) : (
            <Typography aria-hidden sx={{ fontSize: 120 }}>🌯</Typography>
          )}
        </Box>

        {/* Описание и выбор */}
        <Box sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
          <Box sx={{ overflowY: 'auto', p: { xs: 2.5, sm: 3 }, pr: { sm: 7 }, flex: 1 }}>
            <Typography id="product-title" variant="h4" component="h2" sx={{ fontSize: { xs: '1.6rem', sm: '1.9rem' }, lineHeight: 1.1 }}>
              {product.name}
            </Typography>
            {selectedChild && children.length === 1 && selectedChild.name !== 'Стандарт' && (
              <Typography color="text.secondary" sx={{ mt: 0.5 }}>{selectedChild.name}</Typography>
            )}
            {description && (
              <Typography color="text.secondary" sx={{ mt: 1.5, lineHeight: 1.5 }}>{description}</Typography>
            )}

            {/* Варианты */}
            {children.length > 1 && (
              <Box sx={{ mt: 2.5 }}>
                <Box
                  role="radiogroup"
                  aria-label="Вариант"
                  sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, p: 0.5, bgcolor: muted, borderRadius: '16px' }}
                >
                  {children.map(child => {
                    const active = selectedChild?.id === child.id;
                    return (
                      <ButtonBase
                        key={child.id}
                        role="radio"
                        aria-checked={active}
                        onClick={() => setSelectedChild(child)}
                        sx={{
                          flex: '1 1 auto',
                          minWidth: 90,
                          flexDirection: 'column',
                          px: 1.5,
                          py: 1,
                          borderRadius: '12px',
                          bgcolor: active ? 'background.paper' : 'transparent',
                          boxShadow: active ? '0 1px 4px rgba(33,32,31,0.14)' : 'none',
                          transition: 'background-color 0.15s',
                        }}
                      >
                        <Typography sx={{ fontWeight: active ? 700 : 500, fontSize: '0.92rem' }}>{child.name}</Typography>
                        <Typography variant="body2" color="text.secondary">{child.price} ₽</Typography>
                      </ButtonBase>
                    );
                  })}
                </Box>
              </Box>
            )}

            {/* Добавки */}
            {addonsLoading && <Skeleton variant="rounded" height={120} sx={{ mt: 3, borderRadius: '16px' }} />}
            {addonCategories.map(category => {
              const list = selected.get(category.id) ?? [];
              const isMissing = missing.includes(category.id);
              const single = isSingleChoice(category);
              return (
                <Box
                  key={category.id}
                  ref={(el: HTMLElement | null) => { if (el) groupRefs.current.set(category.id, el); }}
                  role={single ? 'radiogroup' : 'group'}
                  aria-label={category.name}
                  sx={{ mt: 3 }}
                >
                  <Box sx={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 1, mb: 1 }}>
                    <Typography variant="h6" sx={{ fontSize: '1.1rem', '&::first-letter': { textTransform: 'uppercase' } }}>
                      {category.name}
                    </Typography>
                    <Typography variant="body2" sx={{ color: isMissing ? 'error.main' : 'text.secondary', fontWeight: isMissing ? 700 : 400, flexShrink: 0 }}>
                      {groupHint(category)}
                    </Typography>
                  </Box>
                  <Box
                    sx={{
                      borderRadius: '16px',
                      border: '1.5px solid',
                      borderColor: isMissing ? 'error.main' : 'transparent',
                      bgcolor: muted,
                      overflow: 'hidden',
                    }}
                  >
                    {category.addons.map((addon, idx) => {
                      const sel = list.find(s => s.addonId === addon.id);
                      const limitReached = !single && !sel && category.maxSelections > 0 && list.length >= category.maxSelections;
                      const canCount = !single && !!sel && (addon.maxQuantity ?? 1) > 1;
                      return (
                        <Box
                          key={addon.id}
                          sx={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: 1,
                            px: 1,
                            borderTop: idx > 0 ? `1px solid ${theme.palette.divider}` : 'none',
                            opacity: limitReached ? 0.5 : 1,
                          }}
                        >
                          <Box
                            component="label"
                            sx={{ display: 'flex', alignItems: 'center', flex: 1, minWidth: 0, cursor: limitReached ? 'not-allowed' : 'pointer', py: 0.25 }}
                          >
                            {single ? (
                              <Radio
                                checked={!!sel}
                                onClick={() => toggleAddon(addon, category)}
                                disabled={limitReached}
                                inputProps={{ 'aria-label': addon.name }}
                              />
                            ) : (
                              <Checkbox
                                checked={!!sel}
                                onChange={() => toggleAddon(addon, category)}
                                disabled={limitReached}
                                inputProps={{ 'aria-label': addon.name }}
                              />
                            )}
                            <Typography sx={{ fontSize: '0.95rem', flex: 1, minWidth: 0 }}>{addon.name}</Typography>
                          </Box>
                          {canCount && (
                            <Box sx={{ display: 'inline-flex', alignItems: 'center', bgcolor: 'background.paper', borderRadius: '10px' }}>
                              <IconButton size="small" aria-label={`Меньше: ${addon.name}`} onClick={() => changeAddonQty(addon, category, -1)}>
                                <RemoveIcon fontSize="small" />
                              </IconButton>
                              <Typography sx={{ minWidth: 18, textAlign: 'center', fontWeight: 700, fontSize: '0.9rem' }}>{sel!.quantity}</Typography>
                              <IconButton
                                size="small"
                                aria-label={`Больше: ${addon.name}`}
                                onClick={() => changeAddonQty(addon, category, 1)}
                                disabled={!!addon.maxQuantity && sel!.quantity >= addon.maxQuantity}
                              >
                                <AddIcon fontSize="small" />
                              </IconButton>
                            </Box>
                          )}
                          <Typography sx={{ fontSize: '0.9rem', color: 'text.secondary', minWidth: 56, textAlign: 'right', pr: 1 }}>
                            {addon.price > 0 ? `+${addon.price} ₽` : 'бесплатно'}
                          </Typography>
                        </Box>
                      );
                    })}
                  </Box>
                </Box>
              );
            })}

            <TextField
              fullWidth
              multiline
              minRows={1}
              maxRows={4}
              label="Комментарий к позиции"
              placeholder="Например, без лука"
              value={instructions}
              onChange={(e) => setInstructions(e.target.value.slice(0, 200))}
              sx={{ mt: 3 }}
            />
          </Box>

          {/* Количество и добавление */}
          <Box
            sx={{
              p: { xs: 2, sm: 2.5 },
              pb: { xs: 'calc(16px + env(safe-area-inset-bottom))', sm: 2.5 },
              borderTop: `1px solid ${theme.palette.divider}`,
              display: 'flex',
              gap: 1.5,
              alignItems: 'center',
              bgcolor: 'background.paper',
            }}
          >
            <Box sx={{ display: 'inline-flex', alignItems: 'center', bgcolor: muted, borderRadius: '16px', height: 52 }}>
              <IconButton onClick={() => setQuantity(q => Math.max(1, q - 1))} disabled={quantity <= 1} aria-label="Меньше">
                <RemoveIcon />
              </IconButton>
              <Typography sx={{ minWidth: 28, textAlign: 'center', fontWeight: 700 }} aria-live="polite">{quantity}</Typography>
              <IconButton onClick={() => setQuantity(q => Math.min(99, q + 1))} aria-label="Больше">
                <AddIcon />
              </IconButton>
            </Box>
            <Button
              variant="contained"
              color="secondary"
              size="large"
              onClick={handleAdd}
              disabled={added || (hasVariants && !selectedChild)}
              sx={{ flex: 1, justifyContent: 'space-between', px: 2.5, '&.Mui-disabled': added ? { bgcolor: tokens.herb, color: '#fff' } : {} }}
            >
              {added ? (
                <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 1, mx: 'auto' }}>
                  <CheckIcon fontSize="small" /> Добавлено
                </Box>
              ) : (
                <>
                  <span>Добавить</span>
                  <span>{totalPrice.toLocaleString('ru-RU')} ₽</span>
                </>
              )}
            </Button>
          </Box>
          {missing.length > 0 && (
            <Typography role="alert" sx={{ px: 2.5, pb: 1.5, mt: -1, color: 'error.main', fontWeight: 600, fontSize: '0.9rem', bgcolor: 'background.paper' }}>
              Выберите: {addonCategories.filter(c => missing.includes(c.id)).map(c => c.name.toLowerCase()).join(', ')}
            </Typography>
          )}
        </Box>
      </Box>
    </Dialog>
  );
};

export default ProductModal;
