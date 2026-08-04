using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PerFunTracker.Core.Entities;
using PerFunTracker.Core.Options;
using PerFunTracker.Core.Services;
using PerFunTracker.Infrastructure.Auth;
using PerFunTracker.Infrastructure.Data;
using PerFunTracker.Infrastructure.Email;

namespace PerFunTracker.Infrastructure;

/// <summary>
/// DI registration helpers for the Infrastructure layer.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers PostgreSQL DbContext, ASP.NET Core Identity, JWT, and email services.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddIdentityCore<AppUser>(options =>
        {
            // Требуем подтверждение email перед входом — защита от фейковых регистраций.
            options.SignIn.RequireConfirmedEmail = true;

            // Lockout (блокировка) при многократном неудачном входе.
            // Защита от brute-force атак на пароль.
            // Алгоритм:
            // - Identity считает неудачные попытки входа через CheckPasswordSignInAsync.
            // - После MaxFailedAccessAttempts (5) неудач подряд пользователь
            //   блокируется на DefaultLockoutTimeSpan (5 минут).
            // - Успешный вход сбрасывает счётчик.
            // - Lockout действует на SignInManager, не на userManager — работает
            //   только через CheckPasswordSignInAsync, а не через CheckPasswordAsync.
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            // AddDefaultTokenProviders — регистрирует стандартные провайдеры
            // для генерации токенов (подтверждение email, сброс пароля, 2FA).
            // Каждый провайдер генерирует уникальный токен, который хранится
            // в AspNetUserTokens и проверяется при подтверждении.
            .AddDefaultTokenProviders()
            // AddSignInManager — регистрирует SignInManager, который:
            // - Проверяет пароль через PasswordHasher
            // - Поддерживает lockout (считает попытки)
            // - Поддерживает two-factor (если включено)
            // Без AddSignInManager нельзя использовать CheckPasswordSignInAsync
            .AddSignInManager<SignInManager<AppUser>>();

        // Required by SignInManager
        services.AddHttpContextAccessor();

        // Options
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));

        // Services
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IEmailNotificationQueue, ChannelEmailNotificationQueue>();

        return services;
    }
}
