import api from './client';

// Публичный конфиг приложения (GET /api/config).
// seeAllEvents — дебаг-режим: админ видит все события;
// пока включён, UI показывает об этом предупреждение.
export interface AppConfig {
  seeAllEvents: boolean;
}

export async function getAppConfig(): Promise<AppConfig> {
  const res = await api.get<AppConfig>('/api/config');
  return res.data;
}
