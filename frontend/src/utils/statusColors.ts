import type { OrderStatus } from '../types';

export const getStatusColor = (status: OrderStatus, themeMode: 'light' | 'dark') => {
  const isLight = themeMode === 'light';
  
  switch (status) {
    case 'Новый': return { 
      bg: isLight ? '#e3f2fd' : '#1e3a5f', 
      color: isLight ? '#1976d2' : '#90caf9' 
    };
    case 'Принят': return { 
      bg: isLight ? '#fff8e1' : '#5c4300', 
      color: isLight ? '#C77700' : '#ffd54f' 
    };
    case 'Готовится': return { 
      bg: isLight ? '#fff3e0' : '#663c00', 
      color: isLight ? '#f57c00' : '#ffb74d' 
    };
    case 'Готов': return { 
      bg: isLight ? '#e8f5e9' : '#1b5e20', 
      color: isLight ? '#2e7d32' : '#81c784' 
    };
    case 'В пути': return { 
      bg: isLight ? '#ede7f6' : '#311b92', 
      color: isLight ? '#5e35b1' : '#b39ddb' 
    };
    case 'Выполнен':
    case 'Доставлен': return { 
      bg: isLight ? '#e8f5e9' : '#1b5e20', 
      color: isLight ? '#2e7d32' : '#81c784' 
    };
    case 'Отменён': return { 
      bg: isLight ? '#ffebee' : '#7f1d1d', 
      color: isLight ? '#d32f2f' : '#ef5350' 
    };
    default: return { 
      bg: isLight ? '#f5f5f5' : '#424242', 
      color: isLight ? '#757575' : '#bdbdbd' 
    };
  }
};
