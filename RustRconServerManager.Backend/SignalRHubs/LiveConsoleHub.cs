using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;

namespace RustRconServerManager.Backend.SignalRHubs;



// TODO:
// ConsoleCard is not receiving any live feed. Cause is that the methods in LiveConsoleHub (this file)
// are not present.

[Authorize]
public class LiveConsoleHub : Hub
{
    private readonly AppDbContext _database;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRconBackgroundService _rconBackgroundService;
    private readonly ILogger<LiveConsoleHub> _logger;

    // Eén vaste prefix voor alle SignalR groups
    public const string ServerGroupPrefix = "server_";

    // Key om in Context.Items op te slaan welke server-id deze verbinding gebruikt
    private const string ConnectionServerKey = "activeServerId";

    public LiveConsoleHub(AppDbContext database, IServiceScopeFactory scopeFactory, IRconBackgroundService backgroundService, ILogger<LiveConsoleHub> logger) : base()
    {
        _database = database;
        _scopeFactory = scopeFactory;
        _rconBackgroundService = backgroundService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        await JoinSelectedServerGroupAsync();
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(ConnectionServerKey, out var oldValue) && oldValue is int oldServerId && oldServerId > 0)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"{ServerGroupPrefix}{oldServerId}");
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client roept dit aan na reconnect of wanneer gebruiker van server wisselt.
    /// </summary>
    public async Task RefreshServerSubscription(CancellationToken cancellation = default)
    {
        await JoinSelectedServerGroupAsync(cancellation);
    }

    /// <summary>
    /// Voorbeeld van hoe een client een command naar de geselecteerde server kan sturen.
    /// </summary>
    
    [HubMethodName("SendCommandToCurrentServer")]
    public async Task SendCommandToCurrentServer(string command)
    {
        _logger.LogDebug("Send Command to server triggered");
        if (string.IsNullOrWhiteSpace(command)) return;

        var (currentServerId, _) = await GetCurrentUserSelectedServerAsync(CancellationToken.None);
        _rconBackgroundService.SendRconCommand(command, currentServerId);
    }
    
    [HubMethodName("ReceiveConsole")]
    public async Task ReceiveConsole(string message, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // Haal op welke server de gebruiker actief heeft geselecteerd
        var (currentServerId, _) = await GetCurrentUserSelectedServerAsync(cancellation);

        // Stuur alleen naar de group van deze server
        await Clients.Group($"{ServerGroupPrefix}{currentServerId}")
            .SendAsync("ReceiveConsole", message, cancellation);
    }


   
    
    [HubMethodName("Ping")]
    public async Task Ping()
    {
        _logger.LogTrace("Ping from client");
        await Task.CompletedTask;
    }

    // ----------------- Private hulpfuncties -----------------

    private async Task JoinSelectedServerGroupAsync(CancellationToken cancellation = default)
    {
        var (selectedServerId, previousServerId) = await GetCurrentUserSelectedServerAsync(cancellation);

        // Oude group verlaten als deze verschilt
        if (previousServerId.HasValue && previousServerId.Value > 0 && previousServerId.Value != selectedServerId)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                $"{ServerGroupPrefix}{previousServerId.Value}",
                cancellation
            );
        }

        // Nieuwe group joinen
        await Groups.AddToGroupAsync(Context.ConnectionId, $"{ServerGroupPrefix}{selectedServerId}", cancellation);
        Context.Items[ConnectionServerKey] = selectedServerId;
    }

    /// <summary>
    /// Haalt de SelectedServerId van de ingelogde gebruiker op en checkt autorisatie.
    /// </summary>
    private async Task<(int selectedServerId, int? previousServerId)> GetCurrentUserSelectedServerAsync(CancellationToken cancellation)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            throw new HubException("Niet ingelogd.");

        var selectedServerInfo = await _database.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.SelectedServerId })
            .FirstOrDefaultAsync(cancellation);

        if (selectedServerInfo is null || selectedServerInfo.SelectedServerId <= 0)
            throw new HubException("Geen server geselecteerd.");

        
        int selectedServerId = selectedServerInfo.SelectedServerId ?? 0;

        
        // Check of gebruiker toegang heeft tot deze server
        bool hasAccess = await Context.User!.HasServerAccess(_database, selectedServerId);
        if (!hasAccess)
            throw new HubException("Geen toegang tot deze server.");

        int? previousServerId = null;
        if (Context.Items.TryGetValue(ConnectionServerKey, out var oldValue) && oldValue is int prevId && prevId > 0)
            previousServerId = prevId;

        return (selectedServerId, previousServerId);
    }
}
