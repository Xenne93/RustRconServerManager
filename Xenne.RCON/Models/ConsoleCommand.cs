using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xenne.RCON.Models
{
    public class ConsoleCommand
    {
        public int Identifier { get; set; }
        public string Message { get; set; }
        public string Name = "WebRcon";

        public ConsoleCommand(int identifier, string message)
        {
            Identifier = identifier;
            Message = message;
        }



    }
}

