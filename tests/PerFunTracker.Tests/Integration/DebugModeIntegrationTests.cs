using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerFunTracker.Infrastructure.Data;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Фабрика с включённым дебаг-режимом. В реальном деплое флаг ставится
/// через env (Debug__SeeAllEvents=true), не через appsettings — здесь
/// эмулируем это через UseSetting (in-memory config поверх appsettings.json).
/// </summary>
public class DebugWebApplicationFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Debug:SeeAllEvents", "true");
    }
}

/// <summary>
/// Дебаг-режим ВКЛ: админ видит все события (список/детали/завершения),
/// обычные пользователи — по-прежнему только свои.
/// </summary>
public class DebugModeOnIntegrationTests : IClassFixture<DebugWebApplicationFactory>
{
    private readonly DebugWebApplicationFactory _factory;

    public DebugModeOnIntegrationTests(DebugWebApplicationFactory factory) => _factory = factory;

    // Тот же хелпер, что в AdminIntegrationTests: создать пользователя,
    // повысить до админа через БД, залогиниться и получить клиент с JWT
    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(adminEmail, "AdminPass123!", "Admin User");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == adminEmail);
        user.IsAdmin = true;
        await db.SaveChangesAsync();

        return await _factory.CreateAuthenticatedClientAsync(adminEmail, "AdminPass123!");
    }

    private async Task<Guid> CreateForeignEventAsync()
        => (await _factory.CreateOwnedEventAsync(
            $"owner-{Guid.NewGuid():N}@example.com", "OwnerPass123!", "Foreign Event")).EventId;

    [DockerFact]
    public async Task AdminSeesAll_ForeignEventInList()
    {
        // Arrange — событие чужого пользователя, админ не участник
        var eventId = await CreateForeignEventAsync();
        var adminClient = await CreateAdminClientAsync();

        // Act
        var list = await adminClient.GetFromJsonAsync<JsonElement>("/api/events?page=1&pageSize=100");

        // Assert — чужое событие попало в список админа
        var found = list.GetProperty("items").EnumerateArray()
            .Any(e => e.GetProperty("id").GetGuid() == eventId);
        Assert.True(found);
    }

    [DockerFact]
    public async Task AdminSeesAll_ForeignEventDetailAndCompletions()
    {
        // Arrange
        var eventId = await CreateForeignEventAsync();
        var adminClient = await CreateAdminClientAsync();

        // Act + Assert — детали и история завершений чужого события доступны
        var detail = await adminClient.GetAsync($"/api/events/{eventId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var completions = await adminClient.GetAsync($"/api/events/{eventId}/completions");
        Assert.Equal(HttpStatusCode.OK, completions.StatusCode);
    }

    [DockerFact]
    public async Task RegularUser_StillExcludedFromForeignEvent()
    {
        // Arrange — флаг не расширяет доступ обычных пользователей
        var eventId = await CreateForeignEventAsync();
        var email = $"regular-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Regular User");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act + Assert
        var detail = await client.GetAsync($"/api/events/{eventId}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/events?page=1&pageSize=100");
        var found = list.GetProperty("items").EnumerateArray()
            .Any(e => e.GetProperty("id").GetGuid() == eventId);
        Assert.False(found);
    }

    [DockerFact]
    public async Task ConfigEndpoint_ReportsFlagOn()
    {
        // /api/config анонимен — режим должны видеть все
        var client = _factory.CreateClient();
        var cfg = await client.GetFromJsonAsync<JsonElement>("/api/config");
        Assert.True(cfg.GetProperty("seeAllEvents").GetBoolean());
    }
}

/// <summary>
/// Дебаг-режим ВЫКЛ (дефолт): админ по-прежнему НЕ видит чужие события.
/// </summary>
public class DebugModeOffIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DebugModeOffIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(adminEmail, "AdminPass123!", "Admin User");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == adminEmail);
        user.IsAdmin = true;
        await db.SaveChangesAsync();

        return await _factory.CreateAuthenticatedClientAsync(adminEmail, "AdminPass123!");
    }

    [DockerFact]
    public async Task Admin_WithoutFlag_CannotSeeForeignEvent()
    {
        // Arrange
        var (_, eventId) = await _factory.CreateOwnedEventAsync(
            $"owner-{Guid.NewGuid():N}@example.com", "OwnerPass123!", "Foreign Event");
        var adminClient = await CreateAdminClientAsync();

        // Act + Assert — без флага поведение прежнее: 404, событие скрыто
        var detail = await adminClient.GetAsync($"/api/events/{eventId}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);

        var list = await adminClient.GetFromJsonAsync<JsonElement>("/api/events?page=1&pageSize=100");
        var found = list.GetProperty("items").EnumerateArray()
            .Any(e => e.GetProperty("id").GetGuid() == eventId);
        Assert.False(found);
    }

    [DockerFact]
    public async Task ConfigEndpoint_ReportsFlagOff()
    {
        var client = _factory.CreateClient();
        var cfg = await client.GetFromJsonAsync<JsonElement>("/api/config");
        Assert.False(cfg.GetProperty("seeAllEvents").GetBoolean());
    }
}
