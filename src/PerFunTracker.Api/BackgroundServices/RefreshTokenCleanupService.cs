using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerFunTracker.Infrastructure.Data;

namespace PerFunTracker.Api.BackgroundServices;

/// <summary>
/// Nightly background service that purges expired/revoked refresh tokens
/// to prevent unbounded table growth.
///
/// Паттерн BackgroundService:
/// - ExecuteAsync выполняется в фоновом потоке на протяжении жизни приложения.
/// - Бесконечный цикл: Task.Delay → работа → снова Task.Delay.
/// - CancellationToken передаётся при остановке приложения (graceful shutdown).
///
/// Почему ExecuteDeleteAsync, а не SaveChanges + RemoveRange:
/// - ExecuteDeleteAsync — это массовое удаление (одна SQL-команда DELETE WHERE ...).
/// - RemoveRange + SaveChanges — загружает каждую запись в память, потом удаляет по одной.
/// - Для тысяч записей ExecuteDeleteAsync в сотни раз быстрее.
/// - Недостаток: не загружает сущности в память, поэтому нельзя триггерить
///   события EF Core (события OnDeleting и т.д.). Для cleanup это не нужно.
/// </summary>
public class RefreshTokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupService> _logger;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    public RefreshTokenCleanupService(IServiceScopeFactory scopeFactory, ILogger<RefreshTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Refresh token cleanup service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var cutoff = DateTime.UtcNow;

                // Delete tokens that are expired OR revoked (inactive)
                var deleted = await db.RefreshTokens
                    .Where(rt => rt.ExpiresAt < cutoff || rt.RevokedAt != null)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deleted > 0)
                {
                    _logger.LogInformation("Purged {Count} expired/revoked refresh tokens.", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge refresh tokens.");
            }
        }

        _logger.LogInformation("Refresh token cleanup service stopped.");
    }
}
