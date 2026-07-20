# PerFunTracker

Веб-приложение для трекинга привычек и повторяющихся событий. Пользователи создают события («Позвонить маме», «Вынести мусор», «Тренировка»), приглашают участников и отмечают завершения. Участники с включёнными уведомлениями получают email при каждом завершении.

## Возможности

- **Регистрация и аутентификация** — JWT access-токены + httpOnly refresh-cookie, подтверждение email, ротация refresh-токенов.
- **События** — создание, редактирование, удаление (только владелец), пагинированный список.
- **Участники** — приглашение по email (только владелец), выход из события, роли Owner/Member.
- **Завершения** — любой участник отмечает завершение с заметкой; история завершений в виде чата с пагинацией.
- **Категории** — пользовательские категории с эмодзи-иконками, уникальность по названию.
- **Email-уведомления** — фоновый сервис (BackgroundService) отправляет письма через SMTP-очередь.
- **Настройки** — каждый пользователь может включить/выключить email-уведомления о завершениях.

## Стек

| Слой | Технологии |
|------|-----------|
| Backend | .NET 10, ASP.NET Core Minimal API, EF Core, ASP.NET Core Identity, JWT |
| База данных | PostgreSQL 16 |
| Frontend | React 18, Vite, TypeScript, MUI (Material UI), React Router, Axios |
| Инфраструктура | Docker Compose, Caddy (reverse proxy для prod), MailHog (email в dev), nginx (отдача SPA) |

## Архитектура

```
PerTracker/
├── src/
│   ├── PerFunTracker.Core/          # Сущности, интерфейсы сервисов, опции
│   ├── PerFunTracker.Infrastructure/# EF Core, DbContext, Identity, реализация сервисов
│   └── PerFunTracker.Api/           # Minimal API endpoints, BackgroundServices, DTO
├── frontend/                        # React SPA (Vite)
├── docker-compose.yml               # dev и prod профили
├── Caddyfile                        # reverse proxy для prod
└── Dockerfile                       # multi-stage сборка API
```

Бэкенд разделён на 3 проекта по Clean Architecture: `Core` (домен) → `Infrastructure` (доступ к данным) → `Api` (HTTP-слои). Миграции EF Core применяются автоматически при старте.

## Развёртывание

### Требования

- Docker + Docker Compose

### Быстрый старт (dev-профиль)

```bash
docker compose --profile dev up -d --build
```

Поднимаются сервисы:

| Сервис | URL | Назначение |
|--------|-----|-----------|
| Frontend | `http://localhost:3000` | React SPA |
| API | `http://localhost:5134` | Бэкенд |
| PostgreSQL | `localhost:5432` | База данных |
| MailHog UI | `http://localhost:8025` | Просмотр писем (перехват SMTP) |
| MailHog SMTP | `localhost:1025` | SMTP для dev |

Проверка работоспособности API:

```bash
curl http://localhost:5134/health
```

В dev-режиме письма не уходят наружу — их перехватывает MailHog. После регистрации откройте `http://localhost:8025`, чтобы найти письмо со ссылкой подтверждения email.

### Prod-профиль (с Caddy и TLS)

```bash
docker compose --profile prod up -d --build
```

Caddy слушает порты 80/443 и проксирует трафик на `frontend:80` (nginx), который раздаёт статику SPA и проксирует `/api` и `/health` на бэкенд. Для автоматического HTTPS отредактируйте `Caddyfile`, указав домен вместо `:8080`.

### Переменные окружения

Конфигурация читается из `appsettings.json` и переопределяется переменными окружения (стандартный механизм .NET, `__` → вложенность).

Ключевые параметры для prod:

