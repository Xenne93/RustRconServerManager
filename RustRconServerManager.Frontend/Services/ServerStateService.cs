using Blazored.LocalStorage;
using Microsoft.JSInterop;
using System.Net.Http.Json;

namespace RustRconServerManager.Frontend.Services
{
    /// <summary>
    /// Centralized service for managing the currently selected server state.
    /// This ensures all components use the same server selection data.
    /// </summary>
    public class ServerStateService
    {
        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorage;
        private readonly IJSRuntime _jsRuntime;
        private int? _cachedServerId = null;
        private string? _cachedServerName = null;
        private bool _isConnected = true; // Start as connected to avoid false banner on initial load
        private bool _hasCheckedOnce = false; // Track if we've done at least one check
        private System.Threading.Timer? _connectionCheckTimer;
        private System.Threading.Timer? _retryCountdownTimer;
        private int _retryCountdown = 15;
        private const int RETRY_INTERVAL_SECONDS = 15;

        public event Action? OnServerChanged;
        public event Action? OnConnectionStatusChanged;

        public ServerStateService(HttpClient httpClient, ILocalStorageService localStorage, IJSRuntime jsRuntime)
        {
            _httpClient = httpClient;
            _localStorage = localStorage;
            _jsRuntime = jsRuntime;
        }

        /// <summary>
        /// Gets the currently selected server ID.
        /// Priority: in-memory cache → localStorage → database (fallback for new devices).
        /// </summary>
        public async Task<int?> GetSelectedServerIdAsync()
        {
            // 1. Return cached value if available
            if (_cachedServerId.HasValue)
            {
                return _cachedServerId;
            }

            // 2. Check localStorage (per-device source of truth)
            try
            {
                int localValue = await _localStorage.GetItemAsync<int>("activeServerId");
                if (localValue > 0)
                {
                    _cachedServerId = localValue;
                    Console.WriteLine($"[ServerStateService] Restored selected server from localStorage: {localValue}");
                    return _cachedServerId;
                }
            }
            catch
            {
                // localStorage might not be available yet
            }

            // 3. Fallback: load from database (for first login on a new device)
            await LoadSelectedServerFromDatabaseAsync();
            return _cachedServerId;
        }

        /// <summary>
        /// Gets the currently selected server name
        /// </summary>
        public string? GetSelectedServerName()
        {
            return _cachedServerName;
        }

        /// <summary>
        /// Updates the selected server in database and notifies all listeners
        /// </summary>
        public async Task<bool> SetSelectedServerAsync(int serverId, string serverName)
        {
            try
            {
                Console.WriteLine($"[ServerStateService] Updating selected server to: {serverId} - {serverName}");

                // Update in database first — cookie is sent automatically
                var response = await _httpClient.PutAsync($"/api/ManageServers/UpdateSelectedServer/{serverId}", null);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[ServerStateService] Error updating server: {response.StatusCode} - {errorContent}");
                    return false;
                }

                Console.WriteLine($"[ServerStateService] Successfully updated server in database");

                // Reset connection-check state on an actual server switch so the newly
                // selected server never inherits a stale connected/disconnected verdict
                // (or a banner stuck hidden because monitoring never ran) from whatever
                // was selected before - the next poll always re-evaluates fresh.
                if (_cachedServerId != serverId)
                {
                    _hasCheckedOnce = false;
                    _isConnected = true;
                }

                // Update cache
                _cachedServerId = serverId;
                _cachedServerName = serverName;

                // Update localStorage for offline use
                await _localStorage.SetItemAsync("activeServerId", serverId);

                // Notify all listeners that the server has changed
                OnServerChanged?.Invoke();

                Console.WriteLine($"[ServerStateService] Notified all listeners of server change");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServerStateService] Exception updating server: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads the selected server from database as a fallback (e.g. first login on a new device).
        /// Does NOT overwrite localStorage if it already has a value.
        /// </summary>
        public async Task LoadSelectedServerFromDatabaseAsync()
        {
            try
            {
                // Don't overwrite localStorage if it already has a value
                try
                {
                    int existingLocal = await _localStorage.GetItemAsync<int>("activeServerId");
                    if (existingLocal > 0)
                    {
                        _cachedServerId = existingLocal;
                        Console.WriteLine($"[ServerStateService] localStorage already has server {existingLocal}, skipping DB fetch");
                        return;
                    }
                }
                catch { /* localStorage not available yet, continue to DB */ }

                // Cookie is sent automatically — no manual Authorization header needed
                var response = await _httpClient.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/Account/SelectedServerId");

                if (response.ValueKind != System.Text.Json.JsonValueKind.Undefined &&
                    response.TryGetProperty("selectedServerId", out var serverIdElement) &&
                    serverIdElement.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    int serverId = serverIdElement.GetInt32();
                    _cachedServerId = serverId;

                    // Sync to localStorage (only because it was empty)
                    await _localStorage.SetItemAsync("activeServerId", serverId);

                    Console.WriteLine($"[ServerStateService] Loaded selected server from database (fallback): {serverId}");
                }
                else
                {
                    Console.WriteLine("[ServerStateService] No selected server found in database");
                    _cachedServerId = null;
                    _cachedServerName = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServerStateService] Error loading selected server from database: {ex.Message}");
                _cachedServerId = null;
                _cachedServerName = null;
            }
        }

