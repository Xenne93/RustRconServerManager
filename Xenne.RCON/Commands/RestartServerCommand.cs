using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xenne.RCON.Interfaces;

namespace Xenne.RCON.Commands
{
    public class RestartServerCommand : RconCommand, IRconCommand
    {
        public RestartServerCommand()
        {
            Message = "restart 60";
        }

    }
}
