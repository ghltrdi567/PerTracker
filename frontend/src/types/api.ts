// Типы данных, соответствующие API PerFunTracker

export interface ErrorResponse {
  errors: string[];
}

export interface AuthResponse {
  accessToken: string;
  expiresIn: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

// --- Категории ---

export interface Category {
  id: string;
  name: string;
  icon: string | null;
}

export interface CreateCategoryRequest {
  name: string;
  icon?: string | null;
}

// --- События ---

export const ParticipantRole = {
  Owner: 0,
  Member: 1,
} as const;

export type ParticipantRoleValue = (typeof ParticipantRole)[keyof typeof ParticipantRole];

export interface Participant {
  userId: string;
  displayName: string;
  email: string;
  role: number;
  joinedAt: string;
}

export interface EventResponse {
  id: string;
  title: string;
  description: string | null;
  ownerUserId: string;
  createdAt: string;
  category: Category | null;
  participants: Participant[];
}

export interface CreateEventRequest {
  title: string;
  description?: string | null;
  categoryId?: string | null;
}

export interface UpdateEventRequest {
  title: string;
  description?: string | null;
  categoryId?: string | null;
}

// --- Завершения ---

export interface Completion {
  id: string;
  completedByUserId: string;
  completedByName: string;
  completedAt: string;
  note: string | null;
}

export interface CreateCompletionRequest {
  note?: string | null;
}

// --- Настройки пользователя ---

export interface UserSettings {
  notifyByEmailOnCompletion: boolean;
}

export interface UpdateUserSettingsRequest {
  notifyByEmailOnCompletion: boolean;
}
