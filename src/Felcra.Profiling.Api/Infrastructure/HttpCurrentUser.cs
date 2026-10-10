using System.Security.Claims;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Contracts.Auth;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Felcra.Profiling.Api.Infrastructure;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    public string? UserName => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);

    public bool IsHqAdmin => Principal?.IsInRole(Roles.HqAdmin) ?? false;

    public IReadOnlyCollection<Guid> FarmIds =>
        Principal?.FindAll(AppClaimTypes.Farm)
            .Select(c => Guid.TryParse(c.Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToArray() ?? [];
}
