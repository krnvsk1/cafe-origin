import { createTheme, alpha } from '@mui/material';

/**
 * Дизайн-токены Кума Шаурмы.
 * Шафрановый жёлтый — цвет всех «денежных» действий (корзина, добавить, оформить),
 * остальной интерфейс — спокойный тёплый нейтральный.
 */
export const tokens = {
  light: {
    bg: '#F5F4F2',
    surface: '#FFFFFF',
    surfaceMuted: '#F1EFEC',
    ink: '#21201F',
    muted: '#7B776F',
    line: '#E9E6E1',
  },
  dark: {
    bg: '#141312',
    surface: '#1E1D1B',
    surfaceMuted: '#2A2826',
    ink: '#F3F1EC',
    muted: '#A39E95',
    line: '#302E2B',
  },
  saffron: '#FFC933',
  saffronHover: '#F5BA12',
  chili: '#E2462C',
  herb: '#2E9E5B',
};

export const createAppTheme = (mode: 'light' | 'dark') => {
  const t = tokens[mode];
  const isLight = mode === 'light';

  return createTheme({
    palette: {
      mode,
      // Основной цвет — тёмный «чернильный»: ссылки, активные вкладки, переключатели
      primary: { main: t.ink, contrastText: t.surface },
      // Акцентный — шафран: корзина и оформление заказа
      secondary: { main: tokens.saffron, dark: tokens.saffronHover, contrastText: '#21201F' },
      error: { main: tokens.chili },
      success: { main: tokens.herb },
      warning: { main: '#E8890C' },
      background: { default: t.bg, paper: t.surface },
      text: { primary: t.ink, secondary: t.muted },
      divider: t.line,
      action: {
        hover: alpha(t.ink, isLight ? 0.04 : 0.08),
        selected: alpha(t.ink, isLight ? 0.08 : 0.14),
      },
    },
    shape: { borderRadius: 14 },
    typography: {
      fontFamily: ['Onest', 'Inter', '-apple-system', 'BlinkMacSystemFont', 'sans-serif'].join(','),
      h1: { fontWeight: 800, letterSpacing: '-0.03em' },
      h2: { fontWeight: 800, letterSpacing: '-0.03em' },
      h3: { fontWeight: 800, letterSpacing: '-0.025em' },
      h4: { fontWeight: 800, letterSpacing: '-0.02em' },
      h5: { fontWeight: 700, letterSpacing: '-0.015em' },
      h6: { fontWeight: 700, letterSpacing: '-0.01em' },
      subtitle1: { fontWeight: 600 },
      subtitle2: { fontWeight: 600 },
      button: { textTransform: 'none', fontWeight: 600, letterSpacing: 0 },
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          body: { backgroundColor: t.bg, WebkitFontSmoothing: 'antialiased' },
          '::selection': { backgroundColor: alpha(tokens.saffron, 0.5) },
        },
      },
      MuiAppBar: {
        defaultProps: { elevation: 0, color: 'inherit' },
        styleOverrides: {
          root: { backgroundColor: t.surface, borderBottom: `1px solid ${t.line}` },
        },
      },
      MuiPaper: {
        defaultProps: { elevation: 0 },
        styleOverrides: {
          root: { backgroundImage: 'none' },
          rounded: { borderRadius: 20 },
        },
      },
      MuiCard: {
        defaultProps: { elevation: 0 },
        styleOverrides: { root: { borderRadius: 20 } },
      },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: { borderRadius: 14, paddingInline: 18, minHeight: 40 },
          sizeLarge: { minHeight: 52, fontSize: '1rem', borderRadius: 16 },
          sizeSmall: { minHeight: 32, borderRadius: 10, paddingInline: 12 },
          outlined: { borderColor: t.line, '&:hover': { borderColor: t.muted, backgroundColor: alpha(t.ink, 0.03) } },
          containedSecondary: { '&:hover': { backgroundColor: tokens.saffronHover } },
        },
      },
      MuiIconButton: {
        styleOverrides: { root: { borderRadius: 12 } },
      },
      MuiFab: {
        styleOverrides: { root: { boxShadow: 'none', textTransform: 'none', fontWeight: 700 } },
      },
      MuiChip: {
        styleOverrides: {
          root: { borderRadius: 12, fontWeight: 500 },
          outlined: { borderColor: t.line },
          filled: { backgroundColor: t.surfaceMuted },
          colorPrimary: { backgroundColor: t.ink, color: t.surface },
          colorSecondary: { backgroundColor: tokens.saffron, color: '#21201F' },
        },
      },
      MuiOutlinedInput: {
        styleOverrides: {
          root: {
            borderRadius: 14,
            backgroundColor: t.surfaceMuted,
            '& .MuiOutlinedInput-notchedOutline': { borderColor: 'transparent' },
            '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: t.line },
            '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: t.ink, borderWidth: 1.5 },
            '&.Mui-error .MuiOutlinedInput-notchedOutline': { borderColor: tokens.chili },
          },
        },
      },
      MuiInputLabel: {
        styleOverrides: { root: { '&.Mui-focused': { color: t.ink } } },
      },
      MuiDialog: {
        styleOverrides: {
          paper: { borderRadius: 24 },
          paperFullScreen: { borderRadius: 0 },
        },
      },
      MuiDialogTitle: {
        styleOverrides: { root: { fontWeight: 800, letterSpacing: '-0.02em' } },
      },
      MuiDrawer: {
        styleOverrides: { paper: { backgroundColor: t.surface } },
      },
      MuiMenu: {
        styleOverrides: {
          paper: { borderRadius: 16, border: `1px solid ${t.line}`, boxShadow: '0 12px 32px rgba(33,32,31,0.12)' },
        },
      },
      MuiToggleButton: {
        styleOverrides: {
          root: {
            textTransform: 'none',
            fontWeight: 600,
            border: 'none',
            borderRadius: 12,
            color: t.muted,
            '&.Mui-selected': {
              backgroundColor: t.surface,
              color: t.ink,
              boxShadow: '0 1px 4px rgba(33,32,31,0.12)',
              '&:hover': { backgroundColor: t.surface },
            },
          },
        },
      },
      MuiToggleButtonGroup: {
        styleOverrides: {
          root: { backgroundColor: t.surfaceMuted, borderRadius: 14, padding: 4, gap: 4 },
          grouped: { border: 'none !important', borderRadius: '12px !important' },
        },
      },
      MuiTabs: {
        styleOverrides: { indicator: { height: 3, borderRadius: 3 } },
      },
      MuiTab: {
        styleOverrides: { root: { textTransform: 'none', fontWeight: 600, minHeight: 44 } },
      },
      MuiAlert: {
        styleOverrides: { root: { borderRadius: 14 } },
      },
      MuiTableCell: {
        styleOverrides: { root: { borderColor: t.line }, head: { color: t.muted, fontWeight: 600 } },
      },
      MuiSwitch: {
        styleOverrides: {
          switchBase: { '&.Mui-checked + .MuiSwitch-track': { backgroundColor: tokens.herb, opacity: 1 } },
          colorPrimary: { '&.Mui-checked': { color: '#fff' } },
        },
      },
      MuiLinearProgress: {
        styleOverrides: { root: { borderRadius: 4 } },
      },
      MuiBadge: {
        styleOverrides: { badge: { fontWeight: 700 } },
      },
    },
  });
};
