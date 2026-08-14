using Microsoft.JSInterop;

namespace RustRconServerManager.Frontend.Services;

/// <summary>
/// Thin wrapper around wwwroot/js/theme.js so Blazor components can read/change the
/// light/dark theme. The actual theme resolution (stored preference vs. OS preference)
/// and DOM attribute application happen in JS, since the initial theme must be applied
/// before Blazor even loads to avoid a flash of the wrong theme - this service just lets
/// components stay in sync with that afterwards.
/// </summary>
public class ThemeService
{
    private readonly IJSRuntime _js;

    public ThemeService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<string> GetThemeAsync()
    {
        return await _js.InvokeAsync<string>("rrsmTheme.get");
    }

    public async Task SetThemeAsync(string theme)
    {
        await _js.InvokeVoidAsync("rrsmTheme.set", theme);
    }
}
