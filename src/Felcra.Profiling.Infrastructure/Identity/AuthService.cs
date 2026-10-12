using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Felcra.Profiling.Application.Auth;
using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Domain.Auditing;
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

    public async Task<TokenResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default, string? language = null)
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
        RememberLanguage(user, language);
        return await IssueTokensAsync(user, previous: null, ct);
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default, string? language = null)
    {
        var now = clock.GetUtcNow();
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return null;

        if (stored.RevokedAt is not null)
        {
            // A rotated token was presented again: assume it was stolen and revoke the whole family.
            // Tokens revoked deliberately (sign-out, password change, admin action) are simply refused, so a
            // phone still holding one cannot sign the user out of the session that replaced it.
            if (stored.ReplacedByTokenHash is not null)
            {
                await db.RefreshTokens
                    .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            }
            return null;
        }
        if (!stored.IsActive(now)) return null;

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive) return null;

        RememberLanguage(user, language);
        return await IssueTokensAsync(user, stored, ct);
    }

    // Saved with the new tokens. The apps refresh every few minutes, so a language switch is picked up quickly.
    private static void RememberLanguage(ApplicationUser user, string? language)
    {
        if (language is not null && user.Language != language) user.Language = language;
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

    public async Task<TokenResponse?> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user)) return null;
        if (!await users.CheckPasswordAsync(user, request.CurrentPassword))
        {
            await users.AccessFailedAsync(user);
            return null;
        }

        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Set directly (as the admin reset does) rather than UserManager.UpdateAsync, which re-marks the whole graph.
        user.PasswordHash = users.PasswordHasher.HashPassword(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.AccessFailedCount = 0;
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = now, UserId = user.Id, UserName = user.Email, Action = AuditAction.Updated, EntityType = "User", EntityId = user.Id,
            Changes = """{"Password":{"new":"(changed by the user)"}}""",
        });
        await db.SaveChangesAsync(ct);
        // Anyone else signed in as this user (another phone, a stolen session) is signed out.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        var tokens = await IssueTokensAsync(user, previous: null, ct);
        await tx.CommitAsync(ct);
        return tokens;
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
