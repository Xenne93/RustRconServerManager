namespace Xenne.RCON.Commands
{
    /// <summary>
    /// Why a command was sent, set by the caller when constructing the command - never sent
    /// over the wire (the outbound RCON envelope only carries Identifier/Message/Name) and
    /// never touched by the game server's response, so it survives the whole
    /// send -> PendingCommands -> matched-response round trip untouched. This is what lets
    /// the live console decide whether to show a response without guessing from the command
    /// text alone, which previously meant a manually-typed command sharing a name with a
    /// background-polled one (e.g. typing "fps") got silently swallowed.
    /// </summary>
    public enum RconCommandPurpose
    {
        /// <summary>Typed into the live console, or any other explicit one-off action (kick,
        /// ban, give item, etc.) - shown in the live console.</summary>
        Manual,

        /// <summary>Sent automatically by the periodic background poller - never shown in the
        /// live console, even though its answer is still processed normally.</summary>
        BackgroundPoll
    }
}
