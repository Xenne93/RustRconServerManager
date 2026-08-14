using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xenne.RCON.Commands;
using Xenne.RCON.Events;
using Xenne.RCON.Helpers;
using Xenne.RCON.Models;


namespace Xenne.RCON;

public partial class RconClient : IDisposable
{
    private bool _disposed = false;
    
    public bool LogDebugMessages = false;

    // Event declarations
    //http://138.201.219.95/
    
    public event EventHandler<RconClientEvents.MessageReceivedEventArgs>? OnMessageReceived;
    public event EventHandler<RconClientEvents.CommandAnswerReceivedEventArgs>? OnCommandAnswerReceived;
    public event EventHandler<RconClientEvents.ConnectionClosedEventArgs>? OnConnectionClosed;
    public event EventHandler<RconClientEvents.ChatMessageReceivedEventArgs>? OnChatMessageReceived;
    public event EventHandler<RconClientEvents.PlayerKillEventArgs>? OnPlayerKill;

    public event EventHandler<RconClientEvents.PlayerConnectedEventArgs>? OnPlayerConnected;
    public event EventHandler<RconClientEvents.PlayerDisconnectedEventArgs>? OnPlayerDisconnected;
    public event EventHandler<RconClientEvents.PlayerReportedEventArgs>? OnPlayerReported;
    
    


    private CancellationTokenSource _ctsReceiver;
    private Task _receiverTask;
    private Task _timeoutCheckerTask;
    private CancellationTokenSource _ctsTimeoutChecker;
    
    

    public int CurrentIdentifier = 1; // Identifier for the command, can be used to track responses
    public string ServerAddress;
    public int RconPort;
    public int ServerId;
    public string RconPassword;
    public Uri uri;
    public ClientWebSocket socket;
    public bool IsConnected => socket.State == WebSocketState.Open;
    public ConcurrentDictionary<int, PendingCommandEntry> PendingCommands = new();
    
    private const int ReceiveChunkSize = 16 * 1024; // 16 KB per read


    
    // Constructor
    public RconClient(string _serverAddress, int _rconPort, string _rconPassword, int serverId)
    {
        ServerId = serverId; // Set the server ID for this client
        ServerAddress = _serverAddress;
        RconPort = _rconPort;
        RconPassword = _rconPassword;
        uri = new Uri($"ws://{ServerAddress}:{RconPort}/{RconPassword}");
        socket = new ClientWebSocket();
    }


