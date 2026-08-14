using System.Security.Claims;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Services;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Helpers;
using RustRconServerManager.Shared.Rcon;
using Xenne.RCON;

namespace RustRconServerManager.Backend.Controllers
{
    
    // The ServerManagement Partial Class extends the RconController class
    // and contains functionality which is used to add new servers to the panel.
    public partial class RconController : ControllerBase
    {

        [HttpDelete("DeleteServer/{serverId}")]
        [Authorize]
        public async Task<IActionResult> DeleteServer(int serverId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            SystemProfile profile = await User.GetUserSystemProfile(dbContext);
            ApplicationUser user = await User.GetUser(dbContext);

            if (serverId <= 0)
            {
                return BadRequest("Invalid server ID.");
            }

            if (profile.Id != user.SystemProfileId)
            {
                return Unauthorized("Mismatch in systemprofile");
            }


            try
            {
                RconServer server = await dbContext.RconServers.SingleOrDefaultAsync(s =>
                    s.Id == serverId && s.SystemProfileId == user.SystemProfileId);
                
                if (server.SystemProfileId != user.SystemProfileId)
                {
                    return Unauthorized("Server does not match users systemprofile.");
                }
                else
                {
                    dbContext.RconServers.Remove(server);
                    await dbContext.SaveChangesAsync();
                    await _rconBackgroundService.DisconnectServerAsync(server.Id);
                    await _rconBackgroundService.HandleDeleteServer(server);
                    return Ok("Server successfully deleted");
                }
               
                    
                


            }
            catch (Exception e)
            {
                return BadRequest("Server not found or database query error.");
            }
        }

        [HttpPost("TestRconConnection")]
        [Authorize]
        public async Task<IActionResult> TestRconConnection([FromBody] Rcon_RconServerDTO dto)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Check if password has been set
            if (string.IsNullOrEmpty(dto.Password))
            {
                return BadRequest("Invalid password or password is empty.");
            }

            // Check if server address has been set
            if (string.IsNullOrEmpty(dto.ServerAddress))
            {
                return BadRequest("Invalid server address or server address is empty.");
            }

            // Check if the rcon port is within valid range
            if (dto.RconPort <= 0 || dto.RconPort > 65535)
            {
                return BadRequest("Invalid RCON port. Port must be between 1 and 65535.");
            }
            
            
            // Get the user and users system profile
            var profile = await User.GetUserSystemProfile(dbContext);

            // Generate random ID to test the Rcon connection
            Random random = new Random();
            int randomServerId = random.Next(999100, 2500000);

            RconClient client = new RconClient(dto.ServerAddress, dto.RconPort, dto.Password, randomServerId);

            bool isConnectionSuccess = await client.TestConnection();

            // Check if the connection was successful

            
            if (isConnectionSuccess)
            {   // Get the users system profile


                // Make RconServer
                RconServer rconServer = new RconServer();
                rconServer.Name = dto.ServerName;
                rconServer.RconPort = dto.RconPort;
                rconServer.EncryptedHost = _rconPasswordsCryptoService.Encrypt(dto.ServerAddress);
                rconServer.SystemProfile = profile;
                rconServer.EnvironmentSecret = profile.Secret;
                rconServer.GamePort = 0;
                rconServer.QueryPort = 0;
                rconServer.ServerHostname = "Freshly added server...";
                rconServer.ModFramework = dto.ModFramework ?? "None";
                rconServer.RustRconServerManagerModInstalled = dto.RustRconServerManagerModInstalled;

                string encryptedPassword = _rconPasswordsCryptoService.Encrypt(dto.Password);
                rconServer.EncryptedPassword = encryptedPassword;

                dbContext.RconServers.Add(rconServer);
                await dbContext.SaveChangesAsync();
                await _rconBackgroundService.HandleNewServer(rconServer);
               
                return Ok("Connection successful.");
            }
            else{
                return BadRequest("Connection failed.");
            }
        }

        [HttpPut("UpdateServer")]
        [Authorize]
        public async Task<IActionResult> UpdateServer([FromBody] Rcon_UpdateServerDTO dto)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                ApplicationUser user = await User.GetUser(dbContext);
                SystemProfile profile = await User.GetUserSystemProfile(dbContext);

                if (profile.Id != user.SystemProfileId)
                {
                    return Unauthorized("System profile mismatch");
                }

                // Get the server
                var server = await dbContext.RconServers
                    .FirstOrDefaultAsync(s => s.Id == dto.ServerId && s.SystemProfileId == profile.Id);

                if (server == null)
                {
                    return NotFound("Server not found or you don't have access to it");
                }

                // Update server details
                server.Name = dto.ServerName;
                server.EncryptedHost = _rconPasswordsCryptoService.Encrypt(dto.ServerAddress);
                server.RconPort = dto.RconPort;
                server.ModFramework = dto.ModFramework ?? "None";
                server.RustRconServerManagerModInstalled = dto.RustRconServerManagerModInstalled;

                // Update password only if provided
                if (!string.IsNullOrWhiteSpace(dto.Password))
                {
                    string encryptedPassword = _rconPasswordsCryptoService.Encrypt(dto.Password);
                    server.EncryptedPassword = encryptedPassword;
                }

                await dbContext.SaveChangesAsync();

                // Reconnect to server with new details if password was changed
                if (!string.IsNullOrWhiteSpace(dto.Password))
                {
                    await _rconBackgroundService.DisconnectServerAsync(server.Id);
                    await _rconBackgroundService.HandleNewServer(server);
                }

                return Ok("Server updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating server");
                return StatusCode(500, ApiErrorHelper.FormatError("Error updating server", ex));
            }
        }

        /// <summary>
        /// Confirms the panel mod is installed and responding on this server over RCON, and
        /// marks it as initialized. No further setup is needed - map/sleeping-bag/tool-cupboard
        /// data is pulled from the server over the same RCON connection whenever requested, so
        /// there's no base URL, API key, or identification for the mod to be given.
        /// </summary>
        [HttpPost("InitializeRrsmMod/{serverId}/hello")]
        [Authorize]
        public async Task<IActionResult> InitializeRrsmModHello(int serverId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                bool hasAccess = await User.HasServerAccess(dbContext, serverId);
                if (!hasAccess)
                    return Unauthorized("You don't have access to this server.");

                bool isConnected = await _rconBackgroundService.IsServerConnected(serverId);
                if (!isConnected)
                    return BadRequest("Server is not connected.");

                string? response = await _rconBackgroundService.ExecuteCommandWithResponse("rrsm.hello", serverId, 10000);

                var server = await dbContext.RconServers.FirstOrDefaultAsync(s => s.Id == serverId);

                if (response == null || !response.Contains("200"))
                {
                    if (server != null)
                    {
                        server.RrsmModInitialized = false;
                        await dbContext.SaveChangesAsync();
                    }

                    _logger.LogWarning("rrsm.hello failed for server {ServerId}. Response: {Response}", serverId, response ?? "null");
                    return BadRequest("The panel mod is not responding. Make sure it's installed and loaded on the server.");
                }

                if (server != null)
                {
                    server.RrsmModInitialized = true;
                    await dbContext.SaveChangesAsync();
                }

                _logger.LogInformation("Panel mod initialized for server {ServerId}", serverId);

                return Ok(new { Success = true, Response = response });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing rrsm.hello for server {ServerId}", serverId);
                return StatusCode(500, ApiErrorHelper.FormatError("An error occurred", ex));
            }
        }

    }

}
