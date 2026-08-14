using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RustRconServerManager.Backend.Models
{
    
    // RconServer model is used to store connectiondata of the server to the users
    // account. RconServer will only be accessable by a user that is part of the Environment by
    // passing the EnvironmentId and EnvironmentSecret.
    public class RconServer
    {
        // Unique Server ID
        public int Id { get; set; }
        public string Name { get; set; }
        public string ServerHostname { get; set; }

        /// <summary>
        /// Encrypted host/IP address of the server
        /// </summary>
        public string? EncryptedHost { get; set; }

        public int? GamePort { get; set; }
        public int? QueryPort { get; set; }
        public int RconPort { get; set; }
        
        public string? Description { get; set; }
        
        // Not yet implemented. EnvironmentId and EnvironmentSecret are
        // parameters that authorize the access to this server.
        public string? EncryptedPassword { get; set; }
        
        public int SystemProfileId { get; set; }
        public SystemProfile SystemProfile { get; set; }
        
        // Secret is the secret of the SystemProfile
        public string? EnvironmentSecret { get; set; }
        
        public int? LatestFpsCount { get; set; }
        public int? LatestPlayerCount { get; set; }
        public int? LatestEntityCount { get; set; }
        public int? LatestUptime { get; set; }
        public int? LatestQueuedPlayers { get; set; }
        public int? LatestJoiningPlayers { get; set; }
        public string? LatestMap { get; set; }
        public int? LatestMemoryUsage { get; set; }
        public int? LatestServerVersion { get; set; }
        public string? LatestServerProtocol { get; set; }

        /// <summary>
        /// Modding framework running on the server (Oxide, Carbon, or None)
        /// </summary>
        public string ModFramework { get; set; } = "None";

        /// <summary>
        /// Indicates if the RustRconServerManager mod is installed on the server
        /// </summary>
        public bool RustRconServerManagerModInstalled { get; set; } = false;

        /// <summary>
        /// Indicates if the RustRconServerManager mod has been confirmed active on the server
        /// (responds to rrsm.hello over RCON). No API key/base URL/identification is needed -
        /// the panel pulls data from the mod over the same RCON connection on demand.
        /// </summary>
        public bool RrsmModInitialized { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeen { get; set; }

        /// <summary>
        /// Moderator permissions for this server
        /// </summary>
        public ICollection<ModeratorServerPermission> ModeratorPermissions { get; set; } = new List<ModeratorServerPermission>();

        /// <summary>
        /// Custom serverlist header image data (stored in database for backup)
        /// </summary>
        public byte[]? ServerHeaderImageData { get; set; }

        /// <summary>
        /// Custom Rust+ logo image data (stored in database for backup)
        /// </summary>
        public byte[]? ServerLogoImageData { get; set; }
    }

    // Insert test data on migration



}
