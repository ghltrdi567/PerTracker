import api from './client';
import type {
  Completion,
  CreateCompletionRequest,
  CreateEventRequest,
  EventResponse,
  PagedResult,
  UpdateEventRequest,
} from '../types/api';

export async function getEvents(page = 1, pageSize = 20) {
  const res = await api.get<PagedResult<EventResponse>>('/api/events', {
    params: { page, pageSize },
  });
  return res.data;
}

export async function getEvent(id: string) {
  const res = await api.get<EventResponse>(`/api/events/${id}`);
  return res.data;
}

export async function createEvent(data: CreateEventRequest) {
  const res = await api.post<EventResponse>('/api/events', data);
  return res.data;
}

export async function updateEvent(id: string, data: UpdateEventRequest) {
  const res = await api.put<EventResponse>(`/api/events/${id}`, data);
  return res.data;
}

export async function deleteEvent(id: string) {
  await api.delete(`/api/events/${id}`);
}

export async function addParticipant(eventId: string, email: string) {
  const res = await api.post(`/api/events/${eventId}/participants`, { email });
  return res.data;
}

export async function removeParticipant(eventId: string, userId: string) {
  await api.delete(`/api/events/${eventId}/participants/${userId}`);
}

export async function completeEvent(eventId: string, data: CreateCompletionRequest) {
  const res = await api.post<Completion>(`/api/events/${eventId}/complete`, data);
  return res.data;
}

export async function getCompletions(eventId: string, page = 1, pageSize = 50) {
  const res = await api.get<PagedResult<Completion>>(
    `/api/events/${eventId}/completions`,
    { params: { page, pageSize } },
  );
  return res.data;
}