| Переменная | Описание | Значение по умолчанию |
|-----------|----------|----------------------|
| `ConnectionStrings__DefaultConnection` | Строка подключения к PostgreSQL | dev-строка |
| `Jwt__Key` | Base64-ключ подписи JWT — **замените в prod** | dev-ключ |
| `Jwt__Issuer` | Издатель токена | `PerFunTracker` |
| `Jwt__Audience` | Аудитория токена | `PerFunTracker` |
| `Jwt__AccessTokenMinutes` | Время жизни access-токена (мин) | `15` |
| `Jwt__RefreshTokenDays` | Время жизни refresh-токена (дни) | `7` |
| `Smtp__Host` | SMTP-хост | `mailhog` |
| `Smtp__Port` | SMTP-порт | `1025` |
| `Smtp__FromEmail` | Email отправителя | `noreply@perfuntracker.local` |
| `Smtp__FromName` | Имя отправителя | `PerFunTracker` |
| `ASPNETCORE_ENVIRONMENT` | Окружение | `Development` |

> ⚠️ **Безопасность:** в prod обязательно замените `Jwt__Key` и пароль PostgreSQL на собственные значения.

### Локальная разработка без Docker

**Backend:**
```bash
dotnet restore
dotnet ef database update -p src/PerFunTracker.Infrastructure -s src/PerFunTracker.Api
dotnet run --project src/PerFunTracker.Api
```

**Frontend:**
```bash
cd frontend
npm install
npm run dev    # http://localhost:5173
```

Для фронтенда скопируйте `frontend/.env.example` в `frontend/.env`. Пустой `VITE_API_BASE` = запросы через Vite proxy (рекомендуется для dev). Для prod укажите полный URL API.

## Использование

### 1. Регистрация

1. Откройте `http://localhost:3000` → страница **Register**.
2. Введите email, отображаемое имя и пароль (минимум 8 символов, 1 цифра, 1 заглавная и 1 строчная буква, 1 спецсимвол — правила ASP.NET Identity).
3. На email придёт письмо подтверждения. В dev — откройте `http://localhost:8025` и перейдите по ссылке.

### 2. Вход

После подтверждения email войдите на странице **Login**. Access-токен хранится в памяти, refresh-токен — в httpOnly cookie (7 дней). При перезагрузке страницы приложение автоматически обновляет access-токен через `/api/auth/refresh`.

### 3. События

- **Создать событие** — укажите название, описание (опц.) и категорию (опц.). Вы автоматически становитесь владельцем (Owner).
- **Пригласить участников** — на странице события введите email зарегистрированного пользователя. Участник получает роль Member.
- **Отметить завершение** — любой участник может отметить завершение с текстовой заметкой. Другим участникам с включёнными уведомлениями отправится email.
- **История завершений** — отображается в виде чата под событием, с пагинацией (по 50).

### 4. Роли участников

| Роль | Возможности |
|------|------------|
| **Owner** (создатель) | Редактировать/удалять событие, добавлять и удалять участников, отмечать завершения |
| **Member** (участник) | Отмечать завершения, выйти из события |

### 5. Категории

Создавайте категории с эмодзи-иконками («Тренировка 💪», «Скучаю по тебе 😢»). Названия уникальны (case-insensitive). Категории привязываются к событиям.

### 6. Настройки уведомлений

На странице **Settings** можно включить/выключить email-уведомления о завершениях событий другими участниками.

## API

Полная документация API — в [`docs/FRONTEND_SPEC.md`](docs/FRONTEND_SPEC.md). Краткая сводка:

| Метод | Путь | Описание |
|-------|------|----------|
| POST | `/api/auth/register` | Регистрация |
| GET | `/api/auth/confirm-email` | Подтверждение email |
| POST | `/api/auth/login` | Вход |
| POST | `/api/auth/refresh` | Обновление токена |
| POST | `/api/auth/logout` | Выход |
| GET | `/api/events` | Список событий (пагинация) |
| POST | `/api/events` | Создать событие |
| GET/PUT/DELETE | `/api/events/{id}` | Детали / обновить / удалить |
| POST | `/api/events/{id}/participants` | Добавить участника |
| DELETE | `/api/events/{id}/participants/{userId}` | Удалить участника |
| POST | `/api/events/{id}/complete` | Отметить завершение |
| GET | `/api/events/{id}/completions` | История завершений |
| GET/POST | `/api/categories` | Список / создать категорию |
| GET/PUT | `/api/users/me/settings` | Настройки уведомлений |
| GET | `/health` | Health check |

## Лицензия

Проект распространяется как есть, без явной лицензии. Все права принадлежат автору.
