import api, { setAccessToken } from './client';
import type { AuthResponse, LoginRequest, RegisterRequest } from '../types/api';

export async function register(data: RegisterRequest) {
  await api.post('/api/auth/register', data);
}

export async function login(data: LoginRequest) {
  const res = await api.post<AuthResponse>('/api/auth/login', data);
  setAccessToken(res.data.accessToken);
  return res.data;
}

export async function logout() {
  try {
    await api.post('/api/auth/logout', {});
  } finally {
    setAccessToken(null);
  }
}

export async function refresh() {
  const res = await api.post<AuthResponse>('/api/auth/refresh', {});
  setAccessToken(res.data.accessToken);
  return res.data;
}

export async function confirmEmail(userId: string, token: string) {
  const res = await api.get<string>('/api/auth/confirm-email', {
    params: { userId, token },
  });
  return res.data;
}
