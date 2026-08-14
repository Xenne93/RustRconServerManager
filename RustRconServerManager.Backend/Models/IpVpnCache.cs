using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RustRconServerManager.Backend.Models
{
    [Table("IpVpnCache")]
    public class IpVpnCache
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(45)]
        public string IpAddress { get; set; } = string.Empty;

        public bool IsVpn { get; set; }

        [MaxLength(50)]
        public string? ProxyType { get; set; }

        [MaxLength(100)]
        public string? Provider { get; set; }

        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }
    }
}
