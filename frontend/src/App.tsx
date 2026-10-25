import { BrowserRouter as Router, Routes, Route, Link, NavLink, Navigate } from 'react-router-dom';
import {
  AppBar, Toolbar, Typography, Button, Box, Container,
  ThemeProvider, CssBaseline,
  IconButton, Drawer, List, ListItem, ListItemButton,
  ListItemIcon, ListItemText, useMediaQuery,
  Menu, MenuItem,
} from '@mui/material';
import {
  Dashboard as DashboardIcon,
  AddCircle as AddIcon,
  Menu as MenuIcon,
  Close as CloseIcon,
  Home as HomeIcon,
  ListAlt as ListAltIcon,
  People as PeopleIcon,
  LocalOffer as PromoIcon,
  Star as StarIcon,
  Block as BlockIcon,
  RateReview as ReviewsIcon,
  SoupKitchen as KitchenIcon,
  DeliveryDining as DeliveryIcon,
  Map as MapIcon,
  Insights as InsightsIcon,
  ExpandMore as ExpandMoreIcon,
  DarkModeOutlined as DarkModeIcon,
  LightModeOutlined as LightModeIcon,
  ShoppingBagOutlined as ShoppingBagIcon,
  Inventory2Outlined as InventoryIcon,
  AdminPanelSettingsOutlined as RolesIcon,
  PointOfSaleOutlined as PosIcon,
} from '@mui/icons-material';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { useState, useEffect } from 'react';
import { useTheme } from './hooks/useTheme';
import { createAppTheme } from './theme';
import BrandLogo from './components/BrandLogo';
import WorkingHours from './components/WorkingHours';
import type { PromoCodeValidation } from './types';
import { useCartStore, useTotalItems, useTotalPrice } from './store/cartStore';
import { useAuthStore } from './store/authStore';
import { useOrderFlowStore } from './store/orderFlowStore';
import CartModal from './components/CartModal';
import OrderModal from './components/OrderModal';
import AuthModal from './components/AuthModal';
import ProfileMenu from './components/ProfileMenu';
import LiveOrdersListener from './components/LiveOrdersListener';
import { initTelegramMiniApp } from './utils/telegram';
import { captureReferralFromUrl } from './utils/referral';

// Страницы
import DashboardPage from './pages/DashboardPage';
import OrdersPage from './pages/OrdersPage';
import MenuPage from './pages/MenuPage';
import CreateMenuItemPage from "./pages/CreateMenuItemPage";
import OrderSuccessPage from './pages/OrderSuccessPage';
import AdminMenuPage from './pages/AdminMenuPage';
import UsersPage from './pages/UserPage';
import AdminPromoCodesPage from './pages/AdminPromoCodesPage';
import PointsPage from './pages/PointsPage';
import AdminStopListPage from './pages/AdminStopListPage';
import AdminReviewsPage from './pages/AdminReviewsPage';
import KitchenPage from './pages/KitchenPage';
import CourierPage from './pages/CourierPage';
import AdminDeliveryZonesPage from './pages/AdminDeliveryZonesPage';
import AnalyticsPage from './pages/AnalyticsPage';
import InventoryPage from './pages/inventory/InventoryPage';
import RolesPage from './pages/RolesPage';
import PosPage from './pages/PosPage';
import { P, useCan } from './auth/permissions';
import { apiClient } from './api/client';
import { useIsFullscreen } from './hooks/useFullscreen';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
});

