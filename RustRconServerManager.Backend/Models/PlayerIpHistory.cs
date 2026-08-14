using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RustRconServerManager.Backend.Models
{
    [Table("PlayerIpHistory")]
    public class PlayerIpHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(17)]
        public string SteamId { get; set; } = string.Empty;

        [Required]
        public int ServerId { get; set; }

        [Required]
        [MaxLength(45)]
        public string IpAddress { get; set; } = string.Empty;

        public bool IsVpn { get; set; }

        [MaxLength(100)]
        public string? Country { get; set; }

        public DateTime FirstUsed { get; set; } = DateTime.UtcNow;

        public DateTime LastUsed { get; set; } = DateTime.UtcNow;

        public int TimesUsed { get; set; } = 1;
    }
}
