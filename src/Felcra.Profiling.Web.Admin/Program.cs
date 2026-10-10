using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.UI.Shared;
using Felcra.Profiling.UI.Shared.Auth;
using Felcra.Profiling.Web.Admin;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The app lives under /admin/ while the API is at the site root.
var apiBase = new Uri(new Uri(builder.HostEnvironment.BaseAddress), "/");
builder.Services.AddFelcraClient(apiBase, new ClientAuthOptions { AppName = "admin", AllowedRoles = Roles.AdminPortal });

await builder.Build().RunAsync();
