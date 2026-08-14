using Xenne.RCON.Commands;

namespace Xenne.RCON.Models;

public class PendingCommandEntry
{
    public RconCommand Command { get; set; }
    public DateTime Timestamp { get; set; }

    public PendingCommandEntry(RconCommand command)
    {
        Command = command;
        Timestamp = DateTime.UtcNow;
    }
}