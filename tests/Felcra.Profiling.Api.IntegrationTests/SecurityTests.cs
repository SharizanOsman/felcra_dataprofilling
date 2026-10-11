using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.Records;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Felcra.Profiling.Api.IntegrationTests;

/// <summary>
/// Basic penetration checks that run with every build: authentication on every route, token tampering,
/// role and farm boundaries, account enumeration and response headers.
/// </summary>
[Collection(ApiCollection.Name)]
public class SecurityTests(ApiFactory factory)
{
    [Fact]
    public async Task Every_api_route_except_sign_in_requires_a_token()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api") == true).ToList();
        Assert.True(endpoints.Count > 60);

        var anonymous = endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText).Order().ToList();
        Assert.Equal(["/api/auth/login", "/api/auth/logout", "/api/auth/refresh"], anonymous);

        var client = factory.CreateClient();
        var failures = new List<string>();
        foreach (var endpoint in endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var url = Regex.Replace(endpoint.RoutePattern.RawText!, @"\{[^}]+\}", Guid.NewGuid().ToString());
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) };
                var response = await client.SendAsync(request);
                if (response.StatusCode != HttpStatusCode.Unauthorized) failures.Add($"{method} {url} → {(int)response.StatusCode}");
            }
        }
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Forged_or_unsigned_tokens_are_rejected()
    {
        var token = (await factory.LoginAsync(ApiFactory.OfficerEmail)).AccessToken;
        var parts = token.Split('.');
        var payload = JsonNode.Parse(Base64UrlEncoder.Decode(parts[1]))!.AsObject();
        payload["role"] = Roles.HqAdmin;
        var forgedPayload = Base64UrlEncoder.Encode(payload.ToJsonString());

        string[] tokens =
        [
            // Role changed, original signature kept.
            $"{parts[0]}.{forgedPayload}.{parts[2]}",
            // "alg": "none" with no signature.
            $"{Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""")}.{forgedPayload}.",
            // Signed with another key.
            Sign(payload, "an-attacker-chosen-signing-key-0123456789"),
        ];
        foreach (var forged in tokens)
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
        }

        // The genuine token still works, within the officer's rights.
        var genuine = factory.CreateClient();
        genuine.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await genuine.GetAsync("/api/users")).StatusCode);
    }

    private static string Sign(JsonObject payload, string key)
    {
        var header = Base64UrlEncoder.Encode("""{"alg":"HS256","typ":"JWT"}""");
        var body = Base64UrlEncoder.Encode(payload.ToJsonString());
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(key));
        return $"{header}.{body}.{Base64UrlEncoder.Encode(hmac.ComputeHash(Encoding.ASCII.GetBytes($"{header}.{body}")))}";
    }

    [Fact]
    public async Task Roles_limit_what_each_user_can_do()
    {
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);

        Assert.Equal(HttpStatusCode.Forbidden, (await officer.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.GetAsync("/api/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.PostAsJsonAsync("/api/farms", new FarmRequest { Code = "X", Name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.GetAsync("/api/reports/culling-candidates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync($"/api/animals/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Users_cannot_reach_animals_on_farms_they_are_not_assigned_to()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var farm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("s"), Name = "Other farm" })).ReadAsync<FarmDto>();
        var animal = await (await hq.PostAsJsonAsync("/api/animals", new AnimalRequest
        {
            FarmId = farm.Id, TagNumber = TestHelpers.UniqueCode("t"), Category = AnimalCategory.Dam, Sex = Sex.Female,
        })).ReadAsync<AnimalDto>();

        foreach (var email in new[] { ApiFactory.ManagerEmail, ApiFactory.OfficerEmail })
        {
            var user = await factory.CreateAuthenticatedClientAsync(email);
            Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/animals/{animal.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/animals/{animal.Id}/records")).StatusCode);
            var write = await user.PostAsJsonAsync($"/api/animals/{animal.Id}/measurements", new MeasurementRequest { Type = MeasurementType.Weight, Value = 300 });
            Assert.Contains(write.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });
            // Managers: the farm is not visible (404); officers have no reports at all (403).
            Assert.Contains((await user.GetAsync($"/api/reports/treatments?farmId={farm.Id}")).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden });
            var list = await user.GetFromJsonAsync<Contracts.Common.PagedResult<AnimalListItemDto>>($"/api/animals?search={animal.TagNumber}");
            Assert.Empty(list!.Items);
        }
    }

    [Fact]
    public async Task Users_change_their_own_password_and_other_sessions_end()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var email = $"{TestHelpers.UniqueCode("pw").ToLowerInvariant()}@felcra.local";
        var farms = await hq.GetFromJsonAsync<List<FarmDto>>("/api/farms");
        (await hq.PostAsJsonAsync("/api/users", new Contracts.Users.CreateUserRequest
        {
            Email = email, FullName = "Password Tester", Role = Roles.FieldOfficer, FarmIds = [farms![0].Id], Password = ApiFactory.Password,
        })).EnsureSuccessStatusCode();
        var otherPhone = await factory.LoginAsync(email);
        var me = await factory.CreateAuthenticatedClientAsync(email);

        var wrong = await me.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest { CurrentPassword = "Wrong@2026", NewPassword = "Better@2027" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("current password is not correct", await wrong.Content.ReadAsStringAsync());
        var weak = await me.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest { CurrentPassword = ApiFactory.Password, NewPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var tokens = await (await me.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = ApiFactory.Password, NewPassword = "Better@2027" })).ReadAsync<TokenResponse>();

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(otherPhone.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(tokens.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = ApiFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "Better@2027" })).StatusCode);
    }

    [Fact]
    public async Task Sign_in_does_not_reveal_which_accounts_exist()
    {
        var client = factory.CreateClient();
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = "nobody@felcra.local", Password = "Wrong@2026x" });
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = ApiFactory.ManagerEmail, Password = "Wrong@2026x" });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, wrongPassword.StatusCode);
        // Same answer apart from the per-request trace id.
        static async Task<string> Body(HttpResponseMessage r) => Regex.Replace(await r.Content.ReadAsStringAsync(), "\"traceId\":\"[^\"]*\"", "");
        Assert.Equal(await Body(unknown), await Body(wrongPassword));
    }

    [Theory]
    [InlineData("/admin/")]
    [InlineData("/field/")]
    [InlineData("/api/auth/me")]
    public async Task Responses_carry_security_headers(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        var csp = string.Join("", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-eval'", csp.Replace("wasm-unsafe-eval'", ""));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("camera=(self)", response.Headers.GetValues("Permissions-Policy").Single());
        if (url.StartsWith("/api")) Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Malformed_requests_get_a_problem_without_internal_details()
    {
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var response = await officer.PostAsync("/api/animals", new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain(" at ", body);
    }
}
