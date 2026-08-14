using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Services;
using RustRconServerManager.Shared.Account;
using RustRconServerManager.Shared.ManageServers;


namespace RustRconServerManager.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ManageServersController : ControllerBase
    {
        
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ManageServersController> _logger;
        private readonly RconBackgroundService _rconBackgroundService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IRconPasswordsCryptoService _rconPasswordsCryptoService;

        public ManageServersController(AppDbContext dbContext, ILogger<ManageServersController> logger, RconBackgroundService rconBackgroundService, IServiceScopeFactory scopeFactory, IRconPasswordsCryptoService rconPasswordsCryptoService) : base()
        {
            _dbContext = dbContext;
            _logger = logger;
            _rconBackgroundService = rconBackgroundService;
            _scopeFactory = scopeFactory;
            _rconPasswordsCryptoService = rconPasswordsCryptoService;
        }

        // Gets the account information
        [HttpGet("GetServerAccountInformation")]
        public async Task<IActionResult> GetServerAccountInformation()
        {
            SystemProfile profile = await User.GetUserSystemProfile(_dbContext);
            int ServerCount = await _dbContext.RconServers.CountAsync(x => x.SystemProfileId == profile.Id);

            RustRconServerManager.Shared.ManageServers.ManageServers_AccountInformationDTO manageServersAccountInformation = new RustRconServerManager.Shared.ManageServers.ManageServers_AccountInformationDTO();
            manageServersAccountInformation.CurrentServers = ServerCount;
            manageServersAccountInformation.ProfileName = profile.Name;
            manageServersAccountInformation.ProfileId = profile.Id.ToString();

            return Ok(manageServersAccountInformation);
        }

        // Gets the serverlist
        [HttpGet("GetServerList")]
        public async Task<IActionResult> GetServers()
        {
            var currentUser = await User.GetUser(_dbContext);
            SystemProfile sysProfile = await User.GetUserSystemProfile(_dbContext);

            List<RconServer> servers;

            // If user is a moderator, only return servers they have permission to access
            if (currentUser.IsModerator)
            {
                servers = await _dbContext.RconServers
                    .Where(x => x.SystemProfileId == sysProfile.Id &&
                                x.ModeratorPermissions.Any(mp => mp.UserId == currentUser.Id))
                    .ToListAsync();
            }
            else
            {
                // Admins and regular users see all servers
                servers = await _dbContext.RconServers
                    .Where(x => x.SystemProfileId == sysProfile.Id)
                    .ToListAsync();
            }
            
            
            List<RustRconServerManager.Shared.ManageServers.ManageServers_ServerListEntryDTO> serverListDto = new List<RustRconServerManager.Shared.ManageServers.ManageServers_ServerListEntryDTO>();

            foreach (var server in servers)
            {
                RustRconServerManager.Shared.ManageServers.ManageServers_ServerListEntryDTO manageServersServerListEntry = new RustRconServerManager.Shared.ManageServers.ManageServers_ServerListEntryDTO();
                manageServersServerListEntry.ServerName = server.Name;
                manageServersServerListEntry.ServerAddress = !string.IsNullOrEmpty(server.EncryptedHost)
                    ? _rconPasswordsCryptoService.Decrypt(server.EncryptedHost)
                    : string.Empty;
                manageServersServerListEntry.RconPort = server.RconPort;
                manageServersServerListEntry.QueryPort = server.QueryPort;
                manageServersServerListEntry.GamePort = server.GamePort;
                manageServersServerListEntry.ServerId = server.Id;
                manageServersServerListEntry.ModFramework = server.ModFramework;
                manageServersServerListEntry.RustRconServerManagerModInstalled = server.RustRconServerManagerModInstalled;
                manageServersServerListEntry.RrsmModInitialized = server.RrsmModInitialized;

                bool isServerConnected = false;

                try
                {
                     isServerConnected = await _rconBackgroundService.IsServerConnected(server.Id);
                }
                catch
                {
                    _logger.LogWarning("Server with ID: " + server.Id + " is not connected.");
                }
                manageServersServerListEntry.IsConnected = isServerConnected;

                serverListDto.Add(manageServersServerListEntry);
            }
            
            return Ok(serverListDto);

        }

        
        // Sets the users SelectedServerId, the server that the user
        // is managing in RustRconServerManager.
        [HttpPut("UpdateSelectedServer/{serverId}")]
        [Authorize]
        public async Task<IActionResult> UpdateSelectedServer(int serverId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            bool userHasAccess = await User.HasServerAccess(dbContext, serverId);

            if (!userHasAccess)
                return Unauthorized("User does not have access to this server.");

            var user = await User.GetUser(dbContext);

            if (user == null)
                return NotFound("User not found.");

            user.SelectedServerId = serverId;

            // Save changes and ensure the transaction is fully committed
            var saveResult = await dbContext.SaveChangesAsync();

            _logger.LogInformation("Updated SelectedServerId to {ServerId} for user {Email}. Rows affected: {RowsAffected}",
                serverId, user.Email, saveResult);

            return Ok(new { message = "Server selection updated successfully", serverId = serverId });
        }
        
        
        
        

      
    }
}