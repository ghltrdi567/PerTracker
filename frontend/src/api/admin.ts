import api from './client';
import type { PagedResult } from '../types/api';

export interface AdminUser {
  id: string;
  email: string;
  displayName: string;
  isAdmin: boolean;
  emailConfirmed: boolean;
  notifyByEmailOnCompletion: boolean;
  createdAt: string;
}

export interface AdminEvent {
  id: string;
  title: string;
  description: string | null;
  ownerUserId: string;
  ownerDisplayName: string;
  ownerEmail: string;
  categoryName: string | null;
  createdAt: string;
}

export async function getAdminUsers(page = 1, pageSize = 50, search?: string) {
  const params: Record<string, string | number> = { page, pageSize };
  if (search) params.search = search;
  const res = await api.get<PagedResult<AdminUser>>('/api/admin/users', { params });
  return res.data;
}

export async function deleteAdminUser(userId: string) {
  await api.delete(`/api/admin/users/${userId}`);
}

export async function getAdminEvents(page = 1, pageSize = 50) {
  const res = await api.get<PagedResult<AdminEvent>>('/api/admin/events', {
    params: { page, pageSize },
  });
  return res.data;
}

export async function deleteAdminEvent(eventId: string) {
  await api.delete(`/api/admin/events/${eventId}`);
}