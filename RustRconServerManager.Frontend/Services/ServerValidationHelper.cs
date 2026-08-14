using System.Net.Http.Json;
using Blazored.LocalStorage;
using Blazored.SessionStorage;
using Microsoft.AspNetCore.Components;
using RustRconServerManager.Shared.ManageServers;

namespace RustRconServerManager.Frontend.Services
{
    /// <summary>
    /// Utility class for validating server access across all pages that require a selected server.
    /// This ensures consistent server validation logic throughout the application.
    /// </summary>
    public class ServerValidationHelper
    {
        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorage;
        private readonly ISessionStorageService _sessionStorage;
        private readonly NavigationManager _navigationManager;
        private readonly NavigationHelper _navHelper;

        public ServerValidationHelper(
            HttpClient httpClient,
            ILocalStorageService localStorage,
            ISessionStorageService sessionStorage,
            NavigationManager navigationManager,
            NavigationHelper navHelper)
        {
            _httpClient = httpClient;
            _localStorage = localStorage;
            _sessionStorage = sessionStorage;
            _navigationManager = navigationManager;
            _navHelper = navHelper;
        }

        /// <summary>
        /// Validates that a user has selected a server and has access to it.
        /// If the selected server doesn't exist or is inaccessible, auto-selects the first available server.
        /// Only redirects to /manage-servers if there are truly no servers available.
        /// </summary>
        /// <returns>True if server is valid and user has access; False if redirected</returns>
        public async Task<bool> ValidateActiveServerAsync()
        {
            try
            {
                int selectedServer = await _localStorage.GetItemAsync<int>("activeServerId");

                if (selectedServer > 0)
                {
                    // Verify with backend that user has access to this server
                    HttpResponseMessage response = await _httpClient.GetAsync($"/api/Dashboard/CheckSelectedServer/{selectedServer}");
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }

                // Selected server is 0, doesn't exist, or user lost access — try to auto-select
                return await TryAutoSelectServerAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error validating server access: {ex.Message}");
                await RedirectToManageServers("SelectServer");
                return false;
            }
        }

        /// <summary>
        /// Tries to auto-select the first available server the user has access to.
        /// </summary>
        private async Task<bool> TryAutoSelectServerAsync()
        {
            try
            {
                var servers = await _httpClient.GetFromJsonAsync<List<ManageServers_ServerListEntryDTO>>("/api/ManageServers/GetServerList");

                if (servers != null && servers.Count > 0)
                {
                    var firstServer = servers[0];
                    int serverId = firstServer.ServerId.GetValueOrDefault();

                    if (serverId > 0)
                    {
                        // Update localStorage and database
                        await _localStorage.SetItemAsync("activeServerId", serverId);
                        await _httpClient.PutAsync($"/api/ManageServers/UpdateSelectedServer/{serverId}", null);

                        Console.WriteLine($"[ServerValidation] Auto-selected server: {serverId} - {firstServer.ServerName}");
                        return true;
                    }
                }

                // No servers available at all
                await RedirectToManageServers("SelectServer");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServerValidation] Error auto-selecting server: {ex.Message}");
                await RedirectToManageServers("SelectServer");
                return false;
            }
        }

        /// <summary>
        /// Gets the currently selected server ID from localStorage.
        /// </summary>
        /// <returns>The selected server ID, or 0 if none selected</returns>
        public async Task<int> GetActiveServerIdAsync()
        {
            return await _localStorage.GetItemAsync<int>("activeServerId");
        }

        /// <summary>
        /// Gets the authentication token. Returns empty since auth is handled via HttpOnly cookies.
        /// </summary>
        /// <returns>Empty string — authentication is cookie-based</returns>
        public Task<string> GetAuthTokenAsync()
        {
            return Task.FromResult(string.Empty);
        }

        /// <summary>
        /// Redirects to the Manage Servers page with a SessionStorage notification.
        /// </summary>
        /// <param name="reason">The reason for redirection (e.g., "SelectServer")</param>
        private async Task RedirectToManageServers(string reason)
        {
            await _sessionStorage.SetItemAsStringAsync("ManageServers-RedirectReason", reason);
            _navHelper.NavigateTo("manage-servers");
        }
    }
}