    // Test if the connection can be made. If true, close the connection and return True.
    // Else, return false, indicates that the connection was not successful.
    public async Task<bool> TestConnection()
    {
        ClientWebSocket testSocket = null;
        try
        {
            // Create a new socket specifically for testing to avoid affecting the main socket
            testSocket = new ClientWebSocket();

            // Add timeout to test connection
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await testSocket.ConnectAsync(uri, cts.Token);

            // Close the connection gracefully after successful test (with timeout)
            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await testSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection test completed", closeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Close timed out, but connection was successful - abort instead
                testSocket.Abort();
            }

            return true;
        }
        catch (Exception e)
        {
            if (LogDebugMessages)
            {
                Console.WriteLine($"Connection test failed: {e.Message}");
            }
            return false;
        }
        finally
        {
            // Ensure the test socket is properly disposed
            testSocket?.Dispose();
        }
    }


    public async Task ConnectAsync()
    {
        try
        {
            // Add timeout to connection attempt
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(uri, cts.Token);

            if (LogDebugMessages)
            {
                Console.WriteLine("RCON Connected");
            }

            StartReceivingLoop();

        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Connection to server timed out after 10 seconds.");

            // Fire connection closed event to notify listeners of connection timeout
            OnConnectionClosed?.Invoke(this,
                new RconClientEvents.ConnectionClosedEventArgs(
                    CurrentIdentifier, "Connection timed out after 10 seconds", "ConnectTimeout", ServerId));

            throw new TimeoutException("Connection timed out after 10 seconds");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Could not connect to server: {e.Message}");

            // Fire connection closed event to notify listeners of connection failure
            OnConnectionClosed?.Invoke(this,
                new RconClientEvents.ConnectionClosedEventArgs(
                    CurrentIdentifier, $"Failed to connect: {e.Message}", "ConnectFailed", ServerId));

            throw; // Re-throw to let caller handle the failure
        }
    }

    
    // Sends a command to the RCON server and waits for a response
    // Adds the command to the waiting queue to match it's response.
    public async Task ExecuteCommand(RconCommand rconCommand)
    {
        if (!IsConnected)
        {
            if (LogDebugMessages)
            {
                Console.WriteLine("Not connected to server.");
            }

            return;
        }

        // Handle identifier overflow - reset to 1 when approaching max value
        CurrentIdentifier++;
        if (CurrentIdentifier >= int.MaxValue - 100)
        {
            CurrentIdentifier = 1;
            if (LogDebugMessages)
            {
                Console.WriteLine("Command identifier reset to prevent overflow.");
            }
        }

        rconCommand.Identifier = CurrentIdentifier;

        PendingCommands[CurrentIdentifier] = new PendingCommandEntry(rconCommand);

        var json = JsonSerializer.Serialize(new
        {
            Identifier = rconCommand.Identifier,
            Message = rconCommand.Message,
            Name = rconCommand.Name
        });

        var data = Encoding.UTF8.GetBytes(json);
        var buffer = new ArraySegment<byte>(data);

        try
        {
            await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);

            if (LogDebugMessages)
            {
                Console.WriteLine($"Sent command [{rconCommand.Message}] with ID {rconCommand.Identifier}");
            }

        }
        catch (WebSocketException wsex)
        {
            Console.WriteLine($"WebSocket error sending command: {wsex.Message}");
            // Clean up pending command since it was never sent
            PendingCommands.TryRemove(CurrentIdentifier, out _);

            // Fire disconnect event if WebSocket is in a failed state
            if (socket.State != WebSocketState.Open)
            {
                OnConnectionClosed?.Invoke(this,
                    new RconClientEvents.ConnectionClosedEventArgs(
                        CurrentIdentifier, $"WebSocket error: {wsex.Message}", "SendError", ServerId));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending command: {ex.Message}");
            // Clean up pending command
            PendingCommands.TryRemove(CurrentIdentifier, out _);
        }
    }
    
    
    


    // Sends manual command to the RCON server
    public async Task SendCommandAsync(string command)
    {
        if (socket.State != WebSocketState.Open)
        {
            if (LogDebugMessages)
            {
                Console.WriteLine("WebSocket is not open. Please connect first.");
            }

            return;
        }

        // Handle identifier overflow - reset to 1 when approaching max value
        CurrentIdentifier++;
        if (CurrentIdentifier >= int.MaxValue - 100)
        {
            CurrentIdentifier = 1;
            if (LogDebugMessages)
            {
                Console.WriteLine("Command identifier reset to prevent overflow.");
            }
        }

        try
        {
            // Create a ConsoleCommand object and serialize it to JSON
            var buffer = new ArraySegment<byte>(new byte[4096]); // Buffer for sending data
            var consoleCommand = new Models.ConsoleCommand(CurrentIdentifier, command);
            string jsonCommand = JsonSerializer.Serialize(consoleCommand);
            var jsonBytes = Encoding.UTF8.GetBytes(jsonCommand);
            buffer = new ArraySegment<byte>(jsonBytes);

            await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);

            if (LogDebugMessages)
            {
                Console.WriteLine($"Command sent: {command}");
            }
        }
        catch (WebSocketException wsex)
        {
            Console.WriteLine($"WebSocket error sending command: {wsex.Message}");

            // Fire disconnect event if WebSocket is in a failed state
            if (socket.State != WebSocketState.Open)
            {
                OnConnectionClosed?.Invoke(this,
                    new RconClientEvents.ConnectionClosedEventArgs(
                        CurrentIdentifier, $"WebSocket error: {wsex.Message}", "SendError", ServerId));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error sending command: {e.Message}");
        }
    }

    
    
    
    // Starts the receiving loop for incoming WebSocket messages.
    // Cancels the associated CancellationTokenSource to terminate the ongoing message receiving task.
   
    
    public void StartReceivingLoop()
{
    _ctsTimeoutChecker?.Cancel();
    _ctsTimeoutChecker = new CancellationTokenSource();
    _timeoutCheckerTask = StartPendingCommandTimeoutChecker(_ctsTimeoutChecker.Token);

    // Cancel vorige loop indien aanwezig
    _ctsReceiver?.Cancel();
    _ctsReceiver = new CancellationTokenSource();

    _receiverTask = Task.Run(async () =>
    {
        var buffer = new byte[ReceiveChunkSize];

        while (!_ctsReceiver.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            try
            {
                var (type, text) = await ReceiveFullMessageAsync(socket, buffer, _ctsReceiver.Token);

                if (type == WebSocketMessageType.Close)
                {
                    try
                    {
                        OnConnectionClosed?.Invoke(this,
                            new RconClientEvents.ConnectionClosedEventArgs(
                                CurrentIdentifier, "Server closed connection", "Disconnect", ServerId));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in OnConnectionClosed event handler: {ex.Message}");
                    }

                    if (LogDebugMessages)
                        Console.WriteLine("🔌 Server closed connection.");

                    break;
                }

                if (type != WebSocketMessageType.Text || string.IsNullOrEmpty(text))
                    continue;

                // We hebben nu het volledige bericht als string
                var response = text;

                using var doc = JsonDocument.Parse(response);
                int identifier = doc.RootElement.GetProperty("Identifier").GetInt32();
                string message = doc.RootElement.GetProperty("Message").GetString() ?? "";

                if (LogDebugMessages)
                    Console.WriteLine($"📥 Response [ID {identifier}]: {message}");

                if (PendingCommands.TryGetValue(identifier, out var entry))
                {
                    entry.Command.Answer = message;
                    entry.Command.isSent = true;

                    if (LogDebugMessages)
                        Console.WriteLine($"Matched to command [{entry.Command.Message}]");

                    PendingCommands.TryRemove(identifier, out _);

                    try
                    {
                        OnCommandAnswerReceived?.Invoke(this,
                            new RconClientEvents.CommandAnswerReceivedEventArgs(
                                identifier, message, entry.Command.Name, ServerId, entry.Command.Message, entry.Command.Purpose));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in OnCommandAnswerReceived event handler: {ex.Message}");
                    }
                }
                else
                {
                    if (LogDebugMessages)
                        Console.WriteLine(message);

                    // Parse async naar specifieke events
                    _ = Task.Run(() => ParseReceivedCommands(message));

                    // Algemeen log/event
                    try
                    {
                        OnMessageReceived?.Invoke(this,
                            new RconClientEvents.MessageReceivedEventArgs(
                                identifier, message, "Unknown Message", ServerId));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in OnMessageReceived event handler: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (LogDebugMessages)
                    Console.WriteLine("Receiving loop cancelled.");
                break;
            }
            catch (JsonException jex)
            {
                // Bescherm tegen halfslachtige of niet-JSON berichten zonder de loop te crashen
                Console.WriteLine($"JSON parse error: {jex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in receive loop: {ex.Message}");
                try
                {
                    OnConnectionClosed?.Invoke(this,
                        new RconClientEvents.ConnectionClosedEventArgs(
                            CurrentIdentifier, "Error in receive loop: " + ex.Message, "Disconnect", ServerId));
                }
                catch (Exception eventEx)
                {
                    Console.WriteLine($"Error in OnConnectionClosed event handler: {eventEx.Message}");
                }
                break;
            }
        }
    }, _ctsReceiver.Token);
}



    
    // Disconnect the websocket gracefully.
    // Raises OnConnectionClosed when closed correctly.
    // Cancels the Receiving Loop
    public async Task DisconnectAsync()
    {
        // Check if the state is open, if not, raise OnConnectionClosed
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected", CancellationToken.None);

                if (LogDebugMessages)
                {
                    Console.WriteLine("RCON Disconnected");
                }

                try
                {
                    OnConnectionClosed?.Invoke(this, new RconClientEvents.ConnectionClosedEventArgs(CurrentIdentifier, "Connection closed by client", "Disconnect", ServerId));
                }
                catch (Exception eventEx)
                {
                    Console.WriteLine($"Error in OnConnectionClosed event handler: {eventEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disconnecting: {ex.Message}");
                _ctsReceiver?.Cancel(); // Stop receiving loop
            }
        }
        else
        {
            if (LogDebugMessages)
            {
                Console.WriteLine("Not connected to server.");
            }

            try
            {
                OnConnectionClosed?.Invoke(this, new RconClientEvents.ConnectionClosedEventArgs(CurrentIdentifier, "Connection closed by client", "Disconnect", ServerId));
            }
            catch (Exception eventEx)
            {
                Console.WriteLine($"Error in OnConnectionClosed event handler: {eventEx.Message}");
            }

        }
        _ctsReceiver?.Cancel(); // Stop receiving loop
    }


    /// <summary>
    /// Stops the receiving loop for incoming WebSocket messages.
    /// Cancels the associated CancellationTokenSource to terminate the ongoing message receiving task.
    /// </summary>
    private void StopReceivingLoop()
    {
        _ctsReceiver?.Cancel();
    }
    
    public void ClearEventHandlers()
    {
        OnMessageReceived = null;
        OnCommandAnswerReceived = null;
        OnConnectionClosed = null;
        _ctsTimeoutChecker?.Cancel();
    }
    
    private Task StartPendingCommandTimeoutChecker(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken); // check elke seconde

                    var now = DateTime.UtcNow;

                    foreach (var kvp in PendingCommands.ToList())
                    {
                        try
                        {
                            var entry = kvp.Value;
                            if (entry?.Command == null) continue;
                            if ((now - entry.Timestamp).TotalSeconds > 10)
                            {
                                if (PendingCommands.TryGetValue(kvp.Key, out var latestEntry) &&
                                    latestEntry?.Command != null &&
                                    !latestEntry.Command.isSent)
                                {
                                    if (PendingCommands.TryRemove(kvp.Key, out _) && LogDebugMessages)
                                    {
                                        Console.WriteLine($"⏱️ Command ID: {kvp.Key} ({latestEntry.Command.Message}) timed out and was removed.");
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Entry was modified/removed by another thread, skip
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in timeout checker: {ex.Message}");
                }
            }
        }, cancellationToken);
    }
    
    
    // Helper

    private static async Task<(WebSocketMessageType type, string? text)> ReceiveFullMessageAsync(
        ClientWebSocket socket,
        byte[] buffer,
        CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Caller handelt de close-afhandeling verder af
                return (WebSocketMessageType.Close, null);
            }

            if (result.Count > 0)
                ms.Write(buffer, 0, result.Count);

        } while (!result.EndOfMessage);

        if (result.MessageType == WebSocketMessageType.Text)
        {
            // Reset position en decode naar UTF-8 tekst
            ms.Position = 0;
            using var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = await reader.ReadToEndAsync();
            return (WebSocketMessageType.Text, text);
        }

        // Als je ooit binaire berichten wilt ondersteunen, kun je hier bytes teruggeven.
        return (result.MessageType, null);
    }

    /// <summary>
    /// Disposes the RconClient and releases all resources including WebSocket, cancellation tokens, and tasks.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected implementation of Dispose pattern.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Cancel all running tasks
            _ctsReceiver?.Cancel();
            _ctsTimeoutChecker?.Cancel();

            // Wait briefly for tasks to finish (best effort)
            try
            {
                Task.WaitAll(new[] { _receiverTask, _timeoutCheckerTask }.Where(t => t != null).ToArray(),
                    TimeSpan.FromMilliseconds(500));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error waiting for tasks to complete during disposal: {ex.Message}");
            }

            // Dispose cancellation token sources
            _ctsReceiver?.Dispose();
            _ctsTimeoutChecker?.Dispose();

            // Dispose WebSocket
            socket?.Dispose();

            // Clear event handlers to prevent memory leaks
            OnMessageReceived = null;
            OnCommandAnswerReceived = null;
            OnConnectionClosed = null;
            OnChatMessageReceived = null;
            OnPlayerKill = null;
            OnPlayerConnected = null;
            OnPlayerDisconnected = null;

            // Clear pending commands
            PendingCommands?.Clear();
        }

        _disposed = true;
    }

}