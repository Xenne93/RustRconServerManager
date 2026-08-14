using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Services;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;
using Xenne.RCON;

namespace RustRconServerManager.Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public partial class RconController : ControllerBase
    {
        public class CommandRequest
        {
            public string Command { get; set; } = "";
        }
        
        private readonly IRconBackgroundService _rconBackgroundService;
        private readonly IRconPasswordsCryptoService _rconPasswordsCryptoService;
        private readonly ILogger<RconController> _logger;
        private readonly AppDbContext _dbContext;
        private readonly IServiceScopeFactory _scopeFactory;
                

        public RconController(IRconBackgroundService rconBackgroundService, IRconPasswordsCryptoService rconPasswordsCryptoService, ILogger<RconController> logger, AppDbContext dbContext, IServiceScopeFactory scopeFactory)
        {
            _rconBackgroundService = rconBackgroundService ?? throw new ArgumentNullException(nameof(rconBackgroundService));
            _rconPasswordsCryptoService = rconPasswordsCryptoService ?? throw new ArgumentNullException(nameof(rconPasswordsCryptoService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }



        /// <summary>
        /// Check if the selected server is connected
        /// </summary>
        [HttpGet("CheckServerConnection")]
        public async Task<IActionResult> CheckServerConnection()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                ApplicationUser user = await User.GetUser(dbContext);
                int serverId = user.SelectedServerId ?? 0;

                if (serverId <= 0)
                {
                    return Ok(new { isConnected = false, serverId = 0, message = "No server selected" });
                }

                bool isConnected = await _rconBackgroundService.IsServerConnected(serverId);

                return Ok(new { isConnected, serverId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking server connection");
                return Ok(new { isConnected = false, serverId = 0, message = "Error checking connection" });
            }
        }


        // This endpoint is used to get the RCON servers from the database

        // Post endpoint for sending command
        [HttpPost("SendCustomCommand")]
        public async Task<IActionResult> SendCommand([FromBody] CommandRequest command)
        {
            
            
            if (string.IsNullOrEmpty(command.Command))
            {
                return BadRequest("Command cannot be empty!");
            }
            
            using var scope = _scopeFactory.CreateScope();
            AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            ApplicationUser user = await User.GetUser(dbContext);
            SystemProfile profile = await User.GetUserSystemProfile(dbContext);

            if (profile.Id != user.SystemProfileId)
            {
                return Unauthorized("Mismatch in systemprofile");
            }

            int serverId = user.SelectedServerId ?? 0;

            if (serverId <= 0)
            {
                return BadRequest("No ServerID given.");
            }
            
            // Check if the user has access to that server
            bool access = await User.HasServerAccess(dbContext, serverId);
            if (!access)
            {
                return Unauthorized("User does not have access to this server.");
            }
            

            _rconBackgroundService.SendRconCommand(command.Command, serverId);

            

            return Ok();

        }
        
        


    }
}
