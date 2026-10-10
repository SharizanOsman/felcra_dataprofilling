using Felcra.Profiling.UI.Shared.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Felcra.Profiling.UI.Shared;

public static class ServiceCollectionExtensions
{
    public const string ApiClient = "felcra-api";

    /// <summary>
    /// Registers MudBlazor, JWT authentication and HTTP clients for a Felcra client app.
    /// Inject <see cref="HttpClient"/> for authenticated API calls.
    /// </summary>
    public static IServiceCollection AddFelcraClient(
        this IServiceCollection services, Uri apiBaseAddress, ClientAuthOptions authOptions, bool mobile = false)
    {
        services.AddMudServices(config =>
        {
            if (!mobile) return;
            // On phones, keep confirmations short and away from the animal header at the top.
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
            config.SnackbarConfiguration.VisibleStateDuration = 2500;
            config.SnackbarConfiguration.MaxDisplayedSnackbars = 2;
            config.SnackbarConfiguration.PreventDuplicates = true;
        });

        services.AddSingleton(authOptions);
        services.AddSingleton(TimeProvider.System);
        // Singletons (one per browser tab in WASM): IHttpClientFactory resolves message handlers in
        // their own DI scope, and they must share token state with the UI.
        services.AddSingleton<TokenStore>();
        services.AddSingleton<JwtAuthStateProvider>();
        services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthStateProvider>());
        services.AddSingleton<AuthClient>();
        services.AddTransient<BearerTokenHandler>();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();

        services.AddHttpClient(AuthClient.AnonymousClient, c => c.BaseAddress = apiBaseAddress);
        services.AddHttpClient(ApiClient, c => c.BaseAddress = apiBaseAddress).AddHttpMessageHandler<BearerTokenHandler>();
        services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient(ApiClient));
        services.AddScoped<Api.ApiClient>();

        return services;
    }
}
