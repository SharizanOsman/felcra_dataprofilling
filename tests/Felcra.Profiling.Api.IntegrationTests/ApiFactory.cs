using System.Net.Http.Headers;
using System.Net.Http.Json;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Felcra.Profiling.Api.IntegrationTests;

/// <summary>Runs the API against a throwaway PostgreSQL container (requires Docker).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string ManagerEmail = "manager@felcra.local";
    public const string OfficerEmail = "officer@felcra.local";
    public const string Password = "Felcra@2026";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Static web assets (the Blazor clients) are only wired up automatically in Development.
        builder.UseStaticWebAssets();
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("RateLimiting:LoginPermitsPerMinute", "1000");
        builder.UseSetting("Storage:Path", Path.Combine(Path.GetTempPath(), $"felcra-tests-{Guid.NewGuid():N}"));
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:SeedOnStartup", "true");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", Password);
        builder.UseSetting("Seed:DevelopmentData", "true");
    }

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        _ = Server; // Start the host so migrations and seeding run.
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    public async Task<TokenResponse> LoginAsync(string email, string password = Password)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var tokens = await LoginAsync(email);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    /// <summary>A DbContext acting as the given user, bypassing HTTP.</summary>
    public AppDbContext CreateDbContext(IServiceScope scope, ICurrentUser? user = null) =>
        new(scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(), user);
}

public sealed class TestUser(Guid? userId, bool isHqAdmin, params Guid[] farmIds) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public Guid? UserId => userId;
    public string? UserName => "test";
    public bool IsHqAdmin => isHqAdmin;
    public IReadOnlyCollection<Guid> FarmIds => farmIds;
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
