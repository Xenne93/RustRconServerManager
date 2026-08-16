using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Extensions;
using RustRconServerManager.Backend.Services;
using SkiaSharp;
using RustRconServerManager.Backend.Helpers;

namespace RustRconServerManager.Backend.Controllers
{
    /// <summary>
    /// Controller for managing public server images (serverlist header, Rust+ logo).
    /// Images are stored in database for backup and served from disk for performance.
    /// </summary>
    [ApiController]
    public class PublicImagesController : ControllerBase
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PublicImagesController> _logger;
        private readonly MapStorageService _mapStorageService;

        public PublicImagesController(
            IServiceScopeFactory scopeFactory,
            ILogger<PublicImagesController> logger,
            MapStorageService mapStorageService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _mapStorageService = mapStorageService;
        }

        /// <summary>
        /// Serves a public server image.
        /// URL format: /{instanceHash}/public/{serverId}/{filename}
        /// </summary>
        [HttpGet("{instanceHash}/public/{serverId}/{filename}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetServerImage(string instanceHash, int serverId, string filename)
        {
            // Validate filename (only allow specific files)
            if (filename != "serverlist_image.jpg" && filename != "logoimage.jpg")
            {
                return NotFound("Invalid image filename.");
            }

            // Try to serve from disk first (fast)
            string filePath;
            try
            {
                filePath = _mapStorageService.GetServerImageFilePath(instanceHash, serverId, filename);
            }
            catch (ArgumentException)
            {
                return NotFound("Invalid image path.");
            }

            if (System.IO.File.Exists(filePath))
            {
                var imageBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                return File(imageBytes, "image/jpeg");
            }

            // Fallback: serve from database and restore to disk
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var server = await dbContext.RconServers
                .Include(s => s.SystemProfile)
                .FirstOrDefaultAsync(s => s.Id == serverId && s.SystemProfile.Hash == instanceHash);

            if (server == null)
            {
                return NotFound("Server not found.");
            }

            byte[]? imageData = filename == "serverlist_image.jpg"
                ? server.ServerHeaderImageData
                : server.ServerLogoImageData;

            if (imageData == null || imageData.Length == 0)
            {
                return NotFound("Image not found.");
            }

            // Restore to disk for next time
            await _mapStorageService.SaveServerImageToDisk(instanceHash, serverId, filename, imageData);

            return File(imageData, "image/jpeg");
        }

        // File size limits
        private const int MaxHeaderImageSize = 700 * 1024; // 700 KB
        private const int MaxLogoImageSize = 256 * 1024;   // 256 KB

        /// <summary>
        /// Uploads a server header image (serverlist image).
        /// Max size: 700KB. Allowed types: .jpg, .png
        /// </summary>
        [HttpPost("api/ServerImages/UploadHeaderImage")]
        [Authorize]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(1_000_000)] // 1MB request limit
        public async Task<IActionResult> UploadHeaderImage(IFormFile file)
        {
            return await UploadServerImage(file, "serverlist_image.jpg", isHeaderImage: true);
        }

        /// <summary>
        /// Uploads a server logo image (Rust+ logo).
        /// Max size: 256KB. Allowed types: .jpg, .png
        /// </summary>
        [HttpPost("api/ServerImages/UploadLogoImage")]
        [Authorize]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(500_000)] // 500KB request limit
        public async Task<IActionResult> UploadLogoImage(IFormFile file)
        {
            return await UploadServerImage(file, "logoimage.jpg", isHeaderImage: false);
        }

        /// <summary>
        /// Deletes a server header image.
        /// </summary>
        [HttpDelete("api/ServerImages/DeleteHeaderImage")]
        [Authorize]
        public async Task<IActionResult> DeleteHeaderImage()
        {
            return await DeleteServerImage("serverlist_image.jpg", isHeaderImage: true);
        }

        /// <summary>
        /// Deletes a server logo image.
        /// </summary>
        [HttpDelete("api/ServerImages/DeleteLogoImage")]
        [Authorize]
        public async Task<IActionResult> DeleteLogoImage()
        {
            return await DeleteServerImage("logoimage.jpg", isHeaderImage: false);
        }

