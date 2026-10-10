using System.Net;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Auth;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Seeded_admin_can_log_in_and_read_profile()
    {
        var client = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);

        var me = await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me");

        Assert.NotNull(me);
        Assert.Equal(ApiFactory.AdminEmail, me.Email);
        Assert.Contains(Roles.HqAdmin, me.Roles);
    }

    [Fact]
    public async Task Farm_manager_profile_lists_assigned_farm()
    {
        var client = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);

        var me = await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me");

        Assert.NotNull(me);
        Assert.Contains(Roles.FarmManager, me.Roles);
        Assert.Single(me.FarmIds);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = ApiFactory.ManagerEmail, Password = "Wrong-password-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_login_payload_returns_validation_problem()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = "not-an-email", Password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_requires_authentication_by_default()
    {
        var response = await factory.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_reuse_revokes_all_sessions()
    {
        var client = factory.CreateClient();
        var original = await factory.LoginAsync(ApiFactory.ManagerEmail);

        var first = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(original.RefreshToken));
        first.EnsureSuccessStatusCode();
        var rotated = (await first.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(original.RefreshToken, rotated.RefreshToken);

        // Replaying the old token looks like theft: it fails and revokes the rotated token too.
        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(original.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var afterReplay = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(rotated.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        var client = factory.CreateClient();
        var tokens = await factory.LoginAsync(ApiFactory.ManagerEmail);

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Theory]
    [InlineData("/admin/")]
    [InlineData("/field/")]
    [InlineData("/field/manifest.webmanifest")]
    [InlineData("/health")]
    public async Task Clients_and_health_are_served_anonymously(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
