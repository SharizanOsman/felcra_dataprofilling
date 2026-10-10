using System.Text.Json;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Application.Users;
using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Contracts.Users;
using Felcra.Profiling.Domain.Auditing;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Infrastructure.Identity;

internal sealed class UserAdminService(
    UserManager<ApplicationUser> users,
    AppDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock) : IUserAdminService
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var list = await users.Users.Include(u => u.Farms).OrderBy(u => u.FullName).ToListAsync(ct);
        var roles = await LoadRolesAsync(list.Select(u => u.Id).ToList(), ct);
        return list.Select(u => ToDto(u, roles.GetValueOrDefault(u.Id))).ToList();
    }

    public async Task<UserDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);
        return user is null ? null : ToDto(user, (await LoadRolesAsync([id], ct)).GetValueOrDefault(id));
    }

    public async Task<UserCommandResult> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var farmIds = request.Role == Roles.HqAdmin ? [] : request.FarmIds.Distinct().ToList();
        if (await FindUnknownFarmAsync(farmIds, ct) is { } error) return error;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            FullName = request.FullName,
            CreatedAt = clock.GetUtcNow(),
            Farms = farmIds.Select(f => new UserFarm { FarmId = f }).ToList(),
        };

        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded) return FromIdentityErrors(created);

        await SetRoleAsync(user.Id, request.Role, ct);
        await WriteAuditAsync(AuditAction.Created, user.Id, new()
        {
            ["Email"] = new { @new = user.Email },
            ["FullName"] = new { @new = user.FullName },
            ["Role"] = new { @new = request.Role },
            ["FarmIds"] = new { @new = farmIds },
        }, ct);
        await tx.CommitAsync(ct);

        return UserCommandResult.Ok(ToDto(user, request.Role));
    }

    public async Task<UserCommandResult> UpdateAsync(Guid id, UpdateUserRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);
        if (user is null) return UserCommandResult.Missing();

        if (id == actingUserId && (!request.IsActive || request.Role != Roles.HqAdmin))
            return UserCommandResult.Invalid(nameof(request.Role), "You cannot deactivate your own account or remove your own HQ admin role.");

        var farmIds = request.Role == Roles.HqAdmin ? [] : request.FarmIds.Distinct().ToList();
        if (await FindUnknownFarmAsync(farmIds, ct) is { } error) return error;

        var oldRole = (await LoadRolesAsync([id], ct)).GetValueOrDefault(id);
        var oldFarms = user.Farms.Select(f => f.FarmId).Order().ToList();
        var changes = new Dictionary<string, object?>();
        if (user.FullName != request.FullName) changes["FullName"] = new { old = user.FullName, @new = request.FullName };
        if (user.IsActive != request.IsActive) changes["IsActive"] = new { old = user.IsActive, @new = request.IsActive };
        if (oldRole != request.Role) changes["Role"] = new { old = oldRole, @new = request.Role };
        if (!oldFarms.SequenceEqual(farmIds.Order())) changes["FarmIds"] = new { old = oldFarms, @new = farmIds };
        if (changes.Count == 0) return UserCommandResult.Ok(ToDto(user, oldRole));

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Tracked entity: change it through the DbContext rather than UserManager.UpdateAsync, which
        // re-marks the whole graph (including farm assignments) as modified.
        user.FullName = request.FullName;
        user.IsActive = request.IsActive;
        db.UserFarms.RemoveRange(user.Farms.Where(f => !farmIds.Contains(f.FarmId)).ToList());
        db.UserFarms.AddRange(farmIds.Where(f => !oldFarms.Contains(f)).Select(f => new UserFarm { UserId = id, FarmId = f }));
        await db.SaveChangesAsync(ct);

        if (oldRole != request.Role) await SetRoleAsync(id, request.Role, ct);

        // Role, farm and status changes are carried in tokens: end existing sessions so they take effect.
        // Access tokens already issued remain valid until they expire (at most Jwt:AccessTokenMinutes).
        if (changes.Keys.Any(k => k is "IsActive" or "Role" or "FarmIds"))
            await RevokeSessionsAsync(id, ct);

        await WriteAuditAsync(AuditAction.Updated, id, changes, ct);
        await tx.CommitAsync(ct);

        return UserCommandResult.Ok(ToDto(user, request.Role));
    }

    public async Task<UserCommandResult> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);
        if (user is null) return UserCommandResult.Missing();

        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, request.NewPassword);
            if (!result.Succeeded) return FromIdentityErrors(result, nameof(request.NewPassword));
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        user.PasswordHash = users.PasswordHasher.HashPassword(user, request.NewPassword);
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.SecurityStamp = Guid.NewGuid().ToString();
        await db.SaveChangesAsync(ct);
        await RevokeSessionsAsync(id, ct);
        await WriteAuditAsync(AuditAction.Updated, id, new() { ["Password"] = new { @new = "(reset)" } }, ct);
        await tx.CommitAsync(ct);

        return UserCommandResult.Ok(ToDto(user, (await LoadRolesAsync([id], ct)).GetValueOrDefault(id)));
    }

    private Task<ApplicationUser?> FindAsync(Guid id, CancellationToken ct) =>
        users.Users.Include(u => u.Farms).SingleOrDefaultAsync(u => u.Id == id, ct);

    private async Task<Dictionary<Guid, string>> LoadRolesAsync(List<Guid> userIds, CancellationToken ct) =>
        await (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               where userIds.Contains(ur.UserId)
               select new { ur.UserId, r.Name })
            .ToDictionaryAsync(x => x.UserId, x => x.Name!, ct);

    private async Task<UserCommandResult?> FindUnknownFarmAsync(List<Guid> farmIds, CancellationToken ct)
    {
        var known = await db.Farms.CountAsync(f => farmIds.Contains(f.Id), ct);
        return known == farmIds.Count ? null : UserCommandResult.Invalid("FarmIds", "One or more farms do not exist.");
    }

    /// <summary>Each user has exactly one role. Written directly (not via UserManager) for the reason above.</summary>
    private async Task SetRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        var roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).SingleAsync(ct);
        await db.UserRoles.Where(ur => ur.UserId == userId).ExecuteDeleteAsync(ct);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync(ct);
    }

    private Task RevokeSessionsAsync(Guid userId, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);

    private async Task WriteAuditAsync(AuditAction action, Guid userId, Dictionary<string, object?> changes, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = clock.GetUtcNow(),
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = action,
            EntityType = "User",
            EntityId = userId,
            Changes = JsonSerializer.Serialize(changes),
        });
        await db.SaveChangesAsync(ct);
    }

    private UserDto ToDto(ApplicationUser user, string? role) => new(
        user.Id,
        user.Email!,
        user.FullName,
        role ?? "",
        user.Farms.Select(f => f.FarmId).ToList(),
        user.IsActive,
        user.LockoutEnd > clock.GetUtcNow(),
        user.CreatedAt);

    private static UserCommandResult FromIdentityErrors(IdentityResult result, string? key = null) =>
        new(null, false, result.Errors
            .GroupBy(e => key ?? (e.Code.Contains("Email") || e.Code.Contains("UserName") ? "Email" : e.Code.Contains("Password") ? "Password" : ""))
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).Distinct().ToArray()));
}
