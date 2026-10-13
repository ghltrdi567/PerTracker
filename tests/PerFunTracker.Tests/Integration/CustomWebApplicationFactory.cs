using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PerFunTracker.Api.BackgroundServices;
using PerFunTracker.Core.Entities;
using PerFunTracker.Core.Services;
using PerFunTracker.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Custom WebApplicationFactory that spins up a Testcontainers PostgreSQL instance
/// and configures the API to use it instead of the real database.
/// Implements IAsyncLifetime for container lifecycle management.
///
/// Паттерн: Integration Testing с WebApplicationFactory + Testcontainers.
///
/// Как это работает:
/// 1. Testcontainers скачивает и запускает Docker-образ postgres:16-alpine.
/// 2. WebApplicationFactory создаёт тестовый хост API в памяти (без Kestrel).
/// 3. ConfigureServices заменяет реальный DbContext на тестовый (указывающий на контейнер).
/// 4. Миграции EF Core накатываются автоматически.
/// 5. HTTP-запросы идут через HttpClient к in-memory хосту — без реального TCP.
///
/// Если Docker недоступен:
/// - Конструктор НЕ падает (container = null).
/// - InitializeAsync ничего не делает.
/// - Тесты с [DockerFact] пропускаются с сообщением "Docker is not running".
///
/// Arrange-Act-Assert (AAA) паттерн:
/// - Arrange: CreateUserAsync / CreateOwnedEventAsync / CreateParticipantClientAsync
///   (хелперы в этом классе, создают данные в БД и возвращают HttpClient с JWT)
/// - Act: HTTP-запрос к API (GET/POST/PUT/DELETE)
/// - Assert: проверка статус-кода и тела ответа
///
/// IAsyncLifetime:
/// - InitializeAsync: запускает Docker-контейнер до первого теста.
/// - DisposeAsync: останавливает контейнер после последнего теста.
/// - IClassFixture: один контейнер на класс тестов (не на каждый тест).
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer? _container;

    /// <summary>
    /// Конструктор не должен падать при отсутствии Docker.
    /// Если Build() выбрасывает исключение, _container = null,
    /// и тесты с [DockerFact] будут пропущены.
    /// </summary>
    public CustomWebApplicationFactory()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("perfuntracker_test")
                .WithUsername("perfun_test")
                .WithPassword("perfun_test_pass")
                .WithCleanUp(true)
                .Build();
        }
        catch
        {
            // Docker is not available — tests will be skipped by [DockerFact]
            _container = null;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            if (_container is not null)
            {
                // Add test DbContext pointing to the Testcontainers PostgreSQL
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseNpgsql(_container.GetConnectionString()));

                // Ensure the database is created and migrations are applied
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Database.Migrate();
            }
            else
            {
                // Fallback to in-memory database when Docker is not available.
                // This allows the factory to be created, but tests will fail on DB operations.
                // Tests should use [DockerFact] to skip gracefully.
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase("PerFunTrackerTest"));
            }

            // Replace IEmailSender with a no-op stub to avoid needing MailHog during tests.
            // The confirmation email is sent during registration; without this stub,
            // tests fail because localhost:1025 (MailHog) is not available in CI/test env.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sp =>
                new StubEmailSender());

            // Remove the EmailNotificationWorker hosted service: it dequeues from the
            // same singleton Channel that tests read directly. With the worker running
            // (CI: Docker present), it races NotificationIntegrationTests for messages.
            var emailWorker = services.SingleOrDefault(
                d => d.ImplementationType == typeof(EmailNotificationWorker));
            if (emailWorker is not null)
                services.Remove(emailWorker);
        });

        // Use test environment
        builder.UseEnvironment("Test");
    }

    /// <summary>
    /// Creates a test user with the given email and password, confirms their email.
    /// Returns the UserManager for further operations.
    /// </summary>
    public async Task<(AppUser User, UserManager<AppUser> UserManager)> CreateUserAsync(
        string email = "test@example.com",
        string password = "TestPass123!",
        string displayName = "Test User")
    {
        var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create test user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        // Confirm email
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await userManager.ConfirmEmailAsync(user, token);

        return (user, userManager);
    }

    /// <summary>
    /// Logs in as the given user and returns an HttpClient with the JWT token set.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string email = "test@example.com",
        string password = "TestPass123!")
    {
        var client = CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password
        });

        loginResponse.EnsureSuccessStatusCode();

        // Read the access token from the response body
        var content = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = content.GetProperty("accessToken").GetString()!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>
    /// Creates a user, creates an event with them as owner, and returns the event ID.
    /// </summary>
    public async Task<(HttpClient Client, Guid EventId)> CreateOwnedEventAsync(
        string email = "owner@example.com",
        string password = "OwnerPass123!",
        string eventTitle = "Test Event")
    {
        await CreateUserAsync(email, password, "Owner User");
        var client = await CreateAuthenticatedClientAsync(email, password);

        var createResponse = await client.PostAsJsonAsync("/api/events", new
        {
            title = eventTitle,
            description = "Test description"
        });

        createResponse.EnsureSuccessStatusCode();
        var content = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = content.GetProperty("id").GetGuid();

        return (client, eventId);
    }

    /// <summary>
    /// Creates a second user, adds them as a participant to an event.
    /// </summary>
    public async Task<HttpClient> CreateParticipantClientAsync(
        Guid eventId,
        string email = "member@example.com",
        string password = "MemberPass123!",
        string displayName = "Member User",
        string ownerEmail = "owner@example.com",
        string ownerPassword = "OwnerPass123!")
    {
        await CreateUserAsync(email, password, displayName);
        var memberClient = await CreateAuthenticatedClientAsync(email, password);

        // Owner adds the member to the event
        var ownerClient = await CreateAuthenticatedClientAsync(ownerEmail, ownerPassword);
        var addResponse = await ownerClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email
        });
        addResponse.EnsureSuccessStatusCode();

        return memberClient;
    }

    // IAsyncLifetime
    public async Task InitializeAsync()
    {
        if (_container is not null)
            await _container.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.StopAsync();
    }
}

/// <summary>
/// No-op email sender that silently discards all emails.
/// Used during integration tests to avoid needing a real SMTP server (MailHog).
/// </summary>
public class StubEmailSender : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        // Silently discard — no SMTP connection needed
        return Task.CompletedTask;
    }
}