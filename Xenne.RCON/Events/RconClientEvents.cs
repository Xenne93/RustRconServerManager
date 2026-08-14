using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Xenne.RCON.Events
{
    public class RconClientEvents
    {


        // Event arguments for when a message is received (e.g. message from the server)
        public class MessageReceivedEventArgs : EventArgs
        {
            public int ServerId { get; set; } // Unique identifier for the server
            public int Identifier { get; set; } // Unique identifier for the command
            public string Message { get; set; } // The message of the command
            public string Name { get; set; } // The name of the command
            public MessageReceivedEventArgs(int identifier, string message, string name, int serverid)
            {
                ServerId = serverid; // Example server ID, replace with actual server ID if needed
                Identifier = identifier;
                Message = message;
                Name = name;
            }
        }

        // Event arguments for when global chat message is received
        public class ChatMessageReceivedEventArgs : EventArgs
        {
            public int ServerId { get; set; } // Unique identifier for the server
            public int Channel { get; set; }
            public string ChatMessage { get; set; } // The message of the command
            public string PlayerId { get; set; } // The player ID of the player that sent the message
            public string PlayerName { get; set; } // The name of the player that sent the message
            public ChatMessageReceivedEventArgs(int serverid, int channel, string playerId, string chatMessage, string playerName)
            {
                ServerId = serverid; // Example server ID, replace with actual server ID if needed
                Channel = channel;
                ChatMessage = chatMessage;
                PlayerId = playerId;
                PlayerName = playerName;
            }
        }
        

        // Event arguments for when a command answer is received (e.g. received answer on identifier)
        public class CommandAnswerReceivedEventArgs: EventArgs
        {
            public int ServerId { get; set; } // Unique identifier for the server
            public int Identifier { get; set; } // Unique identifier for the command
            public string Message { get; set; } // The answer from the server
            public string Name { get; set; } // The name of the command

            public string Command { get; set; } // The command that was sent to the server

            public Commands.RconCommandPurpose Purpose { get; set; } // Whether this was a background poll or a manual/user-facing command

            public CommandAnswerReceivedEventArgs(int identifier, string message, string name, int serverid, string command, Commands.RconCommandPurpose purpose = Commands.RconCommandPurpose.Manual)
            {
                ServerId = serverid; // Example server ID, replace with actual server ID if needed
                Identifier = identifier;
                Message = message;
                Name = name;
                Command = command;
                Purpose = purpose;
            }
        }

        // Event arguments for when a connection to the server has been closed)
        public class ConnectionClosedEventArgs : EventArgs
        {
            public int ServerId { get; set; } // Unique identifier for the server
            public int Identifier { get; set; } // Unique identifier for the command
            public string Message { get; set; } // The answer from the server
            public string Name { get; set; } // The name of the command

            public ConnectionClosedEventArgs(int identifier, string message, string name, int serverid)
            {
                ServerId = serverid; // Example server ID, replace with actual server ID if needed
                Identifier = identifier;
                Message = message;
                Name = name;
            }
        }
        
        // Event arguments for when a player has connected to the server
        public class PlayerConnectedEventArgs : EventArgs
        {
            public int ServerId { get; set; } // Unique identifier for the server
            public string PlayerId { get; set; } // The player ID of the player that connected
            public string PlayerName { get; set; } // The name of the player that connected
            public string PlayerEndpoint { get; set; }
            public PlayerConnectedEventArgs(int serverid, string playerId, string playerName, string playerEndpoint)
            {
                ServerId = serverid; // Example server ID, replace with actual server ID if needed
                PlayerId = playerId;
                PlayerName = playerName;
                PlayerEndpoint = playerEndpoint;
            }
        }
        
        public class PlayerDisconnectedEventArgs : EventArgs
        {
            public int ServerId { get; set; }
            public string PlayerId { get; set; }
            public string PlayerName { get; set; }
            public string Reason { get; set; }
            public PlayerDisconnectedEventArgs(int serverid, string playerId, string playerName, string reason = "")
            {
                ServerId = serverid;
                PlayerId = playerId;
                PlayerName = playerName;
                Reason = reason;
            }
        }
        
        
        public class PlayerKillEventArgs : EventArgs
        {
            public int ServerId { get; }
            public string KillerName { get; }
            public string KillerId { get; }
            public string VictimName { get; }
            public string VictimId { get; }
            public string Position { get; }

            public PlayerKillEventArgs(int serverId, string killerName, string killerId, string victimName, string victimId, string position)
            {
                ServerId = serverId;
                KillerName = killerName;
                KillerId = killerId;
                VictimName = victimName;
                VictimId = victimId;
                Position = position;
            }
        }

        public class PlayerReportedEventArgs : EventArgs
        {
            public int ServerId { get; }
            public string ReporterName { get; }
            public string ReporterId { get; }
            public string ReportedName { get; }
            public string ReportedId { get; }
            public string Subject { get; }
            public string Message { get; }
            public string Type { get; }
            public PlayerReportedEventArgs(int serverId, string reporterName, string reporterId, string reportedName, string reportedId, string subject, string message, string type)
            {
                ServerId = serverId;
                ReporterName = reporterName;
                ReporterId = reporterId;
                ReportedName = reportedName;
                ReportedId = reportedId;
                Subject = subject;
                Message = message;
                Type = type;
            }
        }



    }
}
