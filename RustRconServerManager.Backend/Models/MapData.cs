namespace RustRconServerManager.Backend.Models
{
    /// <summary>
    /// Stores the map image for a Rust server directly in the database.
    /// This ensures the map survives instance updates and disaster recovery.
    /// </summary>
    public class MapData
    {
        public int Id { get; set; }

        /// <summary>
        /// The server this map belongs to
        /// </summary>
        public int ServerId { get; set; }
        public RconServer Server { get; set; } = null!;

        /// <summary>
        /// The map image stored as JPG bytes directly in the database
        /// </summary>
        public byte[] ImageData { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Legacy: Relative path to the JPG file (kept for migration, will be removed)
        /// </summary>
        [Obsolete("Use ImageData instead. This property is kept for migration purposes.")]
        public string? ImagePath { get; set; }

        /// <summary>
        /// Map size (e.g., 4000 for a 4k map)
        /// </summary>
        public int? MapSize { get; set; }

        /// <summary>
        /// Map seed
        /// </summary>
        public int? MapSeed { get; set; }

        /// <summary>
        /// Rendered image width in pixels
        /// </summary>
        public int? ImageWidth { get; set; }

        /// <summary>
        /// Rendered image height in pixels
        /// </summary>
        public int? ImageHeight { get; set; }

        /// <summary>
        /// When the map data was last updated
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the map data was first created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
