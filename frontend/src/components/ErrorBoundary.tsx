import * as React from 'react';
import { Box, Button, Typography } from '@mui/material';
import { reportClientError } from '../utils/errorReporting';

/** Страница упала с ошибкой: сообщаем на сервер и предлагаем обновить, а не показываем белый экран */
export default class ErrorBoundary extends React.Component<{ children: React.ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: { componentStack?: string | null }) {
    reportClientError(error.message, `${error.stack ?? ''}\n${info.componentStack ?? ''}`);
  }

  render() {
    if (!this.state.failed) return this.props.children;
    return (
      <Box sx={{ maxWidth: 420, mx: 'auto', py: 8, px: 2, textAlign: 'center', display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Typography variant="h5" sx={{ fontWeight: 800 }}>Что-то пошло не так</Typography>
        <Typography color="text.secondary">Мы уже получили сообщение об ошибке. Обновите страницу — обычно этого достаточно.</Typography>
        <Button variant="contained" onClick={() => window.location.reload()}>Обновить страницу</Button>
      </Box>
    );
  }
}
