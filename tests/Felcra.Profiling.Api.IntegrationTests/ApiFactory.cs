using System.Net.Http.Headers;
using System.Net.Http.Json;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

    /// <summary>Where the API writes e-mails (as .eml files) during tests.</summary>
    public string MailDirectory { get; } = Path.Combine(Path.GetTempPath(), $"felcra-mail-{Guid.NewGuid():N}");

    /// <summary>Captures push notifications the API sends.</summary>
    internal FakePushSender Push { get; } = new();

    /// <summary>Answers map place searches without calling OpenStreetMap.</summary>
    internal FakeGeocoder Geocoder { get; } = new();

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
        // E-mails are written to a folder the tests read; the hourly schedule is driven by the tests instead.
        builder.UseSetting("Email:From", "FELCRA Tests <tests@felcra.local>");
        builder.UseSetting("Email:PickupDirectory", MailDirectory);
        builder.UseSetting("Reports:Monthly:Enabled", "false");
        // Push messages are captured instead of sent; the daily schedule is driven by the tests.
        var (publicKey, privateKey) = Notifications.PushOptions.GenerateKeys();
        builder.UseSetting("Push:PublicKey", publicKey);
        builder.UseSetting("Push:PrivateKey", privateKey);
        builder.UseSetting("Push:Hour", "24");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<Notifications.IPushSender>(Push);
            services.AddSingleton<Geocoding.IGeocoder>(Geocoder);
        });

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

    /// <param name="language">Sent as Accept-Language, like the apps do ("ms"/"en"); the server remembers it for e-mails and notifications.</param>
    public async Task<TokenResponse> LoginAsync(string email, string password = Password, string? language = null)
    {
        var client = CreateClient();
        if (language is not null) client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string? language = null)
    {
        var tokens = await LoginAsync(email, language: language);
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
