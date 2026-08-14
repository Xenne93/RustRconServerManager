using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xenne.RCON.Commands;

namespace Xenne.RCON.Interfaces
{
    internal interface IRconCommand
    {

        int Identifier { get; set; } // Unique identifier for the command
        string Message { get; set; } // The command message to be sent to the server
        string Name { get; set; }


        Task<RconCommand> ExecuteAsync(RconClient client);

    }
}
