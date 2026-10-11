using Felcra.Profiling.Application.Auth;
using Felcra.Profiling.Application.Census;
using Felcra.Profiling.Application.Users;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Infrastructure.Census;
using Felcra.Profiling.Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Felcra.Profiling.Infrastructure.Identity;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Felcra.Profiling.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseNpgsql(
                // Resolved lazily so hosts (e.g. integration tests) can override configuration late.
                sp.GetRequiredService<IConfiguration>().GetConnectionString("Default") is { Length: > 0 } connectionString
                    ? connectionString
                    : throw new InvalidOperationException("Connection string 'Default' is not configured."),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddOptions<JwtOptions>()
            .Bind(config.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<ICensusImportService, CensusImportService>();

        services.AddOptions<FileStoreOptions>().Bind(config.GetSection(FileStoreOptions.SectionName));
        services.AddOptions<Application.Breeding.BreedingOptions>().Bind(config.GetSection(Application.Breeding.BreedingOptions.SectionName));
        services.AddSingleton<IFileStore>(sp =>
        {
            var path = sp.GetRequiredService<IOptions<FileStoreOptions>>().Value.Path;
            var root = System.IO.Path.IsPathRooted(path)
                ? path
                : System.IO.Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, path);
            return new LocalFileStore(root, sp.GetRequiredService<TimeProvider>());
        });
        services.AddScoped<DbSeeder>();

        return services;
    }
}
