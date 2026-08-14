using Microsoft.AspNetCore.Components;

namespace RustRconServerManager.Frontend.Services;

/// <summary>
/// Thin wrapper around NavigationManager. Single-tenant deployment, so paths are
/// always absolute from the site root. Kept as a service to avoid touching every
/// caller — callers still pass relative paths like "dashboard" or "/dashboard".
/// </summary>
public class NavigationHelper
{
    private readonly NavigationManager _navigationManager;

    public NavigationHelper(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    public void NavigateTo(string path, bool forceLoad = false)
    {
        _navigationManager.NavigateTo("/" + path.TrimStart('/'), forceLoad);
    }

    public string GetBasePath() => string.Empty;

    public string GetUrl(string relativePath) => "/" + relativePath.TrimStart('/');
}
