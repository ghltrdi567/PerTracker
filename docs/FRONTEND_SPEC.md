# PerFunTracker — Спецификация для разработки фронтенда

## Обзор проекта

PerFunTracker — API для трекинга привычек/событий. Пользователи создают события (например «Позвонить маме», «Вынести мусор»), приглашают участников и отмечают завершения. Участники получают email-уведомления при завершении.

**Стек бэкенда:** .NET 10, PostgreSQL 16, EF Core, ASP.NET Core Identity, JWT.

**Базовый URL API:** `http://localhost:5134` (через Docker), либо `http://localhost:8080` через Caddy в prod.

**Рекомендуемый стек фронтенда:** React + Vite + TypeScript. CORS уже настроен для `http://localhost:5173` и `http://localhost:3000`.

---

## Содержание

1. [Аутентификация](#1-аутентификация)
2. [Управление событиями](#2-управление-событиями)
3. [Участники](#3-участники)
4. [Завершения событий](#4-завершения-событий)
5. [Категории](#5-категории)
6. [Настройки пользователя](#6-настройки-пользователя)
7. [Типы данных](#7-типы-данных)
8. [Пагинация](#8-пагинация)
9. [Обработка ошибок](#9-обработка-ошибок)
10. [Локальный запуск](#10-локальный-запуск)

---

## 1. Аутентификация

### Механизм

Используется двойная токенная схема:
- **Access token** (JWT) — возвращается в JSON-теле ответа. Хранится в памяти фронтенда (не в localStorage для безопасности). Время жизни — 15 минут.
- **Refresh token** — устанавливается как **httpOnly cookie** сервером. Фронтенд не имеет доступа к этому cookie через JS — браузер автоматически отправляет его при запросах к `/api/auth/*`. Время жизни — 7 дней.

Access token передаётся в заголовке:
```
Authorization: Bearer <accessToken>
```

Когда access token истекает (401), фронтенд должен вызвать `POST /api/auth/refresh` — браузер автоматически приложит refresh cookie. В ответ придёт новый access token, а cookie обновится с новым refresh токеном (ротация).

### 1.1. Регистрация

```
POST /api/auth/register
```

**Тело запроса:**
```json
{
  "email": "user@example.com",
  "password": "Test1234!",
  "displayName": "Alice"
}
```

**Успех:** `201 Created` — пустое тело. На email отправляется письмо с ссылкой подтверждения.

**Ошибки:** `400 Bad Request`
```json
{
  "errors": [
    "Email 'user@example.com' is already taken.",
    "Password must be at least 8 characters."
  ]
}
```

> **Важно для фронтенда:** Пароли должны соответствовать правилам ASP.NET Identity по умолчанию: минимум 8 символов, минимум 1 цифра, минимум 1 символ в верхнем и нижнем регистре, минимум 1 небуквенно-цифровой символ.

### 1.2. Подтверждение email

```
GET /api/auth/confirm-email?userId=<string>&token=<string>
```

Пользователь переходит по ссылке из письма. Ссылка указывает на API, не на фронтенд.

**Успех:** `200 OK` — текст `"Email confirmed successfully."`

**Ошибки:** `400 Bad Request` — текст `"Invalid user."` или `"Invalid or expired token."`

> **Рекомендация для фронтенда:** Можно перехватить подтверждение — отправлять ссылку из письма на фронтенд (`/confirm-email?userId=...&token=...`), а фронтенд проксирует запрос к API. Но сейчас ссылка ведёт напрямую на API.

### 1.3. Логин

```
POST /api/auth/login
```

**Тело запроса:**
```json
{
  "email": "user@example.com",
  "password": "Test1234!"
}
```

**Успех:** `200 OK`
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresIn": 900
}
```
- `expiresIn` — время жизни access token в секундах (900 = 15 минут).
- Параллельно сервер устанавливает httpOnly cookie `refreshToken`.

**Ошибки:**
- `401 Unauthorized` — неверный email или пароль.
- `400 Bad Request` — email не подтверждён:
```json
{ "errors": ["Email not confirmed."] }
```

### 1.4. Обновление токена

```
POST /api/auth/refresh
```

**Тело запроса:** пустое. Refresh token берётся из cookie (браузер отправляет автоматически).

> **Важно:** Для работы cookie нужен заголовок `credentials: 'include'` в fetch/axios. Без него cookie не отправится.

**Успех:** `200 OK`
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresIn": 900
}
```
Cookie `refreshToken` обновляется (старый токен отзывается, выдаётся новый).

**Ошибки:** `401 Unauthorized` — refresh token отсутствует, истёк или отозван.

### 1.5. Выход

```
POST /api/auth/logout
```

**Тело запроса:** пустое.

**Успех:** `200 OK` — пустое тело. Refresh token отзывается в БД, cookie удаляется.

> Не требует access token, но требует refresh cookie.

---

## 2. Управление событиями

Все endpoints в этой группе требуют заголовок `Authorization: Bearer <accessToken>`.

### 2.1. Список событий (пагинация)

```
GET /api/events?page=1&pageSize=20
```

**Query-параметры:**
| Параметр | По умолчанию | Максимум | Описание |
|----------|-------------|----------|----------|
| `page`   | 1           | —        | Номер страницы (начинается с 1) |
| `pageSize` | 20        | 100      | Размер страницы (clamped to [1, 100]) |

Возвращает только события, в которых текущий пользователь является участником.

**Успех:** `200 OK`
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "title": "Позвонить маме",
      "description": "Каждый день в 18:00",
      "ownerUserId": "abc123-def456",
      "createdAt": "2026-07-17T12:00:00Z",
      "category": {
        "id": "guid-here",
        "name": "Скучаю по тебе",
        "icon": "😢"
      },
      "participants": [
        {
          "userId": "abc123-def456",
          "displayName": "Alice",
          "email": "alice@test.com",
          "role": 0,
          "joinedAt": "2026-07-17T12:00:00Z"
        }
      ]
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 42,
  "totalPages": 3
}
```

> `category` может быть `null` — событие без категории.
> `role`: `0` = Owner, `1` = Member.

### 2.2. Создать событие

```
POST /api/events
```

**Тело запроса:**
```json
{
  "title": "Позвонить маме",
  "description": "Каждый день в 18:00",
  "categoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

- `title` — обязательно, максимум 200 символов.
- `description` — опционально, максимум 2000 символов.
- `categoryId` — опционально, `null` = без категории.

Создатель автоматически становится участником с ролью Owner.

**Успех:** `201 Created` — возвращает созданное событие (формат `EventResponse`).

### 2.3. Получить событие по ID

```
GET /api/events/{id}
```

**Успех:** `200 OK` — `EventResponse` (с participant'ами и category).

**Ошибки:** `404 Not Found` — событие не существует **или** текущий пользователь не участник (в целях безопасности существование события скрыто от не-участников).

### 2.4. Обновить событие

```
PUT /api/events/{id}
```

**Тело запроса:**
```json
{
  "title": "Новое название",
  "description": "Новое описание",
  "categoryId": null
}
```

**Успех:** `200 OK` — обновлённый `EventResponse`.

**Ошибки:**
- `404 Not Found` — событие не найдено.
- `403 Forbidden` — текущий пользователь не владелец.

> Только Owner может редактировать событие.

### 2.5. Удалить событие

```
DELETE /api/events/{id}
```

**Успех:** `204 No Content` — пустое тело.

**Ошибки:**
- `404 Not Found`
- `403 Forbidden` — не владелец.

> Только Owner может удалить событие. Удаление каскадное: удаляются все участники и завершения.

---

## 3. Участники

### 3.1. Добавить участника

```
POST /api/events/{id}/participants
```

**Тело запроса:**
```json
{
  "email": "bob@test.com"
}
```

Пользователь с таким email должен быть уже зарегистрирован и подтверждён.

**Успех:** `200 OK`
```json
{
  "userId": "user-id-string",
  "displayName": "Bob",
  "email": "bob@test.com",
  "role": 1,
  "joinedAt": "2026-07-17T12:30:00Z"
}
```

**Ошибки:**
- `404 Not Found` — событие не найдено.
- `403 Forbidden` — не владелец.
- `400 Bad Request`:
```json
{ "errors": ["User with this email not found. They must register first."] }
```
или
```json
{ "errors": ["User is already a participant."] }
```

> Только Owner может добавлять участников. Новый участник получает роль Member.

### 3.2. Удалить участника

```
DELETE /api/events/{id}/participants/{userId}
```

- Owner может удалить любого участника (кроме себя-Owner).
- Member может удалить только себя (выйти из события).

**Успех:** `204 No Content`

**Ошибки:**
- `404 Not Found` — событие или участник не найден.
- `403 Forbidden` — Member пытается удалить другого.
- `400 Bad Request`:
```json
{ "errors": ["Cannot remove the event owner. Delete the event instead."] }
```

---

## 4. Завершения событий

### 4.1. Отметить завершение

```
POST /api/events/{id}/complete
```

**Тело запроса:**
```json
{
  "note": "Сделано! Бегал 30 минут"
}
```

- `note` — опционально, максимум 1000 символов.

Любой участник события может отметить завершение. После сохранения:
- Другим участникам с включёнными уведомлениями отправляется email (через фоновую очередь).
- Email содержит название события, имя завершившего, время и заметку.

**Успех:** `201 Created`
```json
{
  "id": "completion-guid",
  "completedByUserId": "user-id",
  "completedByName": "Alice",
  "completedAt": "2026-07-17T18:00:00Z",
  "note": "Сделано! Бегал 30 минут"
}
```

**Ошибки:** `404 Not Found` — событие не найдено или пользователь не участник.

### 4.2. История завершений (пагинация)

```
GET /api/events/{id}/completions?page=1&pageSize=50
```

**Query-параметры:**
| Параметр | По умолчанию | Максимум |
|----------|-------------|----------|
| `page`   | 1           | —        |
| `pageSize` | 50        | 100      |

Сортировка — от новых к старым (`CompletedAt DESC`).

**Успех:** `200 OK`
```json
{
  "items": [
    {
      "id": "completion-guid",
      "completedByUserId": "user-id",
      "completedByName": "Alice",
      "completedAt": "2026-07-17T18:00:00Z",
      "note": "Сделано!"
    }
  ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 10,
  "totalPages": 1
}
```

**Ошибки:** `404 Not Found` — событие не найдено или пользователь не участник.

---

## 5. Категории

Категории хранятся в БД и могут создаваться пользователями в любой момент.

### 5.1. Список категорий (пагинация)

```
GET /api/categories?page=1&pageSize=50
```

**Успех:** `200 OK`
```json
{
  "items": [
    {
      "id": "guid-here",
      "name": "Скучаю по тебе",
      "icon": "😢"
    },
    {
      "id": "another-guid",
      "name": "Какашки",
      "icon": "💩"
    }
  ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 2,
  "totalPages": 1
}
```

> `icon` может быть `null`.

### 5.2. Создать категорию

```
POST /api/categories
```

**Тело запроса:**
```json
{
  "name": "Тренировка",
  "icon": "💪"
}
```

- `name` — обязательно, максимум 100 символов, уникальное (case-insensitive).
- `icon` — опционально, максимум 50 символов (эмодзи или короткая строка).

**Успех:** `201 Created`
```json
{
  "id": "new-guid",
  "name": "Тренировка",
  "icon": "💪"
}
```

**Ошибки:** `400 Bad Request`
```json
{ "errors": ["A category with this name already exists."] }
```

---

## 6. Настройки пользователя

### 6.1. Получить настройки

```
GET /api/users/me/settings
```

**Успех:** `200 OK`
```json
{
  "notifyByEmailOnCompletion": true
}
```

### 6.2. Обновить настройки

```
PUT /api/users/me/settings
```

**Тело запроса:**
```json
{
  "notifyByEmailOnCompletion": false
}
```

**Успех:** `200 OK` — возвращает обновлённое значение.

> Когда `false`, пользователь не получает email-уведомления при завершении событий другими участниками.

---

## 7. Типы данных

### ParticipantRole (enum)

| Значение | Имя     | Описание |
|----------|---------|----------|
| `0`      | Owner   | Создатель события, полный доступ |
| `1`      | Member  | Участник, может отмечать завершения и выходить |

В JSON передаётся как число (`"role": 0`).

### Даты

Все даты в формате ISO 8601 UTC (например `"2026-07-17T18:00:00Z"`).

### Идентификаторы

- `TrackerEvent.Id` — GUID (string в JSON).
- `Category.Id` — GUID.
- `EventCompletion.Id` — GUID.
- `AppUser.Id` — string (ASP.NET Identity использует string IDs).
- `EventParticipant` — составной ключ `(EventId, UserId)`.

---

## 8. Пагинация

Все list-endpoints возвращают единый формат `PagedResult<T>`:

```typescript
interface PagedResult<T> {
  items: T[];
  page: number;        // текущая страница (начинается с 1)
  pageSize: number;    // размер страницы
  totalCount: number;  // всего элементов
  totalPages: number;  // всего страниц
}
```

**Endpoints с пагинацией:**
| Endpoint | Default pageSize | Max pageSize |
|----------|-----------------|--------------|
| `GET /api/events` | 20 | 100 |
| `GET /api/events/{id}/completions` | 50 | 100 |
| `GET /api/categories` | 50 | 100 |

`pageSize` ограничен сверху 100 — сервер автоматически ограничивает (clamp to [1, 100]).

---

## 9. Обработка ошибок

### ErrorResponse

Все ошибки валидации/бизнес-логики возвращают единый формат:

```json
{
  "errors": [
    "Описание ошибки 1",
    "Описание ошибки 2"
  ]
}
```

```typescript
interface ErrorResponse {
  errors: string[];
}
```

### Коды ответов

| Код | Значение | Когда |
|-----|----------|-------|
| `200` | OK | Успешный GET/PUT |
| `201` | Created | Успешный POST (создание) |
| `204` | No Content | Успешный DELETE |
| `400` | Bad Request | Ошибка валидации, бизнес-правило |
| `401` | Unauthorized | Нет/неверный access token, неверный логин |
| `403` | Forbidden | Нет прав (не Owner) |
| `404` | Not Found | Ресурс не существует или скрыт |

### 401 vs 403

- `401` — не аутентифицирован (нет токена, токен истёк).
- `403` — аутентифицирован, но нет прав (например, Member пытается редактировать событие).

---

## 10. Локальный запуск

### Через Docker (рекомендуется)

```bash
docker compose --profile dev up -d --build
```

Сервисы:
| Сервис | URL | Назначение |
|--------|-----|-----------|
| API | `http://localhost:5134` | Бэкенд |
| PostgreSQL | `localhost:5432` | База данных |
| MailHog SMTP | `localhost:1025` | Перехват email |
| MailHog UI | `http://localhost:8025` | Просмотр писем в браузере |

### Проверка работоспособности

```
GET http://localhost:5134/health
```

Ответ: `Healthy`

### CORS

API разрешает запросы с `credentials` из:
- `http://localhost:5173` (Vite по умолчанию)
- `http://localhost:3000`

---

## Рекомендации для фронтенда

### Хранение access token

Храните access token **в памяти** (в переменной/state), не в localStorage. При перезагрузке страницы токен теряется — это нормально, фронтенд должен вызвать `POST /api/auth/refresh` для получения нового.

### Refresh-цикл

```
Приложение загружается
  ↓
Есть access token в памяти?
  ├─ Да → делаем запросы с Authorization: Bearer
  └─ Нет → POST /api/auth/refresh (cookie отправится автоматически)
            ↓
            200 → сохраняем новый access token
            401 → редирект на /login
```

При получении `401` на любом запросе:
1. Попробовать `POST /api/auth/refresh`.
2. Если refresh вернул `200` — повторить исходный запрос с новым токеном.
3. Если refresh вернул `401` — редирект на страницу логина.

### Заголовок credentials

Все запросы должны включать:
```javascript
fetch(url, { credentials: 'include' })
// или
axios.defaults.withCredentials = true;
```

Без этого cookie с refresh token не будет отправляться.

### Email-подтверждение при разработке

В dev-режиме письма перехватываются MailHog. После регистрации откройте `http://localhost:8025` — там будет письмо со ссылкой подтверждения.

### Предзаполнение категорий

При первом запуске БД пустая — категорий нет. Фронтенд может:
- Показывать пустой список с кнопкой «Создать категорию».
- Или предложить предустановленные категории при онбординге (создать через `POST /api/categories`).

### UI для ролей участников

- **Owner** (role = 0): может редактировать событие, удалять, добавлять/удалять участников.
- **Member** (role = 1): может отмечать завершения и выйти из события.
- Фронтенд должен скрывать кнопки редактирования/управления для Member'ов.

---

## Сводная таблица всех endpoints

| Метод | Путь | Auth | Описание |
|-------|------|------|----------|
| POST | `/api/auth/register` | — | Регистрация |
| GET | `/api/auth/confirm-email` | — | Подтверждение email (query: userId, token) |
| POST | `/api/auth/login` | — | Логин |
| POST | `/api/auth/refresh` | cookie | Обновление токена |
| POST | `/api/auth/logout` | cookie | Выход |
| GET | `/api/events` | Bearer | Список событий (пагинация) |
| POST | `/api/events` | Bearer | Создать событие |
| GET | `/api/events/{id}` | Bearer | Детали события |
| PUT | `/api/events/{id}` | Bearer | Обновить (Owner) |
| DELETE | `/api/events/{id}` | Bearer | Удалить (Owner) |
| POST | `/api/events/{id}/participants` | Bearer | Добавить участника (Owner) |
| DELETE | `/api/events/{id}/participants/{userId}` | Bearer | Удалить участника |
| POST | `/api/events/{id}/complete` | Bearer | Отметить завершение |
| GET | `/api/events/{id}/completions` | Bearer | История завершений (пагинация) |
| GET | `/api/categories` | Bearer | Список категорий (пагинация) |
| POST | `/api/categories` | Bearer | Создать категорию |
| GET | `/api/users/me/settings` | Bearer | Настройки уведомлений |
| PUT | `/api/users/me/settings` | Bearer | Обновить настройки |
| GET | `/health` | — | Health check |
