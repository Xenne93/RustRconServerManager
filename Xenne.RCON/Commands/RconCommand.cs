using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xenne.RCON.Interfaces;
using Xenne.RCON.Models;

namespace Xenne.RCON.Commands
{
    public class RconCommand : IRconCommand
    {

        public int Identifier { get; set; } // Unique identifier for the command
        public string Name { get; set; } = "WebRcon";
        public string Message { get; set; } // The name of the command, default is "WebRcon"
        public bool isSent = false; // Flag to indicate if the command has been sent
        public string Answer { get; set; } // Placeholder for the response from the server

        // Defaults to Manual so any command type nobody has explicitly categorized (typed
        // console input, one-off admin actions, anything new added later) is visible in the
        // live console by default - only the specific periodic-poll command classes opt out.
        public RconCommandPurpose Purpose { get; set; } = RconCommandPurpose.Manual;


        public async Task<RconCommand> ExecuteAsync(RconClient client)
        {
            // Check if the client is not null and is connected to the server
            if (client == null || !client.IsConnected)
            {
                throw new InvalidOperationException("Client is not connected to the server.");
            }

            // client.ExecuteCommand already assigns the identifier and registers the command in
            // client.PendingCommands - it must not be duplicated here. Registering it a second time
            // under a different identifier left a permanently orphaned entry in PendingCommands for
            // every single command sent (the timeout sweeper skips it too, since it shares the same
            // Command instance whose isSent flag flips true once the real response arrives) - an
            // unbounded memory leak that grew with every periodic RCON poll.
            await client.ExecuteCommand(this);

            return this;
        }

        


    }
}
