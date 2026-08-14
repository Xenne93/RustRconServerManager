namespace RustRconServerManager.Shared.Configuration
{
    public class ServerConfiguration
    {
        public static string DefaultServerName { get; set; } = "RustRconServerManager Server";
        public static string DefaultServerDescription { get; set; } = "A Rust server managed by RustRconServerManager.";
        public static string BackendUrl = "";
        public static string FrontendUrl = "";

    }
}
