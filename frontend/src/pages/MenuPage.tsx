import React, { useState, useMemo } from 'react';
import {
  Typography,
  Box,
  Alert,
  Chip,
  TextField,
  InputAdornment,
  Button,
  Skeleton,
  useTheme,
  useMediaQuery,
  IconButton,
} from '@mui/material';
import {
  Search as SearchIcon,
  Add as AddIcon,
  Close as CloseIcon,
  Favorite as FavoriteIcon,
  AccessTimeRounded as TimeIcon,
  CreditCardRounded as CardIcon,
  StorefrontRounded as PickupIcon,
} from '@mui/icons-material';
import { Link } from 'react-router-dom';
import MenuItemCard from '../components/MenuItemCard';
import RepeatLastOrderBanner from '../components/RepeatLastOrderBanner';
import CartPanel, { MobileCartBar } from '../components/CartPanel';
import { useFavoriteIds, useToggleFavorite } from '../api/favorites';
import { useShawarmas, useRestaurantSettings } from '../api/hooks';
import type { Shawarma, SelectedAddon } from '../types';
import ProductModal from '../components/ProductModal';
import { useCartStore } from '../store/cartStore';
import { useAuthStore } from '../store/authStore';
import { tokens } from '../theme';
import { useCan, P } from '../auth/permissions';

// Сетка карточек: 2 колонки на телефоне, 3 на планшете и ноутбуке, 4 на широком экране
const gridSx = {
  display: 'grid',
  gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', sm: 'repeat(3, minmax(0, 1fr))', xl: 'repeat(4, minmax(0, 1fr))' },
  gap: { xs: 1.25, sm: 2 },
} as const;

/** «Обложка» заведения — как карточка ресторана в сервисах доставки */
const RestaurantHero: React.FC = () => {
  const { data: settings } = useRestaurantSettings();
  const facts: { icon: React.ReactNode; text: string }[] = [
    { icon: <TimeIcon fontSize="small" />, text: 'Доставка 30–45 мин' },
    { icon: <PickupIcon fontSize="small" />, text: 'Самовывоз и в зале' },
  ];
  if (settings?.onlinePaymentEnabled) facts.push({ icon: <CardIcon fontSize="small" />, text: 'Оплата онлайн' });

  return (
    <Box
      component="section"
      sx={{
        position: 'relative',
        overflow: 'hidden',
        borderRadius: { xs: '24px', md: '32px' },
        bgcolor: tokens.saffron,
        color: '#21201F',
        px: { xs: 2.5, md: 5 },
        py: { xs: 3, md: 5 },
        mb: { xs: 2, md: 3 },
      }}
    >
      {/* Крупный силуэт шаурмы справа — главный графический акцент страницы */}
      <Typography
        aria-hidden
        sx={{
          position: 'absolute',
          right: { xs: -24, md: 32 },
          bottom: { xs: -36, md: -48 },
          fontSize: { xs: 150, md: 240 },
          lineHeight: 1,
          transform: 'rotate(-18deg)',
          userSelect: 'none',
          pointerEvents: 'none',
        }}
      >
        🌯
      </Typography>

      <Typography
        component="h1"
        sx={{
          fontWeight: 900,
          fontSize: { xs: '2.25rem', sm: '3rem', md: '4rem' },
          lineHeight: 0.95,
          letterSpacing: '-0.045em',
          maxWidth: { xs: '75%', md: '60%' },
        }}
      >
        Кума Шаурма
      </Typography>
      <Typography sx={{ mt: 1.5, fontSize: { xs: '1rem', md: '1.15rem' }, fontWeight: 500, maxWidth: { xs: '70%', md: 520 } }}>
        Шаурма, фалафель и картошка — привезём горячим или соберём к вашему приходу
      </Typography>

      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, mt: { xs: 2, md: 3 }, maxWidth: { xs: '80%', md: '70%' } }}>
        {settings && (
          <Fact icon={<TimeIcon fontSize="small" />} text={`${settings.isOpenNow ? 'Открыто' : 'Закрыто'}, ${settings.openTime}–${settings.closeTime}`} />
        )}
        {facts.map(f => <Fact key={f.text} icon={f.icon} text={f.text} />)}
      </Box>
    </Box>
  );
};

const Fact: React.FC<{ icon: React.ReactNode; text: string }> = ({ icon, text }) => (
  <Box
    sx={{
      display: 'inline-flex',
      alignItems: 'center',
      gap: 0.75,
      px: 1.5,
      py: 0.75,
      borderRadius: '12px',
      bgcolor: 'rgba(255,255,255,0.55)',
      fontWeight: 600,
      fontSize: '0.875rem',
    }}
  >
    {icon}
    {text}
  </Box>
);

