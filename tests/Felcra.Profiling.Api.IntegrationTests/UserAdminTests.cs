using System.Net;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Auditing;
using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.Users;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class UserAdminTests(ApiFactory factory)
{
    private const string Password = "Officer@2026";

    private async Task<(HttpClient Hq, Guid PagohId)> SetupAsync()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var pagoh = (await hq.GetFromJsonAsync<List<FarmDto>>("/api/farms"))!.Single(f => f.Code == "PAGOH");
        return (hq, pagoh.Id);
    }

    private static string NewEmail() => $"{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task Hq_creates_field_officer_who_can_then_sign_in()
    {
        var (hq, pagohId) = await SetupAsync();
        var email = NewEmail();

        var user = await (await hq.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = email, FullName = "New Officer", Role = Roles.FieldOfficer, FarmIds = [pagohId], Password = Password,
        })).ReadAsync<UserDto>();

        Assert.Equal(Roles.FieldOfficer, user.Role);
        Assert.Equal([pagohId], user.FarmIds);
        var tokens = await factory.LoginAsync(email, Password);
        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));

        var audit = await hq.GetFromJsonAsync<PagedResult<AuditLogDto>>($"/api/audit-logs?entityType=User&entityId={user.Id}");
        Assert.Contains(audit!.Items, a => a.Action == "Created");
    }

    [Fact]
    public async Task Farm_roles_require_a_farm_and_duplicate_emails_are_rejected()
    {
        var (hq, pagohId) = await SetupAsync();

        var noFarm = await hq.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = NewEmail(), FullName = "No Farm", Role = Roles.FarmManager, Password = Password,
        });
        Assert.Equal(HttpStatusCode.BadRequest, noFarm.StatusCode);

        var duplicate = await hq.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = ApiFactory.ManagerEmail, FullName = "Dup", Role = Roles.FarmManager, FarmIds = [pagohId], Password = Password,
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Changing_role_ends_existing_sessions()
    {
        var (hq, pagohId) = await SetupAsync();
        var email = NewEmail();
        var user = await (await hq.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = email, FullName = "Promote Me", Role = Roles.FieldOfficer, FarmIds = [pagohId], Password = Password,
        })).ReadAsync<UserDto>();
        var session = await factory.LoginAsync(email, Password);

        var update = UpdateUserRequest.From(user);
        update.Role = Roles.FarmManager;
        var updated = await (await hq.PutAsJsonAsync($"/api/users/{user.Id}", update)).ReadAsync<UserDto>();
        Assert.Equal(Roles.FarmManager, updated.Role);

        var refresh = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_cannot_sign_in()
    {
        var (hq, pagohId) = await SetupAsync();
        var email = NewEmail();
        var user = await (await hq.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = email, FullName = "Leaving", Role = Roles.FieldOfficer, FarmIds = [pagohId], Password = Password,
        })).ReadAsync<UserDto>();

        var update = UpdateUserRequest.From(user);
        update.IsActive = false;
        (await hq.PutAsJsonAsync($"/api/users/{user.Id}", update)).EnsureSuccessStatusCode();

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_themselves()
    {
        var (hq, _) = await SetupAsync();
        var me = await hq.GetFromJsonAsync<CurrentUserDto>("/api/auth/me");
        var self = await hq.GetFromJsonAsync<UserDto>($"/api/users/{me!.Id}");

        var update = UpdateUserRequest.From(self!);
        update.IsActive = false;
        var response = await hq.PutAsJsonAsync($"/api/users/{me.Id}", update);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reset_password_replaces_the_old_one()
    {
        var (hq, pagohId) = await SetupAsync();
        var email = NewEmail();
        var user = await (await hq.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = email, FullName = "Forgetful", Role = Roles.FieldOfficer, FarmIds = [pagohId], Password = Password,
        })).ReadAsync<UserDto>();

        (await hq.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new ResetPasswordRequest { NewPassword = "Brand@New2026" }))
            .EnsureSuccessStatusCode();

        var oldLogin = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.False(string.IsNullOrEmpty((await factory.LoginAsync(email, "Brand@New2026")).AccessToken));
    }

    [Fact]
    public async Task Only_hq_can_manage_users_or_read_the_audit_log()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/audit-logs")).StatusCode);
    }
}
