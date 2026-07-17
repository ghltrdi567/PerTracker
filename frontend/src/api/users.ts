import api from './client';
import type { UpdateUserSettingsRequest, UserSettings } from '../types/api';

export async function getSettings() {
  const res = await api.get<UserSettings>('/api/users/me/settings');
  return res.data;
}

export async function updateSettings(data: UpdateUserSettingsRequest) {
  const res = await api.put<UserSettings>('/api/users/me/settings', data);
  return res.data;
}
