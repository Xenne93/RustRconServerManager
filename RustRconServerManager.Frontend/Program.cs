using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RustRconServerManager.Frontend;
using RustRconServerManager.Frontend.Services;
using Blazored.LocalStorage;
using Blazored.SessionStorage;


var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddBlazoredSessionStorage();


builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();
builder.Services.AddScoped<ITokenProvider, CookieTokenProvider>();
builder.Services.AddScoped<ServerValidationHelper>();
builder.Services.AddScoped<NavigationHelper>();
builder.Services.AddScoped<PageAccessService>();
builder.Services.AddScoped<ServerStateService>();

builder.Services.AddScoped(sp =>
{
    // Custom handler ensures HttpOnly cookies are sent with every request.
    var handler = new PathPrependingHttpHandler();

    var httpClient = new HttpClient(handler)
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    };
    return httpClient;
});




await builder.Build().RunAsync();