        private async Task<IActionResult> UploadServerImage(IFormFile file, string filename, bool isHeaderImage)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            // Validate file type (only .jpg and .png allowed)
            var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png" };
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedTypes.Contains(file.ContentType.ToLower()) && !allowedExtensions.Contains(fileExtension))
            {
                return BadRequest("Invalid file type. Only .jpg and .png files are allowed.");
            }

            // Validate file size
            var maxSize = isHeaderImage ? MaxHeaderImageSize : MaxLogoImageSize;
            var maxSizeLabel = isHeaderImage ? "700KB" : "256KB";
            if (file.Length > maxSize)
            {
                return BadRequest($"File too large. Maximum size is {maxSizeLabel}.");
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = await User.GetUser(dbContext);
            int serverId = user.SelectedServerId ?? 0;

            if (serverId == 0)
            {
                return NotFound("No selected server.");
            }

            if (!await User.HasServerAccess(dbContext, serverId))
            {
                return Unauthorized("You do not have access to this server.");
            }

            var server = await dbContext.RconServers
                .Include(s => s.SystemProfile)
                .FirstOrDefaultAsync(s => s.Id == serverId);

            if (server == null)
            {
                return NotFound("Server not found.");
            }

            // Convert image to JPG
            byte[] jpgImageData;
            try
            {
                using var inputStream = file.OpenReadStream();
                using var memoryStream = new MemoryStream();
                await inputStream.CopyToAsync(memoryStream);
                var imageBytes = memoryStream.ToArray();

                jpgImageData = ConvertToJpgBytes(imageBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process uploaded image");
                return BadRequest(ApiErrorHelper.FormatError("Failed to process image", ex));
            }

            // Save to database
            if (isHeaderImage)
            {
                server.ServerHeaderImageData = jpgImageData;
            }
            else
            {
                server.ServerLogoImageData = jpgImageData;
            }

            await dbContext.SaveChangesAsync();

            // Save to disk
            await _mapStorageService.SaveServerImageToDisk(server.SystemProfile.Hash, serverId, filename, jpgImageData);

            // Build the public URL
            var publicUrl = $"/{server.SystemProfile.Hash}/public/{serverId}/{filename}";

            _logger.LogInformation("Uploaded {ImageType} for server {ServerId}. Size: {Size} bytes, URL: {Url}",
                isHeaderImage ? "header image" : "logo image", serverId, jpgImageData.Length, publicUrl);

            return Ok(new { Success = true, Url = publicUrl });
        }

        private async Task<IActionResult> DeleteServerImage(string filename, bool isHeaderImage)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = await User.GetUser(dbContext);
            int serverId = user.SelectedServerId ?? 0;

            if (serverId == 0)
            {
                return NotFound("No selected server.");
            }

            if (!await User.HasServerAccess(dbContext, serverId))
            {
                return Unauthorized("You do not have access to this server.");
            }

            var server = await dbContext.RconServers
                .Include(s => s.SystemProfile)
                .FirstOrDefaultAsync(s => s.Id == serverId);

            if (server == null)
            {
                return NotFound("Server not found.");
            }

            // Remove from database
            if (isHeaderImage)
            {
                server.ServerHeaderImageData = null;
            }
            else
            {
                server.ServerLogoImageData = null;
            }

            await dbContext.SaveChangesAsync();

            // Remove from disk
            _mapStorageService.DeleteServerImageFromDisk(server.SystemProfile.Hash, serverId, filename);

            _logger.LogInformation("Deleted {ImageType} for server {ServerId}",
                isHeaderImage ? "header image" : "logo image", serverId);

            return Ok(new { Success = true });
        }

        /// <summary>
        /// Converts image bytes to JPG format.
        /// </summary>
        private byte[] ConvertToJpgBytes(byte[] imageBytes)
        {
            using var inputStream = new MemoryStream(imageBytes);
            using var originalBitmap = SKBitmap.Decode(inputStream);

            if (originalBitmap == null)
            {
                throw new InvalidOperationException("Failed to decode image data.");
            }

            using var image = SKImage.FromBitmap(originalBitmap);
            using var jpgData = image.Encode(SKEncodedImageFormat.Jpeg, 90); // 90% quality

            return jpgData.ToArray();
        }
    }
}