        /// <summary>
        /// Clears the cached server data (used on logout)
        /// </summary>
        public void ClearCache()
        {
            _cachedServerId = null;
            _cachedServerName = null;
            StopConnectionMonitoring();
        }

        /// <summary>
        /// Gets whether a server is currently selected
        /// </summary>
        public bool HasServerSelected => _cachedServerId.HasValue && _cachedServerId > 0;

        /// <summary>
        /// Gets the current connection status
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Gets whether we should show the offline banner (only after first check, and only when a server is selected)
        /// </summary>
        public bool ShouldShowOfflineBanner => HasServerSelected && _hasCheckedOnce && !_isConnected;

        /// <summary>
        /// Gets the retry countdown in seconds
        /// </summary>
        public int RetryCountdown => _retryCountdown;

        /// <summary>
        /// Starts monitoring the server connection status
        /// </summary>
        public void StartConnectionMonitoring()
        {
            // Stop any existing timers
            StopConnectionMonitoring();

            // Start checking connection status every 3 seconds
            _connectionCheckTimer = new System.Threading.Timer(
                async _ => await CheckConnectionStatus(),
                null,
                TimeSpan.Zero, // Start immediately
                TimeSpan.FromSeconds(3)
            );

            // Start countdown timer (ticks every second)
            _retryCountdownTimer = new System.Threading.Timer(
                _ => UpdateRetryCountdown(),
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1)
            );
        }

        /// <summary>
        /// Stops monitoring the server connection status
        /// </summary>
        public void StopConnectionMonitoring()
        {
            _connectionCheckTimer?.Dispose();
            _connectionCheckTimer = null;

            _retryCountdownTimer?.Dispose();
            _retryCountdownTimer = null;
        }

        /// <summary>
        /// Checks the server connection status
        /// </summary>
        private async Task CheckConnectionStatus()
        {
            try
            {
                // Skip when the app is in the background (Capacitor minimized, tab hidden).
                // document.hidden = true whenever the page is not visible to the user.
                try
                {
                    var isHidden = await _jsRuntime.InvokeAsync<bool>("eval", "document.hidden");
                    if (isHidden) return;
                }
                catch { /* JSInterop unavailable during pre-render or disposal — skip silently */ }

                // Ensure cache is populated from localStorage
                if (!_cachedServerId.HasValue)
                {
                    await GetSelectedServerIdAsync();
                }

                if (!_cachedServerId.HasValue || _cachedServerId <= 0)
                {
                    // No server selected — not an error, just nothing to check
                    return;
                }

                // Cookie is sent automatically — no manual Authorization header needed
                var response = await _httpClient.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/Rcon/CheckServerConnection");

                bool newConnectionStatus = false;
                if (response.ValueKind != System.Text.Json.JsonValueKind.Undefined &&
                    response.TryGetProperty("isConnected", out var isConnectedElement))
                {
                    newConnectionStatus = isConnectedElement.GetBoolean();
                }

                // Mark that we've done at least one check
                bool wasFirstCheck = !_hasCheckedOnce;
                _hasCheckedOnce = true;

                // If status changed, notify listeners
                if (newConnectionStatus != _isConnected || wasFirstCheck)
                {
                    _isConnected = newConnectionStatus;

                    // Reset countdown when connection is lost
                    if (!_isConnected)
                    {
                        _retryCountdown = RETRY_INTERVAL_SECONDS;
                    }

                    OnConnectionStatusChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServerStateService] Error checking connection status: {ex.Message}");

                // Mark that we've done at least one check
                bool wasFirstCheck = !_hasCheckedOnce;
                _hasCheckedOnce = true;

                // Assume disconnected on error
                if (_isConnected || wasFirstCheck)
                {
                    _isConnected = false;
                    _retryCountdown = RETRY_INTERVAL_SECONDS;
                    OnConnectionStatusChanged?.Invoke();
                }
            }
        }

        /// <summary>
        /// Updates the retry countdown
        /// </summary>
        private void UpdateRetryCountdown()
        {
            if (!_isConnected)
            {
                _retryCountdown--;

                if (_retryCountdown <= 0)
                {
                    _retryCountdown = RETRY_INTERVAL_SECONDS;
                }

                OnConnectionStatusChanged?.Invoke();
            }
            else
            {
                _retryCountdown = RETRY_INTERVAL_SECONDS;
            }
        }
    }
}
