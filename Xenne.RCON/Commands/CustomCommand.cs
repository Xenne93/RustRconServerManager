using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xenne.RCON.Interfaces;

namespace Xenne.RCON.Commands
{
    public class CustomCommand : RconCommand, IRconCommand
    {
        public CustomCommand(string cmd)
        {
            Message = cmd;
        }






    }
}
