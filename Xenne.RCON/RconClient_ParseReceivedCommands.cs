using System.Text.Json;
using Xenne.RCON.Events;
using Xenne.RCON.Helpers;
using Xenne.RCON.Models;

namespace Xenne.RCON;

public partial class RconClient
{
  public async Task ParseReceivedCommands(string message)
  {
    // PlayerReport JSON comes as a separate message with these unique fields
    // Check for ALL PlayerReport-specific fields to ensure it's truly a report
    if (message.Contains("\"PlayerId\"") &&
        message.Contains("\"PlayerName\"") &&
        message.Contains("\"TargetId\"") &&
        message.Contains("\"TargetName\"") &&
        message.Contains("\"Subject\"") &&
        message.Contains("\"Message\"") &&
        message.Contains("\"Type\""))
    {
      try
      {
        // The entire message is the JSON
        var jsonPart = message.Trim();

        // Parse the JSON
        var reportData = JsonSerializer.Deserialize<PlayerReportJson>(jsonPart, new JsonSerializerOptions
        {
          PropertyNameCaseInsensitive = true
        });

        if (reportData == null)
        {
          Console.WriteLine("Failed to deserialize PlayerReport JSON");
          return;
        }

        // Raise the event
        try
        {
          
          OnPlayerReported?.Invoke(this, new RconClientEvents.PlayerReportedEventArgs(
            ServerId,
            reportData.PlayerName,
            reportData.PlayerId,
            reportData.TargetName,
            reportData.TargetId,
            reportData.Subject,
            reportData.Message,
            reportData.Type
          ));
          Console.WriteLine("--------------------------------");

          Console.WriteLine("RCONCLIENT RECEIVED PLAYER REPORT:");
          Console.WriteLine(JsonSerializer.Serialize(reportData));
          Console.WriteLine("--------------------------------");
        }
        catch (Exception eventEx)
        {
          Console.WriteLine($"Error in OnPlayerReported event handler: {eventEx.Message}");
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error parsing player report: {ex.Message}");
      }
    }

    if (message.Contains("joined"))
    {
      try
      {
        // Split de string op "/"
        var segments = message.Split('/');

        // Haal username op (verwijder " joined" en alles erna)
        var userName = segments[2].Substring(0, segments[2].IndexOf(" joined"));

        try
        {
          OnPlayerConnected?.Invoke(this, new RconClientEvents.PlayerConnectedEventArgs(ServerId, userName, segments[1], segments[0]));
        }
        catch (Exception eventEx)
        {
          Console.WriteLine($"Error in OnPlayerConnected event handler: {eventEx.Message}");
        }
      }
      catch (Exception ex)
      {
        // Log de fout
        Console.WriteLine($"Error parsing player connection: {ex.Message}");
      }
    }


    if (message.Contains("disconnecting:"))
    {
      try
      {
        // Format: IP:PORT/STEAMID/USERNAME disconnecting: reason
        var disconnectIndex = message.IndexOf(" disconnecting:");
        var prefix = message.Substring(0, disconnectIndex);
        var reason = message.Substring(disconnectIndex + " disconnecting:".Length).Trim();

        var segments = prefix.Split('/');
        if (segments.Length >= 3)
        {
          var steamId = segments[1];
          var playerName = segments[2];

          try
          {
            OnPlayerDisconnected?.Invoke(this, new RconClientEvents.PlayerDisconnectedEventArgs(ServerId, steamId, playerName, reason));
          }
          catch (Exception eventEx)
          {
            Console.WriteLine($"Error in OnPlayerDisconnected event handler: {eventEx.Message}");
          }
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error parsing player disconnection: {ex.Message}");
      }
    }

    if (message.Contains("Channel") && message.Contains("Message"))
    {
      Console.WriteLine("TEST MESSAGE RECEIVED");
      try
      {
        ChatMessageModel chatmessage = ParseChatMessage.ProcessChatMessage(message);

        try
        {
          OnChatMessageReceived?.Invoke(this, new RconClientEvents.ChatMessageReceivedEventArgs(ServerId, chatmessage.Channel, chatmessage.UserId, chatmessage.Message, chatmessage.Username));
        }
        catch (Exception eventEx)
        {
          Console.WriteLine($"Error in OnChatMessageReceived event handler: {eventEx.Message}");
        }

        Console.WriteLine("RCONCLIENT RECEIVED CHAT MESSAGE GLOBAL CHAT");
      }
      catch
      {
        Console.WriteLine("Could not parse chat message... Ignoring...");
      }
    }
                        


    if (message.Contains("was killed by") && message.Contains(" at "))
    {
      try
      {
        // Split the parts
        var parts = message.Split(" was killed by ");
        var victimRaw = parts[0];
        var rest = parts[1].Split(" at ");
        var killerRaw = rest[0];
        var position = rest[1];

        // Parse victim info
        var victimName = victimRaw.Contains("[")
          ? victimRaw.Substring(0, victimRaw.IndexOf("["))
          : victimRaw;

        var victimId = victimRaw.Contains("[")
          ? victimRaw.Substring(victimRaw.IndexOf("[") + 1).TrimEnd(']')
          : "unknown";

        string killerName, killerId;

        // Check if the killer has an ID (e.g., Suicide does not have one)
        if (killerRaw.Contains("["))
        {
          killerName = killerRaw.Substring(0, killerRaw.IndexOf("["));
          killerId = killerRaw.Substring(killerRaw.IndexOf("[") + 1).TrimEnd(']');
        }
        else
        {
          killerName = killerRaw;
          killerId = "environment"; // of "unknown", of null
        }

        try
        {
          OnPlayerKill?.Invoke(this, new RconClientEvents.PlayerKillEventArgs(
            ServerId,
            killerName,
            killerId,
            victimName,
            victimId,
            position
          ));
        }
        catch (Exception eventEx)
        {
          Console.WriteLine($"Error in OnPlayerKill event handler: {eventEx.Message}");
        }
      }
      catch
      {
        Console.WriteLine("Could not parse kill message... Ignoring...");
      }
    }
    
  }
  
}