using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;
using SkiaSharp;

namespace RustRconServerManager.Backend.Services;

/// <summary>
/// Requests map/sleeping-bag/tool-cupboard data from a Rust server's RustRconServerManager
/// Oxide plugin over the RCON connection the panel already maintains, and stores it.
///
/// The plugin replies to the rrsm.send* commands directly with the data (as JSON, prefixed
/// "200-") instead of making an outbound HTTP call back to the panel. That means the panel
/// never needs to be reachable from the game server's network - only the existing, already
/// required direction (panel -> game server RCON) is used.
/// </summary>
public class RrsmModDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IRconBackgroundService _rconService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MapStorageService _mapStorageService;
    private readonly ILogger<RrsmModDataService> _logger;

    public RrsmModDataService(
        IRconBackgroundService rconService,
        IServiceScopeFactory scopeFactory,
        MapStorageService mapStorageService,
        ILogger<RrsmModDataService> logger)
    {
        _rconService = rconService;
        _scopeFactory = scopeFactory;
        _mapStorageService = mapStorageService;
        _logger = logger;
    }

    private static bool TryUnwrap(string? response, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrEmpty(response) || !response.StartsWith("200-"))
            return false;

        json = response.Substring(4);
        return true;
    }

    /// <param name="scaleArg">Optional map render scale override (same argument rrsm.sendmap
    /// accepts in-game), useful if the default resolution produces too large a payload to
    /// send over RCON for a given world size.</param>
    public async Task<(bool Success, string? Error)> RequestAndStoreMapDataAsync(int serverId, string? scaleArg = null)
    {
        var command = string.IsNullOrWhiteSpace(scaleArg) ? "rrsm.sendmap" : $"rrsm.sendmap {scaleArg}";
        string? response = await _rconService.ExecuteCommandWithResponse(command, serverId, 30000);

        if (!TryUnwrap(response, out var json))
        {
            _logger.LogWarning("rrsm.sendmap did not return usable data for server {ServerId}. Response: {Response}",
                serverId, response ?? "null");
            return (false, "Failed to get map data from the server. It may be offline, the mod may not be " +
                           "installed, or the rendered map may be too large to fit in a single RCON response " +
                           "(try again with a lower scale, e.g. rrsm.sendmap 1.0).");
        }

        MapDataPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MapDataPayload>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse map data payload for server {ServerId}", serverId);
            return (false, "Received an invalid map data response from the server.");
        }

        if (payload == null || string.IsNullOrEmpty(payload.Base64Image))
            return (false, "Server did not return any map image data.");

        byte[] jpgImageData;
        try
        {
            jpgImageData = ConvertToJpgBytes(payload.Base64Image);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process map image for server {ServerId}", serverId);
            return (false, "Failed to process the map image.");
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var mapData = await dbContext.MapData.FirstOrDefaultAsync(m => m.ServerId == serverId);
        if (mapData == null)
        {
            mapData = new MapData
            {
                ServerId = serverId,
                ImageData = jpgImageData,
                MapSize = payload.MapSize,
                MapSeed = payload.MapSeed,
                ImageWidth = payload.ImageWidth,
                ImageHeight = payload.ImageHeight,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            dbContext.MapData.Add(mapData);
        }
        else
        {
            mapData.ImageData = jpgImageData;
            mapData.MapSize = payload.MapSize;
            mapData.MapSeed = payload.MapSeed;
            mapData.ImageWidth = payload.ImageWidth;
            mapData.ImageHeight = payload.ImageHeight;
            mapData.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync();
        await _mapStorageService.SaveMapToDisk(serverId, jpgImageData);

        _logger.LogInformation(
            "Map image saved for server {ServerId}. Size: {ImageSize} bytes, MapSize: {MapSize}, Seed: {Seed}",
            serverId, jpgImageData.Length, payload.MapSize, payload.MapSeed);

        return (true, null);
    }

    public async Task<(bool Success, string? Error, int Count)> RequestAndStoreSleepingBagsAsync(int serverId)
    {
        string? response = await _rconService.ExecuteCommandWithResponse("rrsm.sendsleepingbags", serverId, 15000);

        if (!TryUnwrap(response, out var json))
            return (false, "Failed to get sleeping bag data from the server.", 0);

        SleepingBagsPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SleepingBagsPayload>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse sleeping bags payload for server {ServerId}", serverId);
            return (false, "Received an invalid response from the server.", 0);
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = dbContext.SleepingBags.Where(s => s.ServerId == serverId);
        dbContext.SleepingBags.RemoveRange(existing);

        var now = DateTime.UtcNow;
        var newBags = (payload?.SleepingBags ?? new List<SleepingBagEntry>()).Select(b => new SleepingBagData
        {
            ServerId = serverId,
            OwnerSteamId = b.OwnerId ?? "",
            OwnerName = b.OwnerName ?? "Unknown",
            Name = b.Name ?? "",
            PositionX = b.PositionX,
            PositionY = b.PositionY,
            PositionZ = b.PositionZ,
            Type = b.Type ?? "sleepingbag",
            UpdatedAt = now
        }).ToList();

        dbContext.SleepingBags.AddRange(newBags);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation("Received {Count} sleeping bags/beds for server {ServerId}", newBags.Count, serverId);
        return (true, null, newBags.Count);
    }

    public async Task<(bool Success, string? Error, int Count)> RequestAndStoreToolCupboardsAsync(int serverId)
    {
        string? response = await _rconService.ExecuteCommandWithResponse("rrsm.sendtoolcupboards", serverId, 15000);

        if (!TryUnwrap(response, out var json))
            return (false, "Failed to get tool cupboard data from the server.", 0);

        ToolCupboardsPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ToolCupboardsPayload>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse tool cupboards payload for server {ServerId}", serverId);
            return (false, "Received an invalid response from the server.", 0);
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = dbContext.ToolCupboards.Where(t => t.ServerId == serverId);
        dbContext.ToolCupboards.RemoveRange(existing);

        var now = DateTime.UtcNow;
        var newTCs = (payload?.ToolCupboards ?? new List<ToolCupboardEntry>()).Select(tc => new ToolCupboardData
        {
            ServerId = serverId,
            OwnerSteamId = tc.OwnerId ?? "",
            OwnerName = tc.OwnerName ?? "Unknown",
            PositionX = tc.PositionX,
            PositionY = tc.PositionY,
            PositionZ = tc.PositionZ,
            AuthorizedPlayers = tc.AuthorizedPlayers != null ? string.Join(", ", tc.AuthorizedPlayers) : "",
            UpdatedAt = now
        }).ToList();

        dbContext.ToolCupboards.AddRange(newTCs);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation("Received {Count} tool cupboards for server {ServerId}", newTCs.Count, serverId);
        return (true, null, newTCs.Count);
    }

    private static byte[] ConvertToJpgBytes(string base64Image)
    {
        var imageBytes = Convert.FromBase64String(base64Image);

        using var inputStream = new MemoryStream(imageBytes);
        using var originalBitmap = SKBitmap.Decode(inputStream);

        if (originalBitmap == null)
            throw new InvalidOperationException("Failed to decode image data.");

        using var image = SKImage.FromBitmap(originalBitmap);
        using var jpgData = image.Encode(SKEncodedImageFormat.Jpeg, 85); // 85% quality

        return jpgData.ToArray();
    }

    private class MapDataPayload
    {
        public string? Base64Image { get; set; }
        public int? MapSize { get; set; }
        public int? MapSeed { get; set; }
        public int? ImageWidth { get; set; }
        public int? ImageHeight { get; set; }
    }

    private class SleepingBagsPayload
    {
        public List<SleepingBagEntry>? SleepingBags { get; set; }
    }

    private class ToolCupboardsPayload
    {
        public List<ToolCupboardEntry>? ToolCupboards { get; set; }
    }

    private class SleepingBagEntry
    {
        public string? OwnerId { get; set; }
        public string? OwnerName { get; set; }
        public string? Name { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public string? Type { get; set; }
    }

    private class ToolCupboardEntry
    {
        public string? OwnerId { get; set; }
        public string? OwnerName { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public List<string>? AuthorizedPlayers { get; set; }
    }
}
