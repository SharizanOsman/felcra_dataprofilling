namespace Felcra.Profiling.Api.Infrastructure;

/// <summary>
/// Small command-line tools run with the API binary, e.g. <c>dotnet Felcra.Profiling.Api.dll healthcheck</c>.
/// The runtime image has no curl, so Docker's health check uses this.
/// </summary>
internal static class Commands
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = args switch
        {
            ["healthcheck", ..] => HealthCheck(args.Length > 1 ? args[1] : null),
            ["vapid-keys"] => VapidKeys(),
            _ => -1,
        };
        return exitCode >= 0;
    }

    /// <summary>Prints a new Web Push key pair for the Push section.</summary>
    private static int VapidKeys()
    {
        var (publicKey, privateKey) = Notifications.PushOptions.GenerateKeys();
        Console.WriteLine($"Push__PublicKey={publicKey}");
        Console.WriteLine($"Push__PrivateKey={privateKey}");
        return 0;
    }

    /// <summary>0 when /health answers 200 (database reachable), otherwise 1.</summary>
    private static int HealthCheck(string? url)
    {
        url ??= $"http://localhost:{Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080"}/health";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = http.GetAsync(url).GetAwaiter().GetResult();
            Console.WriteLine($"{url}: {(int)response.StatusCode}");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{url}: {ex.Message}");
            return 1;
        }
    }
}