function App() {
  const totalItems = useTotalItems();
  const totalPrice = useTotalPrice();

  const { theme: themeMode, toggleTheme } = useTheme();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const isFullscreen = useIsFullscreen();
  const [authOpen, setAuthOpen] = useState(false);
  const [promoInfo, setPromoInfo] = useState<PromoCodeValidation | null>(null);
  const [pointsDiscount, setPointsDiscount] = useState(0);
  const [adminMenuAnchor, setAdminMenuAnchor] = useState<HTMLElement | null>(null);

  const cartOpen = useOrderFlowStore((state) => state.cartOpen);
  const deliveryType = useOrderFlowStore((state) => state.deliveryType);
  const address = useOrderFlowStore((state) => state.address);
  const orderOpen = useOrderFlowStore((state) => state.orderOpen);
  const openCart = useOrderFlowStore((state) => state.openCart);
  const closeCart = useOrderFlowStore((state) => state.closeCart);
  const closeOrder = useOrderFlowStore((state) => state.closeOrder);
  const backToCart = useOrderFlowStore((state) => state.backToCart);
  const proceedToCheckout = useOrderFlowStore((state) => state.proceedToCheckout);
  const setDeliveryType = useOrderFlowStore((state) => state.setDeliveryType);
  const setAddress = useOrderFlowStore((state) => state.setAddress);
  const customerName = useOrderFlowStore((state) => state.customerName);
  const setCustomerName = useOrderFlowStore((state) => state.setCustomerName);
  const phone = useOrderFlowStore((state) => state.phone);
  const setPhone = useOrderFlowStore((state) => state.setPhone);

  const { isAuthenticated, logout } = useAuthStore();
  const setUser = useAuthStore((state) => state.setUser);
  const can = useCan();
  const user = useAuthStore((state) => state.user);
  const clearCart = useCartStore((state) => state.clearCart);
  const resetCustomerData = useOrderFlowStore((state) => state.resetCustomerData);

  // При выходе — очищаем корзину и данные пользователя
  useEffect(() => {
    if (!isAuthenticated) {
      clearCart();
      resetCustomerData();
    }
  }, [isAuthenticated]);

  // При смене аккаунта (изменился user.id) — сбрасываем и перезаполняем данные
  const userId = user?.id;
  useEffect(() => {
    if (userId && isAuthenticated && user) {
      resetCustomerData();
      const name = [user.firstName, user.lastName].filter(Boolean).join(' ');
      if (name) setCustomerName(name);
      if (user.phone) setPhone(user.phone);
    }
  }, [userId]);

  // При открытии модалки заказа — подставляем данные авторизованного пользователя
  useEffect(() => {
    if (orderOpen && isAuthenticated && user) {
      const name = [user.firstName, user.lastName].filter(Boolean).join(' ');
      if (name && !customerName) {
        setCustomerName(name);
      }
      if (user.phone && !phone) {
        setPhone(user.phone);
      }
    }
  }, [orderOpen, isAuthenticated, user]);

  // При запуске обновляем профиль: роли и права могли измениться, пока вкладка была закрыта
  useEffect(() => {
    if (!isAuthenticated) return;
    apiClient.get('/api/auth/me')
      .then(res => setUser(res.data))
      .catch(() => { /* 401 обработает перехватчик */ });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated, userId]);

  // Открыто как мини-приложение Telegram — разворачиваем и пробуем войти;
  // запоминаем реферальный код из ссылки ?ref=
  useEffect(() => {
    captureReferralFromUrl();
    void initTelegramMiniApp();
  }, []);

  useEffect(() => {
    const handleUnauthorized = () => {
      logout();
    };

    window.addEventListener('auth-unauthorized', handleUnauthorized);
    return () => {
      window.removeEventListener('auth-unauthorized', handleUnauthorized);
    };
  }, [logout]);


  const theme = React.useMemo(() => createAppTheme(themeMode), [themeMode]);

  const isMobile = useMediaQuery(theme.breakpoints.down('md'));

  const toggleMobileMenu = () => {
    setMobileMenuOpen(!mobileMenuOpen);
  };

  const closeMobileMenu = () => {
    setMobileMenuOpen(false);
  };

  // Разделы управления: показываются по правам ролей (и в меню «Управление», и в боковом меню)
  const staffLinks = ([
    { text: 'Дашборд', icon: <DashboardIcon />, path: '/admin/dashboard', perms: [P.Dashboard] },
    { text: 'Аналитика', icon: <InsightsIcon />, path: '/admin/analytics', perms: [P.Analytics] },
    { text: 'Касса', icon: <PosIcon />, path: '/pos', perms: [P.Pos] },
    { text: 'Кухня', icon: <KitchenIcon />, path: '/kitchen', perms: [P.Kitchen] },
    { text: 'Доставка', icon: <DeliveryIcon />, path: '/courier', perms: [P.Delivery, P.CouriersAssign] },
    { text: 'Товары', icon: <AddIcon />, path: '/admin/menu', perms: [P.Menu] },
    { text: 'Стоп-лист', icon: <BlockIcon />, path: '/admin/stoplist', perms: [P.StopList] },
    { text: 'Зоны доставки', icon: <MapIcon />, path: '/admin/zones', perms: [P.Zones] },
    { text: 'Промокоды', icon: <PromoIcon />, path: '/admin/promocodes', perms: [P.PromoCodes] },
    { text: 'Отзывы', icon: <ReviewsIcon />, path: '/admin/reviews', perms: [P.Reviews] },
    { text: 'Склад', icon: <InventoryIcon />, path: '/admin/inventory', perms: [P.InventoryView, P.TechCards, P.Chz] },
    { text: 'Добавить товар', icon: <AddIcon />, path: '/admin/create', perms: [P.Menu] },
    { text: 'Пользователи', icon: <PeopleIcon />, path: '/admin/users', perms: [P.UsersView, P.UsersRoles] },
    { text: 'Роли и доступы', icon: <RolesIcon />, path: '/admin/roles', perms: [P.Roles] },
  ] as { text: string; icon: React.ReactNode; path: string; perms: string[] }[])
    .filter(link => isAuthenticated && can(...link.perms));

  const getMenuItems = () => {
    const items: { text: string; icon: React.ReactNode; path: string }[] = [
      { text: 'Меню', icon: <HomeIcon />, path: '/' },
      { text: 'Мои заказы', icon: <ListAltIcon />, path: '/orders' },
      { text: 'Мои баллы', icon: <StarIcon />, path: '/points' },
    ];

    staffLinks.forEach(link => items.push({ text: link.text, icon: link.icon, path: link.path }));
    return items;
  };

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <Router>
          <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
            {/* На весь экран (кухня, касса) шапку и подвал не показываем */}
            {!isFullscreen && (
            <AppBar position="sticky" sx={{ zIndex: (t) => t.zIndex.appBar }}>
              <Container maxWidth={false} sx={{ maxWidth: 1320 }}>
                <Toolbar disableGutters sx={{ gap: { xs: 1, md: 2 }, minHeight: { xs: 60, md: 72 } }}>
                  {isMobile && (
                    <IconButton edge="start" aria-label="Открыть меню" onClick={toggleMobileMenu}>
                      <MenuIcon />
                    </IconButton>
                  )}

                  <BrandLogo compact={false} />

                  {!isMobile && (
                    <Box component="nav" sx={{ display: 'flex', gap: 0.5, ml: 3 }}>
                      {[
                        { to: '/', label: 'Меню' },
                        { to: '/orders', label: 'Мои заказы' },
                        { to: '/points', label: 'Баллы' },
                      ].map(link => (
                        <Button
                          key={link.to}
                          component={NavLink}
                          to={link.to}
                          end
                          sx={{
                            color: 'text.secondary',
                            fontWeight: 600,
                            '&.active': { color: 'text.primary', bgcolor: 'action.hover' },
                          }}
                        >
                          {link.label}
                        </Button>
                      ))}
                      {staffLinks.length > 0 && (
                        <>
                          <Button
                            onClick={(e) => setAdminMenuAnchor(e.currentTarget)}
                            endIcon={<ExpandMoreIcon />}
                            sx={{ color: 'text.secondary', fontWeight: 600 }}
                          >
                            Управление
                          </Button>
                          <Menu
                            anchorEl={adminMenuAnchor}
                            open={Boolean(adminMenuAnchor)}
                            onClose={() => setAdminMenuAnchor(null)}
                          >
                            {staffLinks.map((link) => (
                              <MenuItem
                                key={link.path}
                                component={Link}
                                to={link.path}
                                onClick={() => setAdminMenuAnchor(null)}
                              >
                                <ListItemIcon>{link.icon}</ListItemIcon>
                                <ListItemText>{link.text}</ListItemText>
                              </MenuItem>
                            ))}
                          </Menu>
                        </>
                      )}
                    </Box>
                  )}

                  <Box sx={{ flex: 1 }} />

                  {!isMobile && (
                    <IconButton onClick={toggleTheme} aria-label={themeMode === 'light' ? 'Тёмная тема' : 'Светлая тема'}>
                      {themeMode === 'light' ? <DarkModeIcon /> : <LightModeIcon />}
                    </IconButton>
                  )}

                  <ProfileMenu onLoginClick={() => setAuthOpen(true)} />

                  <Button
                    variant="contained"
                    color="secondary"
                    onClick={openCart}
                    aria-label={`Корзина, ${totalItems} шт.`}
                    startIcon={<ShoppingBagIcon />}
                    sx={{
                      minWidth: { xs: 48, sm: 120 },
                      px: { xs: 1.5, sm: 2.25 },
                      fontWeight: 700,
                      whiteSpace: 'nowrap',
                      '& .MuiButton-startIcon': { mr: { xs: totalItems ? 0.75 : 0, sm: 1 } },
                    }}
                  >
                    <Box component="span" sx={{ display: { xs: totalItems ? 'inline' : 'none', sm: 'inline' } }}>
                      {totalItems > 0 ? `${totalPrice.toLocaleString('ru-RU')} ₽` : 'Корзина'}
                    </Box>
                  </Button>
                </Toolbar>
              </Container>
            </AppBar>
            )}

            <Drawer
              anchor="left"
              open={mobileMenuOpen}
              onClose={closeMobileMenu}
              PaperProps={{ sx: { width: 300, borderRadius: '0 24px 24px 0' } }}
            >
              <Box sx={{ p: 2.5, display: 'flex', flexDirection: 'column', height: '100%' }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
                  <BrandLogo onClick={closeMobileMenu} />
                  <IconButton onClick={closeMobileMenu} aria-label="Закрыть меню">
                    <CloseIcon />
                  </IconButton>
                </Box>

                <List disablePadding>
                  {getMenuItems().map((item) => (
                    <ListItem key={item.path} disablePadding sx={{ mb: 0.5 }}>
                      <ListItemButton
                        component={NavLink}
                        to={item.path}
                        end
                        onClick={closeMobileMenu}
                        sx={{
                          borderRadius: 3,
                          py: 1.25,
                          '&.active': { bgcolor: 'action.selected' },
                        }}
                      >
                        <ListItemIcon sx={{ minWidth: 40, color: 'text.secondary' }}>{item.icon}</ListItemIcon>
                        <ListItemText primary={item.text} primaryTypographyProps={{ fontWeight: 600 }} />
                      </ListItemButton>
                    </ListItem>
                  ))}
                </List>

                <Box sx={{ mt: 'auto', pt: 2, display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                  <WorkingHours />
                  <IconButton onClick={toggleTheme} aria-label={themeMode === 'light' ? 'Тёмная тема' : 'Светлая тема'}>
                    {themeMode === 'light' ? <DarkModeIcon /> : <LightModeIcon />}
                  </IconButton>
                </Box>
              </Box>
            </Drawer>

            <Container
              maxWidth={false}
              sx={{
                maxWidth: isFullscreen ? 'none' : 1320,
                pt: isFullscreen ? 2 : { xs: 2, md: 3 },
                pb: isFullscreen ? 2 : { xs: 12, md: 6 },
                flex: 1,
                px: { xs: 2, sm: 3 },
              }}
            >
              <LiveOrdersListener />
              <Routes>
                <Route path="/" element={<MenuPage />} />
                <Route path="/orders" element={<OrdersPage />} />
                <Route path="/points" element={<PointsPage />} />
                <Route path="/order" element={<Navigate to="/" replace />} />
                <Route path="/order/:id/success" element={<OrderSuccessPage />} />

                {/* Разделы управления — только при наличии прав */}
                {can(P.Dashboard) && <Route path="/admin/dashboard" element={<DashboardPage />} />}
                {can(P.Analytics) && <Route path="/admin/analytics" element={<AnalyticsPage />} />}
                {can(P.Pos) && <Route path="/pos" element={<PosPage />} />}
                {can(P.Kitchen) && <Route path="/kitchen" element={<KitchenPage />} />}
                {can(P.Delivery, P.CouriersAssign) && <Route path="/courier" element={<CourierPage />} />}
                {can(P.Menu) && <Route path="/admin/menu" element={<AdminMenuPage />} />}
                {can(P.Menu) && <Route path="/admin/create" element={<CreateMenuItemPage />} />}
                {can(P.Menu) && <Route path="/admin/edit/:id" element={<CreateMenuItemPage />} />}
                {can(P.StopList) && <Route path="/admin/stoplist" element={<AdminStopListPage />} />}
                {can(P.Zones) && <Route path="/admin/zones" element={<AdminDeliveryZonesPage />} />}
                {can(P.PromoCodes) && <Route path="/admin/promocodes" element={<AdminPromoCodesPage />} />}
                {can(P.Reviews) && <Route path="/admin/reviews" element={<AdminReviewsPage />} />}
                {can(P.InventoryView, P.TechCards, P.Chz) && <Route path="/admin/inventory" element={<InventoryPage />} />}
                {can(P.InventoryView, P.TechCards, P.Chz) && <Route path="/admin/inventory/:tab" element={<InventoryPage />} />}
                {can(P.UsersView, P.UsersRoles) && <Route path="/admin/users" element={<UsersPage />} />}
                {can(P.Roles) && <Route path="/admin/roles" element={<RolesPage />} />}

                <Route path="*" element={<Navigate to="/" replace />} />
              </Routes>
            </Container>

            <CartModal
              open={cartOpen}
              onClose={closeCart}
              onCheckout={proceedToCheckout}
              deliveryType={deliveryType}
              onDeliveryTypeChange={setDeliveryType}
              address={address}
              onAddressChange={setAddress}
              onPromoApplied={setPromoInfo}
              onPointsApplied={setPointsDiscount}
            />

            <OrderModal
              open={orderOpen}
              onClose={closeOrder}
              onBackToCart={backToCart}
              deliveryType={deliveryType}
              address={address}
              onAddressChange={setAddress}
              customerName={customerName}
              onCustomerNameChange={setCustomerName}
              phone={phone}
              onPhoneChange={setPhone}
              promoInfo={promoInfo}
              pointsDiscount={pointsDiscount}
            />

            <AuthModal
              open={authOpen}
              onClose={() => setAuthOpen(false)}
            />

            {!isFullscreen && (
            <Box component="footer" sx={{ py: 3, px: 2, mt: 'auto', borderTop: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
              <Container maxWidth={false} sx={{ maxWidth: 1320, display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
                <BrandLogo />
                <WorkingHours />
                <Typography variant="body2" color="text.secondary">
                  © {new Date().getFullYear()} Кума Шаурма
                </Typography>
              </Container>
            </Box>
            )}
          </Box>
        </Router>
      </ThemeProvider>
    </QueryClientProvider>
  );
}

export default App;