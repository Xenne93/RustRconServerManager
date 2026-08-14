using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.SignalRHubs;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Shared.LiveChatHub;
using Xenne.RCON;
using Xenne.RCON.Commands;
using Xenne.RCON.Models;

namespace RustRconServerManager.Backend.Services;


// This partial class of RconBackgroundService handles all incoming command answers.
// These commands are requested by the RconBackgroundService.Periodic partial class.

public partial class RconBackgroundService : BackgroundService, IRconBackgroundService
{

    // TODO: Replace functionality so the database does not select complete query but only executes the update async:
    // await db.RconServers.Where(r => r.Id == serverId).ExecuteUpdateAsync(s => s.SetProperty(r => r.QueryPort, _ => port));

   
    public async Task OnChatReceived(int serverId, string message, string playerid, string playername, string channel)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        ChatMessage chatmessage = new ChatMessage();
        chatmessage.Message = message;
        chatmessage.Channel = channel;
        chatmessage.SteamId = playerid;
        chatmessage.PlayerName = playername;
        chatmessage.Timestamp = DateTime.UtcNow;
        chatmessage.ServerId = serverId;

        await db.ChatMessages.AddAsync(chatmessage);
        await db.SaveChangesAsync();
        
        LiveChatHub_ChatMessageDto chatMessageDto = new LiveChatHub_ChatMessageDto();
        chatMessageDto.Message = message;
        chatMessageDto.ServerId = serverId;
        chatMessageDto.Username = playername;
        chatMessageDto.SteamId = playerid;
        chatMessageDto.Timestamp = chatmessage.Timestamp;
        
        // Push naar iedereen in de groep van die server
        await _liveChatHub.Clients.Group($"{LiveChatHub.ServerGroupPrefix}{serverId}")
            .SendAsync("ReceivedChatMessage", chatMessageDto);

        // Execute triggers for chat messages
        _ = Task.Run(async () => await _triggerExecutionService.OnChatMessageAsync(serverId, playername, playerid, message, channel));

        _logger.LogInformation("[Server {ServerId}] GLOBAL CHAT: {Message}", serverId, message);
    }
    

    public async Task SendChatMessageToServer(string message, int serverId)
    {
        if (!_rconConnectionManager.TryGetClient(serverId, out var client))
            return;

        await client.SendCommandAsync("say " + message);
    }

   
}
