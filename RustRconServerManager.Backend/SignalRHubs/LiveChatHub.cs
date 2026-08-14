using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Shared.LiveChatHub;

namespace RustRconServerManager.Backend.SignalRHubs;

[Authorize]
public class LiveChatHub : Hub
{
    private readonly AppDbContext _database;
    private readonly IRconBackgroundService _rconBackgroundService;

    // Zelfde group-prefix als console
    public const string ServerGroupPrefix = "server_";
    private const string ConnectionServerKey = "activeServerId";

    public LiveChatHub(AppDbContext database, IRconBackgroundService rconBackgroundService) : base()
    {
        _database = database;
        _rconBackgroundService = rconBackgroundService;
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

    /// <summary>Client roept dit aan na reconnect of bij serverwissel.</summary>
    public async Task RefreshServerSubscription(CancellationToken cancellation = default)
        => await JoinSelectedServerGroupAsync(cancellation);

    /// <summary>Client stuurt alleen de platte message; server verrijkt en broadcast DTO.</summary>
    [HubMethodName("SendMessageToCurrentServer")]
    public async Task SendMessageToCurrentServer(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var (currentServerId, _) = await GetCurrentUserSelectedServerAsync(CancellationToken.None);

        var dto = new LiveChatHub_ChatMessageDto
        {
            ServerId = currentServerId,
            Username = Context.User?.Identity?.Name
                       ?? Context.User?.FindFirstValue(ClaimTypes.Email)
                       ?? "Unknown",
            SteamId = null, // Voeg hier je eigen logica toe als je SteamId kunt resolven
            Message = message.Trim(),
            Timestamp = DateTime.UtcNow
        };

        await Clients.Group($"{ServerGroupPrefix}{currentServerId}")
            .SendAsync("ReceivedChatMessage", dto);
        
        // Server verrijkt en broadcast DTO
        await _rconBackgroundService.SendChatMessageToServer(dto.Message, currentServerId);
        
    }

    [HubMethodName("Ping")]
    public Task Ping() => Task.CompletedTask;

    // ----------------- Helpers -----------------

    private async Task JoinSelectedServerGroupAsync(CancellationToken cancellation = default)
    {
        var (selectedServerId, previousServerId) = await GetCurrentUserSelectedServerAsync(cancellation);

        if (previousServerId.HasValue && previousServerId.Value > 0 && previousServerId.Value != selectedServerId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"{ServerGroupPrefix}{previousServerId.Value}", cancellation);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"{ServerGroupPrefix}{selectedServerId}", cancellation);
        Context.Items[ConnectionServerKey] = selectedServerId;
    }

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

        bool hasAccess = await Context.User!.HasServerAccess(_database, selectedServerId);
        if (!hasAccess)
            throw new HubException("Geen toegang tot deze server.");

        int? previousServerId = null;
        if (Context.Items.TryGetValue(ConnectionServerKey, out var oldValue) && oldValue is int prevId && prevId > 0)
            previousServerId = prevId;

        return (selectedServerId, previousServerId);
    }
}
