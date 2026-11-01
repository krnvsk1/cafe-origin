import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { useAuthStore } from '../store/authStore';

export const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Текущий access token (из localStorage или хранилища Zustand)
export const getAccessToken = (): string | null => {
  let token = localStorage.getItem('token');

  if (!token) {
    const authStorage = localStorage.getItem('cafe-auth');
    if (authStorage) {
      try {
        const authData = JSON.parse(authStorage);
        token = authData.state?.accessToken ?? null;
      } catch (e) {
        console.error('Error parsing auth storage', e);
      }
    }
  }
  return token;
};

// Считаем токен истёкшим чуть заранее, чтобы не отправить его за секунду до конца
const TOKEN_EXPIRY_SKEW_MS = 30_000;

/** Истёк ли access token (по полю exp в JWT); токен без exp или нечитаемый — считаем истёкшим */
export const isAccessTokenExpired = (token: string | null = getAccessToken()): boolean => {
  if (!token) return true;
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
    return typeof payload.exp !== 'number' || payload.exp * 1000 - TOKEN_EXPIRY_SKEW_MS < Date.now();
  } catch {
    return true;
  }
};

let refreshing: Promise<string | null> | null = null;

/**
 * Обновить access token по refresh token. Один запрос на всех, кто получил 401 одновременно.
 * null — refresh token отклонён (нужно войти заново); исключение — сервер недоступен (сессию не трогаем).
 */
export const refreshAccessToken = (): Promise<string | null> => {
  if (refreshing) return refreshing;
  const { refreshToken, setTokens } = useAuthStore.getState();
  if (!refreshToken) return Promise.resolve(null);

  // Мимо apiClient, чтобы 401 самого обновления не зациклил перехватчик
  refreshing = axios
    .post<{ success: boolean; accessToken?: string; refreshToken?: string }>(
      `${import.meta.env.VITE_API_URL ?? ''}/api/auth/refresh`,
      { refreshToken },
    )
    .then(({ data }) => {
      if (!data.success || !data.accessToken || !data.refreshToken) return null;
      setTokens(data.accessToken, data.refreshToken);
      return data.accessToken;
    })
    .catch((error: AxiosError) => {
      if (error.response && error.response.status < 500) return null;
      throw error;
    })
    .finally(() => { refreshing = null; });
  return refreshing;
};

const forceLogout = () => {
  localStorage.removeItem('token');
  localStorage.removeItem('cafe-auth'); // Очищаем и Zustand storage
  // Мы не можем напрямую вызвать useAuthStore.getState().logout() здесь — App слушает событие и выходит сам
  window.dispatchEvent(new Event('auth-unauthorized'));
};

apiClient.interceptors.request.use(async (config) => {
  let token = getAccessToken();
  // Токен вот-вот истечёт — обновляем заранее, а не ловим 401 и повторяем запрос
  if (token && isAccessTokenExpired(token) && useAuthStore.getState().refreshToken) {
    try { token = (await refreshAccessToken()) ?? token; } catch { /* сервер недоступен — отправим как есть */ }
  }

  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
}, (error) => Promise.reject(error));

type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean };

// 401: access token живёт 60 минут — сначала обновляем его и повторяем запрос, выходим только если не вышло
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const config = error.config as RetriableConfig | undefined;
    if (error.response?.status !== 401 || !config) return Promise.reject(error);
    if (config._retried) {
      forceLogout();
      return Promise.reject(error);
    }
    config._retried = true;

    let token: string | null;
    try {
      token = await refreshAccessToken();
    } catch {
      return Promise.reject(error); // сервер недоступен — не разлогиниваем
    }
    if (!token) {
      forceLogout();
      return Promise.reject(error);
    }
    config.headers.Authorization = `Bearer ${token}`;
    return apiClient(config);
  }
);

export default apiClient;
