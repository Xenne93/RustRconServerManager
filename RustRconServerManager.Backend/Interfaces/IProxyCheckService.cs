namespace RustRconServerManager.Backend.Interfaces;

public class VpnCheckResult
{
    public bool IsVpn { get; set; }
    public string? ProxyType { get; set; }
    public string? Provider { get; set; }
}

public interface IProxyCheckService
{
    Task<VpnCheckResult> CheckIpAsync(string ipAddress);
}
