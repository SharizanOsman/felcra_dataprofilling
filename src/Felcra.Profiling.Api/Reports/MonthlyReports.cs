using System.Net;
using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Contracts.Reports;
using Felcra.Profiling.Domain.Reports;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Felcra.Profiling.Api.Reports;

public sealed class MonthlyReportOptions
{
    public const string Section = "Reports:Monthly";

    public bool Enabled { get; set; }

    /// <summary>Day of the month (Malaysia time) on which last month's reports are sent.</summary>
    public int DayOfMonth { get; set; } = 1;

    /// <summary>Hour of that day (Malaysia time, 0–23) from which they are sent.</summary>
    public int Hour { get; set; } = 7;

    /// <summary>Portal address used for links in the e-mail, e.g. https://profiling.felcra.com.my.</summary>
    public string? PortalUrl { get; set; }
}

/// <summary>
/// Last month's farm performance, health and treatment, and culling candidate reports, e-mailed as PDFs:
/// HQ admins get all farms together, each farm manager gets one e-mail per assigned farm.
/// </summary>
internal sealed class MonthlyReportJob(
    AppDbContext db, ReportService reports, IEmailSender email, IOptions<MonthlyReportOptions> options, TimeProvider clock, ILogger<MonthlyReportJob> logger)
{
    internal sealed record Recipient(Guid UserId, string Email, string Name, bool IsHq, IReadOnlyList<Guid> FarmIds);

    public async Task<List<Recipient>> RecipientsAsync(CancellationToken ct)
    {
        var users = await (
                from u in db.Users.AsNoTracking()
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                where u.IsActive && u.Email != null && (r.Name == Roles.HqAdmin || r.Name == Roles.FarmManager)
                select new { u.Id, u.Email, u.FullName, IsHq = r.Name == Roles.HqAdmin })
            .ToListAsync(ct);
        var activeFarms = await db.Farms.IgnoreQueryFilters().Where(f => f.IsActive && !f.IsDeleted).Select(f => f.Id).ToListAsync(ct);
        var farms = (await db.UserFarms.AsNoTracking().ToListAsync(ct))
            .Where(uf => activeFarms.Contains(uf.FarmId))
            .GroupBy(uf => uf.UserId).ToDictionary(g => g.Key, g => g.Select(uf => uf.FarmId).ToList());

        return users
            .Select(u => new Recipient(u.Id, u.Email!, u.FullName, u.IsHq, u.IsHq ? [] : farms.GetValueOrDefault(u.Id) ?? []))
            .Where(r => r.IsHq || r.FarmIds.Count > 0)
            .OrderBy(r => r.Email)
            .ToList();
    }

    /// <summary>Sends a month's reports once (scheduled). Returns false when that month was already sent.</summary>
    public async Task<bool> RunScheduledAsync(DateOnly period, CancellationToken ct)
    {
        if (await db.ScheduledReportRuns.AnyAsync(r => r.Period == period, ct)) return false;

        // Claim the month first: the unique period stops a second server instance sending it too.
        var run = new ScheduledReportRun { Period = period, StartedAt = clock.GetUtcNow() };
        db.ScheduledReportRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return false;
        }

        try
        {
            run.EmailsSent = await SendAllAsync(period, ct);
            run.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Monthly reports for {Period:yyyy-MM} sent in {Count} e-mails", period, run.EmailsSent);
            return true;
        }
        catch
        {
            // Release the claim so the next check tries again.
            db.ScheduledReportRuns.Remove(run);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<int> SendAllAsync(DateOnly period, CancellationToken ct)
    {
        var (from, to) = (period, period.AddMonths(1).AddDays(-1));
        var packages = new Dictionary<Guid, MonthlyPackage>();
        async Task<MonthlyPackage> PackageAsync(Guid? farmId)
        {
            var key = farmId ?? Guid.Empty;
            if (!packages.TryGetValue(key, out var package)) packages[key] = package = await reports.MonthlyPackageAsync(farmId, from, to, ct);
            return package;
        }

        var sent = 0;
        foreach (var recipient in await RecipientsAsync(ct))
        {
            foreach (var farmId in recipient.IsHq ? [null] : recipient.FarmIds.Select(id => (Guid?)id))
            {
                await email.SendAsync(Compose(recipient.Email, recipient.Name, period, await PackageAsync(farmId)), ct);
                sent++;
            }
        }
        return sent;
    }

    /// <summary>Last month's all-farms (HQ) or farm reports, sent only to the given address.</summary>
    public async Task<EmailMessage> TestMessageAsync(string to, string name, Guid? farmId, CancellationToken ct)
    {
        var (from, until) = reports.LastMonth();
        var message = Compose(to, name, from, await reports.MonthlyPackageAsync(farmId, from, until, ct));
        return message with { Subject = "[Test] " + message.Subject };
    }

    private EmailMessage Compose(string to, string name, DateOnly period, MonthlyPackage package)
    {
        var d = package.Data;
        var h = d.Herd;
        var month = ReportText.Month(period);
        string[] lines =
        [
            $"Active animals: {h.ActiveAnimals}" + (h.Capacity is > 0 ? $" ({100.0 * h.ActiveAnimals / h.Capacity.Value:0}% of capacity)" : ""),
            $"Pregnancy rate: {(h.PregnancyRatePercent is { } p ? $"{p:0.#}%" : "– (no breeding records yet)")}",
            $"Recorded in {month}: {d.Activity.Weighings} weighings, {d.Activity.Treatments} treatments, {d.Activity.BreedingEvents} breeding events",
            $"Calves registered: {d.Activity.CalvesBorn} · Animals that left the herd: {d.Exits.Count}",
            $"Culling candidates: {d.CullingCandidates} · Breeding tasks overdue: {h.TasksOverdue}",
        ];
        var link = options.Value.PortalUrl is { Length: > 0 } url ? $"{url.TrimEnd('/')}/admin/reports" : null;
        var attachments = string.Join(", ", package.Files.Select(f => f.FileName));

        var text = $"Dear {name},\n\nThe cattle profiling reports for {month} — {d.Scope.FarmLabel} — are attached.\n\n" +
                   string.Join("\n", lines.Select(l => "• " + l)) +
                   $"\n\nAttached: {attachments}.\nExcel versions and other periods are in the admin portal under Reports" +
                   (link is null ? "." : $": {link}") +
                   "\n\nThis e-mail is sent automatically each month by FELCRA Cattle Profiling.";
        var html = $"<p>Dear {WebUtility.HtmlEncode(name)},</p>" +
                   $"<p>The cattle profiling reports for <b>{month}</b> — {WebUtility.HtmlEncode(d.Scope.FarmLabel)} — are attached.</p>" +
                   "<ul>" + string.Concat(lines.Select(l => $"<li>{WebUtility.HtmlEncode(l)}</li>")) + "</ul>" +
                   $"<p>Attached: {WebUtility.HtmlEncode(attachments)}.<br>Excel versions and other periods are in the admin portal under " +
                   (link is null ? "Reports." : $"<a href=\"{WebUtility.HtmlEncode(link)}\">Reports</a>.") + "</p>" +
                   "<p style=\"color:#757575;font-size:12px\">This e-mail is sent automatically each month by FELCRA Cattle Profiling.</p>";

        return new EmailMessage(to, name, $"Cattle profiling reports — {month} — {d.Scope.FarmLabel}", text, html, package.Files);
    }

    public async Task<MonthlyReportStatusDto> StatusAsync(CancellationToken ct)
    {
        var runs = await db.ScheduledReportRuns.AsNoTracking().OrderByDescending(r => r.Period).Take(12)
            .Select(r => new MonthlyReportRunDto(r.Period, r.StartedAt, r.CompletedAt, r.EmailsSent)).ToListAsync(ct);
        var o = options.Value;
        return new MonthlyReportStatusDto(o.Enabled, email.IsConfigured, o.DayOfMonth, o.Hour, (await RecipientsAsync(ct)).Count, runs);
    }
}

/// <summary>Checks every hour whether last month's reports are due and sends them once.</summary>
internal sealed class MonthlyReportService(
    IServiceScopeFactory scopes, IOptions<MonthlyReportOptions> options, TimeProvider clock, ILogger<MonthlyReportService> logger) : BackgroundService
{
    private static readonly TimeSpan Malaysia = TimeSpan.FromHours(8);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                await RunIfDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Monthly report e-mail failed; will retry within the hour");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunIfDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToOffset(Malaysia);
        var o = options.Value;
        var day = Math.Clamp(o.DayOfMonth, 1, 28);
        if (now.Day < day || (now.Day == day && now.Hour < o.Hour)) return;

        using var scope = scopes.CreateScope();
        if (!scope.ServiceProvider.GetRequiredService<IEmailSender>().IsConfigured)
        {
            logger.LogWarning("Monthly reports are enabled but e-mail is not configured (Email section)");
            return;
        }
        var period = new DateOnly(now.Year, now.Month, 1).AddMonths(-1);
        await scope.ServiceProvider.GetRequiredService<MonthlyReportJob>().RunScheduledAsync(period, ct);
    }
}
