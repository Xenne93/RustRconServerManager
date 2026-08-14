using RustRconServerManager.Backend.Models;
using Xenne.RCON.Commands;


// Interface for the RconBackgroundService
namespace RustRconServerManager.Backend.Interfaces
{
    public interface IRconBackgroundService
    {

        public void SendRconCommand(RconCommand command, int serverId);

        public void SendRconCommand(string command, int serverId);

        public Task<string?> ExecuteCommandWithResponse(string command, int serverId, int timeoutMs = 5000);

        public Task HandleNewServer(RconServer server);

        public Task<bool> DisconnectServerAsync(int serverId);

        public Task HandleDeleteServer(RconServer server);

        public Task SendChatMessageToServer(string message, int serverId);

        public Task<bool> IsServerConnected(int serverId);


    }
}

