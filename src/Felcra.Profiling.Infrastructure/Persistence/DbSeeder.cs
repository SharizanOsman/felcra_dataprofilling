using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Domain.Farms;
using Felcra.Profiling.Domain.MasterData;
using Felcra.Profiling.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Felcra.Profiling.Infrastructure.Persistence;

/// <summary>
/// Seeds roles and the first HQ admin. With <c>Seed:DevelopmentData=true</c> it also adds the
/// Pagoh farm and a sample manager / field officer so every role can be tried out locally.
/// </summary>
public sealed class DbSeeder(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IConfiguration config,
    TimeProvider clock,
    ILogger<DbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.CreateVersion7() });
        }

        if (!await users.Users.AnyAsync(ct))
        {
            var email = config["Seed:AdminEmail"];
            var password = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("No users exist and Seed:AdminEmail / Seed:AdminPassword are not set; skipping admin seed.");
            }
            else
            {
                await CreateUserAsync(email, config["Seed:AdminFullName"] ?? "HQ Administrator", password, Roles.HqAdmin, farm: null);
                logger.LogInformation("Seeded HQ admin {Email}", email);
            }
        }

        if (config.GetValue<bool>("Seed:DevelopmentData"))
            await SeedDevelopmentDataAsync(ct);
    }

    private async Task SeedDevelopmentDataAsync(CancellationToken ct)
    {
        await SeedReferenceDataAsync(ct);
        if (await db.Farms.AnyAsync(f => f.Code == "PAGOH", ct)) return;

        var projectIds = await db.Projects.Where(p => p.Code == "EPP5" || p.Code == "RP4").Select(p => p.Id).ToListAsync(ct);
        var farm = new Farm
        {
            Code = "PAGOH",
            Name = "FELCRA Berhad Kawasan Pagoh",
            Address = "Kandang Tetap Integrasi FELCRA Berhad Kawasan Pagoh, Segamat",
            State = "Johor",
            AreaHectares = 319,
            Capacity = 200,
            // Approximate (Pagoh, Johor); FELCRA to confirm the exact location.
            Latitude = 2.148m,
            Longitude = 102.771m,
        };
        farm.Projects = projectIds.Select(p => new FarmProject { FarmId = farm.Id, ProjectId = p }).ToList();
        db.Farms.Add(farm);
        await db.SaveChangesAsync(ct);

        const string devPassword = "Felcra@2026";
        await CreateUserAsync("manager@felcra.local", "Pagoh Farm Manager", devPassword, Roles.FarmManager, farm);
        await CreateUserAsync("officer@felcra.local", "Pagoh Field Officer", devPassword, Roles.FieldOfficer, farm);
        logger.LogInformation("Seeded development farm PAGOH with sample manager and field officer");
    }

    /// <summary>Projects, breeds and programme treatments from the Pagoh census report (July 2026).</summary>
    private async Task SeedReferenceDataAsync(CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(ct))
        {
            db.Projects.AddRange(
                new Project { Code = "EPP5", Name = "Entry Point Project 5", PlanReference = "RMK-11" },
                new Project { Code = "RP4", Name = "Rolling Plan 4", PlanReference = "RMK-11" });
        }

        if (!await db.Breeds.AnyAsync(ct))
        {
            db.Breeds.AddRange(
                new Breed { Code = "BX", Name = "Brahman Cross" },
                new Breed { Code = "ACX", Name = "Australian Cattle Cross" });
        }

        if (!await db.TreatmentProducts.AnyAsync(ct))
        {
            db.TreatmentProducts.AddRange(
                new TreatmentProduct { Name = "Brucella blood screening", Category = TreatmentCategory.DiagnosticTest },
                new TreatmentProduct { Name = "FMD vaccine (booster)", Category = TreatmentCategory.Vaccine, Unit = "ml" },
                new TreatmentProduct { Name = "Doramectin (Dectomax)", Category = TreatmentCategory.Antiparasitic, Unit = "ml" },
                new TreatmentProduct { Name = "Multivitamin", Category = TreatmentCategory.Vitamin, Unit = "ml" },
                new TreatmentProduct { Name = "Ear tagging", Category = TreatmentCategory.Procedure });
        }

        // Indicative values for common Malaysian feeds (DM basis), so the ration planner can be tried.
        // Not authoritative: FELCRA should replace them with analysed values and current prices.
        if (!await db.FeedIngredients.AnyAsync(ct))
        {
            db.FeedIngredients.AddRange(
                Feed("Napier grass (fresh)", FeedCategory.Forage, 20, 9, 55, 0.45, 0.30, 0.10),
                Feed("Brachiaria grass (fresh)", FeedCategory.Forage, 25, 7, 52, 0.35, 0.20, 0.08),
                Feed("Palm kernel cake (PKC)", FeedCategory.ByProduct, 90, 16, 72, 0.25, 0.55, 0.70),
                Feed("Rice bran", FeedCategory.ByProduct, 90, 13, 70, 0.10, 1.50, 1.20),
                Feed("Soybean meal", FeedCategory.Concentrate, 89, 48, 82, 0.30, 0.70, 2.80),
                Feed("Molasses", FeedCategory.Supplement, 75, 4, 75, 0.80, 0.08, 1.00),
                Feed("Limestone", FeedCategory.Mineral, 99, 0, 0, 38, 0, 0.50),
                Feed("Dicalcium phosphate", FeedCategory.Mineral, 97, 0, 0, 22, 18, 4.50));
        }

        await db.SaveChangesAsync(ct);

        // Link the farm to its projects if it was seeded before projects existed.
        var pagoh = await db.Farms.Include(f => f.Projects).SingleOrDefaultAsync(f => f.Code == "PAGOH", ct);
        if (pagoh is { Projects.Count: 0 })
        {
            var projectIds = await db.Projects.Where(p => p.Code == "EPP5" || p.Code == "RP4").Select(p => p.Id).ToListAsync(ct);
            db.FarmProjects.AddRange(projectIds.Select(p => new FarmProject { FarmId = pagoh.Id, ProjectId = p }));
            await db.SaveChangesAsync(ct);
        }
    }

    private static FeedIngredient Feed(string name, FeedCategory category, double dm, double cp, double tdn, double ca, double p, double price) =>
        new()
        {
            Name = name, Category = category, DryMatterPercent = (decimal)dm, CrudeProteinPercent = (decimal)cp, TdnPercent = (decimal)tdn,
            CalciumPercent = (decimal)ca, PhosphorusPercent = (decimal)p, PricePerKg = (decimal)price,
        };

    private async Task CreateUserAsync(string email, string fullName, string password, string role, Farm? farm)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            CreatedAt = clock.GetUtcNow(),
        };
        if (farm is not null) user.Farms.Add(new UserFarm { FarmId = farm.Id });

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Seeding user {email} failed: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        await users.AddToRoleAsync(user, role);
    }
}
