using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using PerFunTracker.Api.BackgroundServices;
using PerFunTracker.Api.Endpoints;
using PerFunTracker.Core.Options;
using PerFunTracker.Infrastructure;
using PerFunTracker.Infrastructure.Data;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Debug-флаги (env Debug__SeeAllEvents=true) — значение задаётся при деплое,
// в репо всегда false. Singleton, чтобы endpoints получали флаг через DI.
builder.Services.AddSingleton(
    builder.Configuration.GetSection(DebugOptions.SectionName).Get<DebugOptions>() ?? new DebugOptions());

// JWT authentication
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtOptions.Key)),
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireClaim("is_admin", "true"));
});

// CORS for the React SPA.
// Origins из конфигурации (env Cors__Origins, запятая-разделённые) — для деплоя
// SPA и API на разные домены (Vercel → Render). Локально — дефолт ниже.
var corsOrigins = (builder.Configuration["Cors:Origins"] ?? "http://localhost:5173,http://localhost:3000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials()
              .WithOrigins(corsOrigins));
});

// Rate limiting: strict fixed-window limiter on auth endpoints (brute-force protection)
// In the "Test" environment, the limit is much higher to avoid interfering with integration tests.
builder.Services.AddRateLimiter(options =>
{
    var isTest = builder.Environment.IsEnvironment("Test");
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = isTest ? 1000 : 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Пустой AddHealthChecks() всегда отвечает 200 — оркестратор считает
// сломанный инстанс живым. Проверяем доступность БД.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddHostedService<EmailNotificationWorker>();
builder.Services.AddHostedService<RefreshTokenCleanupService>();

var app = builder.Build();

// --- Middleware pipeline — порядок ВАЖЕН! ---
//
// 1. UseRateLimiter — проверяет лимиты запросов ДО аутентификации.
//    Цель: блокировать брутфорс-атаки на /api/auth/login без затрат на расшифровку JWT.
//    RateLimiter смотрит на IP-адрес (по умолчанию) — аутентификация не нужна.
//
// 2. UseCors — устанавливает CORS-заголовки до авторизации.
//    Цель: браузер отправляет preflight (OPTIONS) без Authorization header.
//    CORS middleware должен ответить на OPTIONS до того, как Authentication
//    отклонит запрос из-за отсутствия токена.
//
// 3. UseAuthentication — расшифровывает JWT, заполняет HttpContext.User identity.
//    Если токен невалиден или истёк — User остаётся анонимным.
//
// 4. UseAuthorization — проверяет политики (RequireAuthorization, RequireClaim).
//    Если у User нет нужного claim — 401 Unauthorized или 403 Forbidden.
app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/", () => "PerFunTracker API");
// Публичный конфиг для SPA: фронтенд показывает баннер, пока включён дебаг-режим.
// Анонимно намеренно — режим должен быть виден всем пользователям.
app.MapGet("/api/config", (DebugOptions debug) => TypedResults.Ok(new { seeAllEvents = debug.SeeAllEvents }));
app.MapAuthEndpoints();
app.MapEventEndpoints();
app.MapUserEndpoints();
app.MapCategoryEndpoints();
app.MapAdminEndpoints();

// Auto-apply EF Core migrations on startup.
//
// В dev-среде это удобно: поднял Docker → миграции накатились → можно работать.
// В production-среде должен быть отдельный шаг (dotnet ef database update / CI/CD).
// Автоматические миграции в production опасны: если миграция упадёт,
// приложение не запустится. Для production лучше контролировать процесс вручную.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();

/// <summary>Health check: доступна ли база (CanConnectAsync через scope DI).</summary>
internal sealed class DatabaseHealthCheck(ApplicationDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Database reachable")
            : HealthCheckResult.Unhealthy("Database unreachable");
}
