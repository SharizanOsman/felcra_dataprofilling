using System.Text;
using System.Threading.RateLimiting;
using Felcra.Profiling.Api.Endpoints;
using Felcra.Profiling.Api.Geocoding;
using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Api.Notifications;
using Felcra.Profiling.Api.Reports;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Infrastructure;
using Felcra.Profiling.Infrastructure.Identity;
using Felcra.Profiling.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;

if (Commands.TryRun(args, out var exitCode)) return exitCode;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<Felcra.Profiling.Api.Breeding.BreedingQueries>();
builder.Services.AddScoped<Felcra.Profiling.Api.Analytics.AnalyticsQueries>();
builder.Services.AddScoped<ReportQueries>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<MonthlyReportJob>();
builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.Section));
builder.Services.Configure<MonthlyReportOptions>(builder.Configuration.GetSection(MonthlyReportOptions.Section));
builder.Services.AddHostedService<MonthlyReportService>();
builder.Services.Configure<PushOptions>(builder.Configuration.GetSection(PushOptions.Section));
builder.Services.AddHttpClient("webpush");
builder.Services.AddMemoryCache();
builder.Services.Configure<GeocodingOptions>(builder.Configuration.GetSection(GeocodingOptions.Section));
builder.Services.AddHttpClient<IGeocoder, NominatimGeocoder>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<GeocodingOptions>>().Value;
    http.BaseAddress = new Uri(o.BaseUrl);
    http.DefaultRequestHeaders.UserAgent.ParseAdd(o.UserAgent);
    http.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<IPushSender, WebPushSender>();
builder.Services.AddScoped<ReminderNotificationJob>();
builder.Services.AddHostedService<ReminderNotificationService>();
Felcra.Profiling.Contracts.Common.ValidationText.Configure();
AssemblyScanner.FindValidatorsInAssemblyContaining<LoginRequestValidator>()
    .ForEach(v => builder.Services.AddSingleton(v.InterfaceType, v.ValidatorType));

// --- Authentication & authorization ---------------------------------------------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            NameClaimType = "email",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    // Every endpoint requires a signed-in user unless it explicitly opts out.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.HqAdmin, p => p.RequireRole(Roles.HqAdmin))
    .AddPolicy(Policies.Manager, p => p.RequireRole(Roles.HqAdmin, Roles.FarmManager));

var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPermitsPerMinute", 10);
var apiPermitLimit = builder.Configuration.GetValue("RateLimiting:ApiRequestsPerMinute", 600);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermitLimit, Window = TimeSpan.FromMinutes(1) }));
    // Every API call, per signed-in user (or per IP before sign-in). Generous: a phone sending its outbox
    // after a day offline makes a burst of calls. Static files (the clients) are not limited.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        !context.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetNoLimiter("static")
            : RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Sub)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = apiPermitLimit, Window = TimeSpan.FromMinutes(1) }));
});

// Behind the reverse proxy (Caddy): take the client IP and https scheme from X-Forwarded-* so rate limits,
// logs and HSTS see the real request. Only enable when the API is reachable through the proxy alone.
if (builder.Configuration.GetValue<bool>("ReverseProxy:Enabled"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        // The proxy's address on the Docker network is not fixed.
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

// Keys protect Identity tokens; keep them on the data volume so they survive container restarts.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("felcra-profiling");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

// Malformed query/route values are the caller's fault: always answer 400, never 500 (Development throws by default).
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DbExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

await InitialiseDatabaseAsync(app);

// --- Pipeline -------------------------------------------------------------------------------
if (app.Configuration.GetValue<bool>("ReverseProxy:Enabled")) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();
app.UseRequestLanguage();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}
else
{
    app.UseHsts();
}

// Both Blazor WebAssembly clients are served from this host: /admin (portal) and /field (PWA).
// Razor class library assets (e.g. MudBlazor) are shared at /_content, but each app requests them
// relative to its own base path.
app.UseRewriter(new RewriteOptions().AddRewrite("^(?:admin|field)/(_content/.*)$", "$1", skipRemainingRules: true));
app.UseBlazorFrameworkFiles("/admin");
app.UseBlazorFrameworkFiles("/field");
app.UseStaticFiles();

// Nothing (no static file, no endpoint) matched: answer 404 here, otherwise the authenticated-by-default
// fallback policy below would answer 401 and make e.g. a missing /favicon.ico look like an auth failure.
app.Use(async (context, next) =>
{
    if (context.GetEndpoint() is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapAuthEndpoints();
app.MapFarmEndpoints();
app.MapReferenceDataEndpoints();
app.MapUserEndpoints();
app.MapAuditLogEndpoints();
app.MapAnimalEndpoints();
app.MapCensusEndpoints();
app.MapRecordEndpoints();
app.MapSyncEndpoints();
app.MapBreedingEndpoints();
app.MapAnalyticsEndpoints();
app.MapReportEndpoints();
app.MapPushEndpoints();
app.MapGeocodeEndpoints();

app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html").AllowAnonymous();
app.MapFallbackToFile("/field/{*path:nonfile}", "field/index.html").AllowAnonymous();
app.MapGet("/", () => Results.Redirect("/admin/")).AllowAnonymous().ExcludeFromDescription();

app.Run();
return 0;

static async Task InitialiseDatabaseAsync(WebApplication app)
{
    var config = app.Configuration;
    var migrate = config.GetValue<bool>("Database:MigrateOnStartup");
    var seed = config.GetValue<bool>("Database:SeedOnStartup");
    if (!migrate && !seed) return;

    using var scope = app.Services.CreateScope();
    if (migrate)
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    if (seed)
        await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync();
}

public partial class Program;
