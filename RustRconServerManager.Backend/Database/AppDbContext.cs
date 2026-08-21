using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Models;

namespace RustRconServerManager.Backend.Database
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {

        public AppDbContext(DbContextOptions<AppDbContext> options):base(options)
        {

        }

        
        // Database set for RCON servers
        public DbSet<RconServer> RconServers { get; set; }

        // Database set for admin/moderator audit log entries
        public DbSet<AuditLog> AuditLogs { get; set; }
        
        // Database set for the SystemProfiles
        public DbSet<SystemProfile> SystemProfiles { get; set; }
        
        // Database set for SteamPlayers
        public DbSet<SteamPlayer> SteamPlayers { get; set; }
        
        
        // Database set for Player Kill Logs
        public DbSet<PlayerKillLog> PlayerKillLogs { get; set; }
        
        // Database set for ChatMessages
        public DbSet<ChatMessage> ChatMessages { get; set; }
        
        // Database set for Rcon Log Entries (log history)
        public DbSet<RconLogEntry> RconLogEntries { get; set; }
        
        // Database set for stats histories (server performance, fps, memory, entity count, etc)
        public DbSet<StatsHistory> StatsHistories { get; set; }

        // Database set for scheduled commands
        public DbSet<ScheduledCommand> ScheduledCommands { get; set; }

        // Database set for triggers (event-based command execution)
        public DbSet<Trigger> Triggers { get; set; }

        // Database set for panel settings
        public DbSet<PanelSettings> PanelSettings { get; set; }

        // Database set for AI provider configuration (Ollama/LM Studio/OpenAI/Anthropic/Universal)
        public DbSet<AiIntegrationSettings> AiIntegrationSettings { get; set; }

        // Database set for user sessions (multi-session support)
        public DbSet<UserSession> UserSessions { get; set; }

        // Database set for player bans (server bans from Rust)
        public DbSet<PlayerBan> PlayerBans { get; set; }

        // Database set for player ban history (historical record of all bans)
        public DbSet<PlayerBanHistory> PlayerBanHistories { get; set; }

        // Database set for player reports (in-game player reports)
        public DbSet<PlayerReport> PlayerReports { get; set; }

        // Database set for player notes (staff notes about players)
        public DbSet<PlayerNote> PlayerNotes { get; set; }

        // Database set for moderator server permissions
        public DbSet<ModeratorServerPermission> ModeratorServerPermissions { get; set; }

        // Database set for moderator page permissions
        public DbSet<ModeratorPagePermission> ModeratorPagePermissions { get; set; }

        // Database set for server protection settings
        public DbSet<ServerProtectionSettings> ServerProtectionSettings { get; set; }

        // Database set for server webhook settings
        public DbSet<ServerWebhookSettings> ServerWebhookSettings { get; set; }

        // Database set for plugin version cache (30-minute cache to reduce API calls)
        public DbSet<PluginVersionCache> PluginVersionCache { get; set; }

        // Database set for server-specific plugin source configuration (Umod/Codefling/Custom)
        public DbSet<ServerPluginSource> ServerPluginSources { get; set; }

        // Database set for Rust items (for give item functionality)
        public DbSet<RustItem> RustItems { get; set; }

        // Database set for aggregated statistics (hourly/daily summaries for efficient graphing)
        public DbSet<AggregatedStat> AggregatedStats { get; set; }

        // Database set for legal consent records (T&C, Privacy Policy, Telemetry consent)
        public DbSet<LegalConsent> LegalConsents { get; set; }

        // Database set for map data (server map images)
        public DbSet<MapData> MapData { get; set; }

        // Database set for sleeping bag/bed locations
        public DbSet<SleepingBagData> SleepingBags { get; set; }

        // Database set for tool cupboard locations
        public DbSet<ToolCupboardData> ToolCupboards { get; set; }

        // Database set for preset commands (quick-access RCON commands)
        public DbSet<PresetCommand> PresetCommands { get; set; }

        // Database set for device push tokens (FCM tokens for push notifications)
        public DbSet<DevicePushToken> DevicePushTokens { get; set; }

        // Database set for user notification preferences (per-server push notification settings)
        public DbSet<UserNotificationPreference> UserNotificationPreferences { get; set; }

        // Database set for VPN/proxy IP check cache (proxycheck.io results cached for 1 day)
        public DbSet<IpVpnCache> IpVpnCaches { get; set; }

        // Database set for player IP history (all IPs a player has connected with)
        public DbSet<PlayerIpHistory> PlayerIpHistories { get; set; }

        // Database set for developer-mode fake VAC-ban profiles (testing VAC protection rules)
        public DbSet<DeveloperVacBanOverride> DeveloperVacBanOverrides { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure PanelSettings relationship with SystemProfile
            modelBuilder.Entity<PanelSettings>()
                .HasOne(ps => ps.SystemProfile)
                .WithOne(sp => sp.PanelSettings)
                .HasForeignKey<PanelSettings>(ps => ps.SystemProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure UserSession relationship with ApplicationUser
            modelBuilder.Entity<UserSession>()
                .HasOne(us => us.User)
                .WithMany(u => u.Sessions)
                .HasForeignKey(us => us.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for querying active sessions
            modelBuilder.Entity<UserSession>()
                .HasIndex(us => new { us.UserId, us.IsRevoked });

            // Configure ModeratorServerPermission relationships
            modelBuilder.Entity<ModeratorServerPermission>()
                .HasOne(msp => msp.User)
                .WithMany(u => u.ServerPermissions)
                .HasForeignKey(msp => msp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ModeratorServerPermission>()
                .HasOne(msp => msp.RconServer)
                .WithMany(r => r.ModeratorPermissions)
                .HasForeignKey(msp => msp.RconServerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for querying moderator permissions by server (unique constraint)
            modelBuilder.Entity<ModeratorServerPermission>()
                .HasIndex(msp => new { msp.UserId, msp.RconServerId })
                .IsUnique();

            // Configure ModeratorPagePermission relationships
            modelBuilder.Entity<ModeratorPagePermission>()
                .HasOne(mpp => mpp.User)
                .WithMany(u => u.PagePermissions)
                .HasForeignKey(mpp => mpp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for querying moderator page permissions (unique constraint: one permission per user per page)
            modelBuilder.Entity<ModeratorPagePermission>()
                .HasIndex(mpp => new { mpp.UserId, mpp.PageRoute })
                .IsUnique();

            // Index for querying plugin version cache by plugin name
            modelBuilder.Entity<PluginVersionCache>()
                .HasIndex(pvc => pvc.PluginName);

            // Index for querying expired cache entries
            modelBuilder.Entity<PluginVersionCache>()
                .HasIndex(pvc => pvc.ExpiresAt);

            // Index for querying aggregated stats by server, stat type, and timestamp
            modelBuilder.Entity<AggregatedStat>()
                .HasIndex(a => new { a.ServerId, a.Stat, a.Timestamp, a.AggregationType });

            // Index for querying/aggregating raw stats history by server, stat type, and time range.
            // This is the highest-write-volume table in the app (one row per server per heavy tick
            // per stat) and previously had no index besides the PK, so every read/aggregation/cleanup
            // query against it was a full table scan.
            modelBuilder.Entity<StatsHistory>()
                .HasIndex(s => new { s.ServerId, s.Stat, s.CreatedAt });

            // Unique index for server plugin sources (one source config per plugin per server)
            modelBuilder.Entity<ServerPluginSource>()
                .HasIndex(sps => new { sps.RustServerId, sps.PluginName })
                .IsUnique();

            // Configure MapData relationship with RconServer (one map per server)
            modelBuilder.Entity<MapData>()
                .HasOne(md => md.Server)
                .WithOne()
                .HasForeignKey<MapData>(md => md.ServerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique index for MapData (one map per server)
            modelBuilder.Entity<MapData>()
                .HasIndex(md => md.ServerId)
                .IsUnique();

            // Configure PresetCommand optional relationship with RconServer
            modelBuilder.Entity<PresetCommand>()
                .HasOne(pc => pc.RconServer)
                .WithMany()
                .HasForeignKey(pc => pc.RconServerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for querying preset commands by server
            modelBuilder.Entity<PresetCommand>()
                .HasIndex(pc => pc.RconServerId);

            // Configure DevicePushToken relationships
            modelBuilder.Entity<DevicePushToken>()
                .HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique index on Token to prevent duplicates
            modelBuilder.Entity<DevicePushToken>()
                .HasIndex(d => d.Token)
                .IsUnique();

            // Index for querying tokens by user
            modelBuilder.Entity<DevicePushToken>()
                .HasIndex(d => d.UserId);

            // Configure UserNotificationPreference relationships
            modelBuilder.Entity<UserNotificationPreference>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserNotificationPreference>()
                .HasOne(n => n.Server)
                .WithMany()
                .HasForeignKey(n => n.ServerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique index: one preference per user per server
            modelBuilder.Entity<UserNotificationPreference>()
                .HasIndex(n => new { n.UserId, n.ServerId })
                .IsUnique();

            // Unique index on IP address for VPN cache lookups
            modelBuilder.Entity<IpVpnCache>()
                .HasIndex(i => i.IpAddress)
                .IsUnique();

            // Index for cache expiration cleanup
            modelBuilder.Entity<IpVpnCache>()
                .HasIndex(i => i.ExpiresAt);

            // Unique index for player IP history (one entry per player per server per IP)
            modelBuilder.Entity<PlayerIpHistory>()
                .HasIndex(h => new { h.SteamId, h.ServerId, h.IpAddress })
                .IsUnique();

            // Index for querying IP history by player
            modelBuilder.Entity<PlayerIpHistory>()
                .HasIndex(h => new { h.SteamId, h.ServerId });

            // Unique index for developer-mode VAC-ban overrides (one profile per SteamID)
            modelBuilder.Entity<DeveloperVacBanOverride>()
                .HasIndex(o => o.SteamId)
                .IsUnique();

        }
    }
}
