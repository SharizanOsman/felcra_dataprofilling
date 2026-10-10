using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Felcra.Profiling.Application.Auth;
using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Felcra.Profiling.Infrastructure.Identity;

internal sealed class AuthService(
    UserManager<ApplicationUser> users,
    AppDbContext db,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider clock) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<TokenResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive) return null;
        if (await users.IsLockedOutAsync(user)) return null;

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            return null;
        }

        await users.ResetAccessFailedCountAsync(user);
        return await IssueTokensAsync(user, previous: null, ct);
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return null;

        if (stored.RevokedAt is not null)
        {
            // A rotated token was presented again: assume it was stolen and revoke the whole family.
            await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            return null;
        }
        if (!stored.IsActive(now)) return null;

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive) return null;

        return await IssueTokensAsync(user, stored, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);
    }

    public async Task<CurrentUserDto?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.Users.Include(u => u.Farms).SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        var roles = await users.GetRolesAsync(user);
        return new CurrentUserDto(user.Id, user.Email!, user.FullName, [.. roles], [.. user.Farms.Select(f => f.FarmId)]);
    }

    private async Task<TokenResponse> IssueTokensAsync(ApplicationUser user, RefreshToken? previous, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var roles = await users.GetRolesAsync(user);
        var farmIds = await db.UserFarms.Where(f => f.UserId == user.Id).Select(f => f.FarmId).ToListAsync(ct);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AppClaimTypes.FullName, user.FullName),
        };
        claims.AddRange(roles.Select(r => new Claim("role", r)));
        claims.AddRange(farmIds.Select(f => new Claim(AppClaimTypes.Farm, f.ToString())));

        var accessExpires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var accessToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = accessExpires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)), SecurityAlgorithms.HmacSha256),
        });

        var rawRefresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(rawRefresh),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays),
        };
        db.RefreshTokens.Add(refresh);

        if (previous is not null)
        {
            previous.RevokedAt = now;
            previous.ReplacedByTokenHash = refresh.TokenHash;
        }

        await db.SaveChangesAsync(ct);
        return new TokenResponse(accessToken, accessExpires, rawRefresh, refresh.ExpiresAt);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
