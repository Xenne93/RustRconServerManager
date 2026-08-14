using System.Text.Json;
using System.Text.RegularExpressions;
using Xenne.RCON.Models;

namespace Xenne.RCON.Helpers;

public class ParseChatMessage
{
   
    public static ChatMessageModel? ProcessChatMessage(string input)
    {
        try
        {
            return JsonSerializer.Deserialize<ChatMessageModel>(input);
        }
        catch
        {
            return null;
        }
    }
    

}