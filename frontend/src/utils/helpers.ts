import type { AxiosError } from 'axios';
import type { ErrorResponse } from '../types/api';

// Извлекает массив сообщений об ошибках из ответа API.
export function getApiErrors(error: unknown): string[] {
  const err = error as AxiosError<ErrorResponse>;
  if (err.response?.data?.errors?.length) {
    return err.response.data.errors;
  }
  if (err.response?.status === 401) {
    return ['Неверный email или пароль, либо email не подтверждён.'];
  }
  if (err.response?.status === 403) {
    return ['Недостаточно прав для выполнения действия.'];
  }
  if (err.response?.status === 404) {
    return ['Ресурс не найден.'];
  }
  return ['Произошла ошибка. Попробуйте позже.'];
}

// Форматирует ISO-дату в читаемый вид с учётом часового пояса устройства.
export function formatDate(iso: string): string {
  return new Date(iso).toLocaleString('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZoneName: 'short',
  });
}

// Возвращает метку дня для разделителя в чате: «Сегодня», «Вчера» или дату.
// Сравнение происходит в локальном часовом поясе устройства.
export function getDayLabel(iso: string): string {
  const date = new Date(iso);
  const now = new Date();

  const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const startOfYesterday = new Date(startOfToday);
  startOfYesterday.setDate(startOfYesterday.getDate() - 1);

  const startOfDate = new Date(date.getFullYear(), date.getMonth(), date.getDate());

  if (startOfDate.getTime() === startOfToday.getTime()) {
    return 'Сегодня';
  }
  if (startOfDate.getTime() === startOfYesterday.getTime()) {
    return 'Вчера';
  }

  return date.toLocaleDateString('ru-RU', {
    day: 'numeric',
    month: 'long',
    year: now.getFullYear() === date.getFullYear() ? undefined : 'numeric',
  });
}
