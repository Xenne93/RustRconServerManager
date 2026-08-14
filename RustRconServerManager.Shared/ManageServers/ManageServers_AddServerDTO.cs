namespace RustRconServerManager.Shared.ManageServers;

public class ManageServers_AddServerDTO
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int GamePort { get; set; }
    public int QueryPort { get; set; }
    public int RconPort { get; set; }
    public string Address { get; set; }
}