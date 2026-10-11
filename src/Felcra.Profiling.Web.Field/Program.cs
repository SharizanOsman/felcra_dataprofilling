using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.UI.Shared;
using Felcra.Profiling.UI.Shared.Auth;
using Felcra.Profiling.Web.Field;
using Felcra.Profiling.Web.Field.Offline;
using Felcra.Profiling.Web.Field.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The app lives under /field/ while the API is at the site root.
var apiBase = new Uri(new Uri(builder.HostEnvironment.BaseAddress), "/");
builder.Services.AddFelcraClient(apiBase, new ClientAuthOptions { AppName = "field", AllowedRoles = Roles.FieldApp }, mobile: true);
builder.Services.AddScoped<RecentAnimals>();
// Scoped = one instance for the app's lifetime in WebAssembly (there is a single scope), so offline
// data and the sync loop are shared by all pages. Singleton would clash with the scoped ApiClient.
builder.Services.AddScoped<OfflineDb>();
builder.Services.AddScoped<FieldData>();
builder.Services.AddScoped<SyncService>();

await builder.Build().RunAsync();