const MenuPage: React.FC = () => {
  const theme = useTheme();
  const isWide = useMediaQuery(theme.breakpoints.up('lg'));

  const { isAuthenticated } = useAuthStore();
  const can = useCan();
  const isAdminRole = isAuthenticated && can(P.Menu);

  const { data: menuItems, isLoading, error, refetch } = useShawarmas();

  const [selectedProduct, setSelectedProduct] = useState<Shawarma | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [onlyFavorites, setOnlyFavorites] = useState(false);
  const [activeSection, setActiveSection] = useState('');

  // Избранное
  const { data: favoriteIds = [] } = useFavoriteIds();
  const toggleFavorite = useToggleFavorite();
  const favoriteSet = useMemo(() => new Set(favoriteIds), [favoriteIds]);

  const cartItems = useCartStore(state => state.items);
  const addToCart = useCartStore(state => state.addItem);

  // Сколько штук каждой карточки в корзине
  const inCart = useMemo(() => {
    const map = new Map<number, number>();
    cartItems.forEach(i => map.set(i.id, (map.get(i.id) ?? 0) + i.quantity));
    return map;
  }, [cartItems]);

  // Карточки (parent_id = null) с доступными вариантами
  const cards = useMemo(() => {
    return (menuItems ?? [])
      .filter(item => item.isCard || item.parentId === null)
      .map(item => ({ ...item, children: (item.children || []).filter(c => c.isAvailable) }))
      .filter(item => item.children.length > 0);
  }, [menuItems]);

  // Поиск (по названию, вариантам и составу) и избранное
  const visibleCards = useMemo(() => {
    let list = cards;
    if (onlyFavorites) list = list.filter(c => favoriteSet.has(c.id));
    const q = searchQuery.trim().toLowerCase();
    if (!q) return list;
    return list.filter(card =>
      card.name.toLowerCase().includes(q) ||
      card.description?.toLowerCase().includes(q) ||
      (card.children || []).some(child => child.name.toLowerCase().includes(q)));
  }, [cards, onlyFavorites, favoriteSet, searchQuery]);

  // Разделы меню в порядке первого появления
  const sections = useMemo(() => {
    const map = new Map<string, Shawarma[]>();
    visibleCards.forEach(card => {
      const key = card.category?.trim() || 'Меню';
      if (!map.has(key)) map.set(key, []);
      map.get(key)!.push(card);
    });
    return Array.from(map.entries()).map(([title, items], i) => ({ id: `section-${i}`, title, items }));
  }, [visibleCards]);

  // Подсветка вкладки раздела, который сейчас на экране
  React.useEffect(() => {
    if (sections.length < 2) return;
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries.filter(e => e.isIntersecting).sort((x, y) => x.boundingClientRect.top - y.boundingClientRect.top);
        if (visible[0]) setActiveSection(visible[0].target.id);
      },
      { rootMargin: '-160px 0px -55% 0px' }
    );
    sections.forEach(s => {
      const el = document.getElementById(s.id);
      if (el) observer.observe(el);
    });
    return () => observer.disconnect();
  }, [sections]);

  // Активную вкладку держим в зоне видимости
  const tabsRef = React.useRef<HTMLDivElement>(null);
  React.useEffect(() => {
    const tab = tabsRef.current?.querySelector(`[data-section="${activeSection}"]`) as HTMLElement | null;
    tab?.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
  }, [activeSection]);

  const scrollToSection = (id: string) => {
    setActiveSection(id);
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  const handleAddToCart = (product: Shawarma, quantity: number, selectedAddons: SelectedAddon[], instructions: string, selectedChild?: Shawarma) => {
    addToCart(product, quantity, selectedAddons, instructions, selectedChild);
  };

  return (
    <>
      <RestaurantHero />
      <RepeatLastOrderBanner />

      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'minmax(0, 1fr) 340px' },
          gap: 3,
          alignItems: 'start',
        }}
      >
        <Box component="section" aria-label="Меню" sx={{ minWidth: 0 }}>
          {/* Липкая панель: поиск и быстрые фильтры */}
          <Box
            sx={{
              position: 'sticky',
              top: { xs: 60, md: 72 },
              zIndex: 5,
              mx: { xs: -2, sm: -3, lg: 0 },
              px: { xs: 2, sm: 3, lg: 0 },
              py: 1.5,
              bgcolor: 'background.default',
              display: 'flex',
              alignItems: 'center',
              gap: 1,
              flexWrap: { xs: 'wrap', md: 'nowrap' },
            }}
          >
            <TextField
              size="small"
              placeholder="Найти в меню"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              sx={{ flex: { xs: '1 1 100%', md: '0 1 320px' }, '& .MuiOutlinedInput-root': { bgcolor: 'background.paper' } }}
              inputProps={{ 'aria-label': 'Поиск по меню' }}
              InputProps={{
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchIcon sx={{ color: 'text.secondary' }} />
                  </InputAdornment>
                ),
                endAdornment: searchQuery ? (
                  <InputAdornment position="end">
                    <IconButton size="small" aria-label="Очистить поиск" onClick={() => setSearchQuery('')}>
                      <CloseIcon fontSize="small" />
                    </IconButton>
                  </InputAdornment>
                ) : undefined,
              }}
            />
            <Box
              ref={tabsRef}
              role="tablist"
              aria-label="Разделы меню"
              sx={{ display: 'flex', gap: 1, overflowX: 'auto', flex: 1, minWidth: 0, scrollbarWidth: 'none', '&::-webkit-scrollbar': { display: 'none' } }}
            >
              {isAuthenticated && (favoriteIds.length > 0 || onlyFavorites) && (
                <Chip
                  icon={<FavoriteIcon />}
                  label="Избранное"
                  onClick={() => setOnlyFavorites(v => !v)}
                  color={onlyFavorites ? 'primary' : 'default'}
                  aria-pressed={onlyFavorites}
                  sx={{ height: 40, px: 0.5, fontWeight: 600, flexShrink: 0, bgcolor: onlyFavorites ? undefined : 'background.paper' }}
                />
              )}
              {sections.length > 1 && sections.map(section => (
                <Chip
                  key={section.id}
                  role="tab"
                  data-section={section.id}
                  aria-selected={activeSection === section.id}
                  label={section.title}
                  onClick={() => scrollToSection(section.id)}
                  color={activeSection === section.id ? 'primary' : 'default'}
                  sx={{ height: 40, px: 0.5, fontWeight: 600, flexShrink: 0, bgcolor: activeSection === section.id ? undefined : 'background.paper' }}
                />
              ))}
            </Box>
            {isAdminRole && (
              <Button
                component={Link}
                to="/admin/create"
                startIcon={<AddIcon />}
                variant="outlined"
                sx={{ ml: 'auto', flexShrink: 0, display: { xs: 'none', md: 'inline-flex' }, bgcolor: 'background.paper' }}
              >
                Новая позиция
              </Button>
            )}
          </Box>

          {error && (
            <Alert
              severity="error"
              sx={{ my: 2 }}
              action={<Button color="inherit" size="small" onClick={() => refetch()}>Повторить</Button>}
            >
              Не удалось загрузить меню. Проверьте соединение и попробуйте ещё раз.
            </Alert>
          )}

          {isLoading && (
            <Box sx={gridSx}>
              {Array.from({ length: 8 }).map((_, i) => (
                <Box key={i} sx={{ bgcolor: 'background.paper', borderRadius: '24px', p: 1.25 }}>
                  <Skeleton variant="rounded" sx={{ aspectRatio: '1 / 1', height: 'auto', borderRadius: '18px' }} />
                  <Skeleton width="40%" height={32} sx={{ mt: 1 }} />
                  <Skeleton width="80%" />
                  <Skeleton variant="rounded" height={40} sx={{ mt: 1, borderRadius: '14px' }} />
                </Box>
              ))}
            </Box>
          )}

          {sections.map(section => (
            <Box key={section.id} id={section.id} component="section" sx={{ scrollMarginTop: { xs: 180, md: 150 }, mt: { xs: 2, md: 3 } }}>
              {sections.length > 1 && (
                <Typography variant="h4" component="h2" sx={{ mb: { xs: 1.5, md: 2 }, fontSize: { xs: '1.5rem', md: '2rem' } }}>
                  {section.title}
                </Typography>
              )}
              <Box sx={gridSx}>
                {section.items.map(card => (
                  <MenuItemCard
                    key={card.id}
                    item={card}
                    onOpen={() => setSelectedProduct(card)}
                    inCartCount={inCart.get(card.id) ?? 0}
                    isFavorite={favoriteSet.has(card.id)}
                    onToggleFavorite={isAuthenticated
                      ? () => toggleFavorite.mutate({ shawarmaId: card.id, isFavorite: !favoriteSet.has(card.id) })
                      : undefined}
                  />
                ))}
              </Box>
            </Box>
          ))}

          {!isLoading && !error && visibleCards.length === 0 && (
            <Box sx={{ textAlign: 'center', py: 8 }}>
              <Typography variant="h5" sx={{ mb: 1 }}>
                {cards.length === 0 ? 'Меню скоро появится' : 'Ничего не нашлось'}
              </Typography>
              <Typography color="text.secondary" sx={{ mb: 2 }}>
                {cards.length === 0
                  ? (isAdminRole ? 'Добавьте первую позицию — она сразу появится здесь.' : 'Загляните чуть позже.')
                  : 'Попробуйте другое слово или сбросьте фильтр.'}
              </Typography>
              {cards.length === 0 && isAdminRole ? (
                <Button component={Link} to="/admin/create" variant="contained" startIcon={<AddIcon />}>Добавить позицию</Button>
              ) : cards.length > 0 && (
                <Button variant="outlined" onClick={() => { setSearchQuery(''); setOnlyFavorites(false); }}>Показать всё меню</Button>
              )}
            </Box>
          )}
        </Box>

        {isWide && (
          <Box sx={{ position: 'sticky', top: 96 }}>
            <CartPanel />
          </Box>
        )}
      </Box>

      {!isWide && <MobileCartBar />}

      <ProductModal
        open={!!selectedProduct}
        onClose={() => setSelectedProduct(null)}
        product={selectedProduct}
        onAddToCart={handleAddToCart}
      />
    </>
  );
};

export default MenuPage;
