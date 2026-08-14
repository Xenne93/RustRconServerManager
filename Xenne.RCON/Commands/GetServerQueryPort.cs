using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xenne.RCON.Interfaces;

namespace Xenne.RCON.Commands
{
    public class GetServerQueryPort : RconCommand, IRconCommand
    {
        public GetServerQueryPort()
        {
            Message = "server.queryport";
            Purpose = RconCommandPurpose.BackgroundPoll;
        }






    }
}
