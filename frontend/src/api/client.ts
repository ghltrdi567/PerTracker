import axios, { type AxiosInstance, type InternalAxiosRequestConfig } from 'axios';
import type { AuthResponse } from '../types/api';

const API_BASE = import.meta.env.VITE_API_BASE ?? '';

// Access token хранится в памяти (не в localStorage) для безопасности.
let accessToken: string | null = null;

// Колбэк, вызываемый при неудачном refresh (для редиректа на логин).
let onAuthFailure: (() => void) | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function getAccessToken() {
  return accessToken;
}

export function setOnAuthFailure(cb: () => void) {
  onAuthFailure = cb;
}

// Флаг предотвращает параллельные refresh-запросы.
let refreshPromise: Promise<string | null> | null = null;

async function refreshToken(): Promise<string | null> {
  if (refreshPromise) return refreshPromise;
  refreshPromise = (async () => {
    try {
      const res = await axios.post<AuthResponse>(
        `${API_BASE}/api/auth/refresh`,
        {},
        { withCredentials: true },
      );
      setAccessToken(res.data.accessToken);
      return res.data.accessToken;
    } catch {
      setAccessToken(null);
      onAuthFailure?.();
      return null;
    } finally {
      refreshPromise = null;
    }
  })();
  return refreshPromise;
}

const api: AxiosInstance = axios.create({
  baseURL: API_BASE,
  withCredentials: true,
});

// Добавляем Authorization на каждый запрос.
api.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }
  return config;
});

// При 401 — пробуем refresh и повторяем запрос.
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;
    if (error.response?.status === 401 && !originalRequest._retry) {
      // Не ретраим refresh-запрос при его собственной неудаче.
      if (originalRequest.url?.includes('/api/auth/refresh')) {
        setAccessToken(null);
        onAuthFailure?.();
        return Promise.reject(error);
      }
      originalRequest._retry = true;
      const newToken = await refreshToken();
      if (newToken) {
        originalRequest.headers.Authorization = `Bearer ${newToken}`;
        return api(originalRequest);
      }
      return Promise.reject(error);
    }
    return Promise.reject(error);
  },
);

export default api;
