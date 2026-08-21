using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RustRconServerManager.Backend.Database;
using RustRconServerManager.Backend.Services;
using Pomelo.EntityFrameworkCore.MySql;
using RustRconServerManager.Backend.SignalRHubs;
using Xenne.RCON;
using RustRconServerManager.Backend.Interfaces;
using RustRconServerManager.Backend.Middleware;
using RustRconServerManager.Backend.Models;
using RustRconServerManager.Backend.Helpers;

var builder = WebApplication.CreateBuilder(args);

// The minimum log level is adjustable at runtime from the Panel Settings page (see
// LogLevelState and PanelSettingsController.SetLogLevel) instead of being fixed at
// startup - this filter delegate is re-evaluated on every single log call, so changing
// the setting takes effect immediately without restarting the app. The persisted value
// is loaded into LogLevelState further down, once the database is available.
builder.Logging.AddFilter((category, level) => level >= LogLevelState.Minimum);

// Add services to the container.

builder.Services.AddControllers(options =>
{
    options.Filters.Add<RustRconServerManager.Backend.Filters.UserNotAuthenticatedExceptionFilter>();
    options.Filters.Add<RustRconServerManager.Backend.Filters.AuditLogActionFilter>();
});

// Records every mutating action taken by an admin/moderator (see AuditLogActionFilter, AuditLogService)
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddSingleton<IRconPasswordsCryptoService, RconPasswordsCryptoService>();

// Mirrors PanelSettings.AutoUpdateEnabled to a flag file the update-check scripts read
builder.Services.AddSingleton<AutoUpdateFlagFileService>();

// Register Steam API Service (for future use)
builder.Services.AddHttpClient<SteamApiService>()
    .ConfigureHttpClient(client => { });
builder.Services.AddScoped<ISteamApiService>(provider => provider.GetRequiredService<SteamApiService>());

// Register IP Geolocation Service (for country detection from player IP)
builder.Services.AddHttpClient<IpGeolocationService>()
    .ConfigureHttpClient(client => { });
builder.Services.AddScoped<IIpGeolocationService>(provider => provider.GetRequiredService<IpGeolocationService>());

// Register ProxyCheck Service (VPN/proxy detection via proxycheck.io)
builder.Services.AddHttpClient<IProxyCheckService, ProxyCheckService>();

// Register Player Protection Service (enforces server protection rules)
builder.Services.AddScoped<IPlayerProtectionService, PlayerProtectionService>();

// Register Discord Webhook Service (for Discord event notifications)
builder.Services.AddHttpClient<IDiscordWebhookService, DiscordWebhookService>();

builder.Services.AddHttpClient<IAiService, AiService>();

// Register Email Service (for sending password recovery emails via local SMTP)
builder.Services.AddScoped<IEmailService, EmailService>();

// Register Plugin Version Check Service (for checking plugin versions from Codefling and Umod)
builder.Services.AddHttpClient<PluginVersionCheckService>(client =>
{
    // Set User-Agent header to get proper rate limits from Umod (35/min instead of 1/min)
    client.DefaultRequestHeaders.Add("User-Agent", "RCONManagementPanel");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(10);
})
.SetHandlerLifetime(TimeSpan.FromMinutes(5));

// Register the RconConnectionManager as a singleton service
builder.Services.AddSingleton<RconConnectionManager>(); // Register the dRconConnectionManager as a singleton service

// Register the RconBackgroundService as a singleton service
builder.Services.AddSingleton<RconBackgroundService>();

// Register the IRconBackgroundService interface to the RconBackgroundService implementation
builder.Services.AddSingleton<IRconBackgroundService>(provider => provider.GetRequiredService<RconBackgroundService>());

// Register the ScheduledCommandService as a scoped service
builder.Services.AddScoped<ScheduledCommandService>();

// Register the TriggerExecutionService as a singleton service
builder.Services.AddSingleton<TriggerExecutionService>();

// Register the MapStorageService (restores maps from database to disk on startup)
builder.Services.AddSingleton<MapStorageService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<MapStorageService>());

// Register the RrsmModDataService (pulls map/sleepingbag/toolcupboard data from Rust servers over RCON)
builder.Services.AddScoped<RrsmModDataService>();

// Register the AnalyticsReportingService (opt-in daily anonymous usage check-in - see PanelSettings.AnalyticsEnabled)
builder.Services.AddHttpClient();
builder.Services.AddHostedService<AnalyticsReportingService>();

// Register the RconClientFactory as a singleton service
builder.Services.AddHostedService(provider => provider.GetRequiredService<RconBackgroundService>());

// Register the RconClientFactory as a singleton service
builder.Services.AddSignalR();


// Register the AppDbContext with MySQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"),
    new MySqlServerVersion(new Version(8, 0, 34)))
);



builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        // Email is optional (login is by username) - RequireUniqueEmail would otherwise
        // reject account creation entirely whenever no email is provided at all.
        // Uniqueness among accounts that DO have one is still enforced manually where
        // an email is set/changed (see AuthController.Setup, ModeratorController, SecurityController.ChangeEmail).
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();


builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.FromSeconds(10) // Allow 10 second clock skew for client-server time differences
        };

        // Read JWT from HttpOnly cookie or SignalR query string
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var path = context.HttpContext.Request.Path;

                // For SignalR WebSocket connections, check query string first (legacy support)
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/liveconsole") || path.StartsWithSegments("/livechat")))
                {
                    context.Token = accessToken;
                    return Task.CompletedTask;
                }

                // For all requests: read JWT from HttpOnly cookie
                var cookieToken = context.Request.Cookies["rrsm_auth"];
                if (!string.IsNullOrEmpty(cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();


var app = builder.Build();

// Configure API error response detail level
ApiErrorHelper.Configure(builder.Configuration.GetValue<bool>("AppSettings:ShowDetailedErrors", false));

// Check and generate unique cryptographic keys on first startup
try
{
    var keyManager = new ConfigurationKeyManager(
        app.Services.GetRequiredService<ILogger<ConfigurationKeyManager>>()
    );
    var keysGenerated = await keyManager.EnsureUniqueKeysAsync(builder.Configuration);

    if (keysGenerated)
    {
        Console.WriteLine("[STARTUP] New cryptographic keys have been generated and saved to appsettings.json");
        Console.WriteLine("[STARTUP] IMPORTANT: Please restart the application for the new keys to take effect.");
        Environment.Exit(0);
        return;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[STARTUP] Warning: Failed to check/generate configuration keys: {ex.Message}");
}

// Apply database migrations automatically on startup
try
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.Migrate();
        Console.WriteLine("[STARTUP] Database migrations applied successfully.");

        // Seed Rust items if not already present
        await RustItemSeeder.SeedAsync(dbContext, app.Logger);

        // Load the persisted minimum log level (falls back to the LogLevelState default,
        // Error, if no panel settings row exists yet or the stored value is unrecognized)
        var firstPanelSettings = await dbContext.PanelSettings.FirstOrDefaultAsync();
        if (firstPanelSettings != null && LogLevelState.TryParse(firstPanelSettings.MinimumLogLevel, out var savedLogLevel))
        {
            LogLevelState.Minimum = savedLogLevel;
        }

        // Re-sync the auto-update flag file from the database on every boot, in case it
        // was ever changed by something other than the Panel Settings page (or is missing
        // entirely on an upgraded install). The update-check scripts read this file before
        // this process even starts, since they have no access to the database driver.
        if (firstPanelSettings != null)
        {
            scope.ServiceProvider.GetRequiredService<AutoUpdateFlagFileService>().Write(firstPanelSettings.AutoUpdateEnabled);
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[STARTUP ERROR] Failed to apply database migrations: {ex.Message}");
    throw;
}

// `--reset-password` runs an interactive terminal password reset instead of starting the
// web server - for admins locked out with no working SMTP configuration for the email-code
// "forgot password" flow. Exits here (before Kestrel would bind a port) so it can safely
// run as a second process alongside an already-running instance sharing the same database.
if (args.Contains("--reset-password"))
{
    var exitCode = await RustRconServerManager.Backend.Cli.ResetPasswordCli.RunAsync(app.Services);
    Environment.Exit(exitCode);
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var showDetails = app.Configuration["DetailedErrors"] == "true" 
                          || Environment.GetEnvironmentVariable("ASPNETCORE_DETAILEDERRORS") == "true";

        var problem = Results.Problem(
            title: "Server Error",
            detail: showDetails ? feature?.Error.ToString() : "Something went wrong.",
            statusCode: 500
        );
        await problem.ExecuteAsync(context);
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


// Configure CORS. The Blazor frontend is served from the same origin as this API by default,
// so this only matters if you're hosting the frontend separately or behind a different subdomain.
// Add extra origins via the "Cors:AllowedOrigins" config section or the CORS_ALLOWED_ORIGINS env var (comma-separated).
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
app.UseCors(policy =>
    policy.SetIsOriginAllowed(origin =>
        {
            var uri = new Uri(origin);
            if (uri.Host == "localhost" || uri.Host == "127.0.0.1") return true;
            return configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
        })
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());

// Disabled for reverse proxy compatibility (Traefik handles SSL)
// app.UseHttpsRedirection();

// Serve static files (CSS, JS, icons, etc.) before authentication
// This ensures stylesheets and resources load correctly on first visit
// Configure Blazor framework files with proper cache headers
var blazorOptions = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Framework files are versioned, so cache them aggressively
        if (ctx.Context.Request.Path.StartsWithSegments("/_framework"))
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        }
        // For index.html and other root files, no cache
        else
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache,no-store,must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";
        }
    }
};

app.UseBlazorFrameworkFiles();

// Configure static file serving with proper cache headers
var staticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.PhysicalPath ?? ctx.Context.Request.Path.Value ?? "";

        // For versioned Blazor framework files (_framework), cache aggressively
        if (ctx.Context.Request.Path.StartsWithSegments("/_framework"))
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        }
        // For fonts and icons, cache but allow revalidation
        else if (path.EndsWith(".woff") || path.EndsWith(".woff2") ||
                 path.EndsWith(".ttf") || path.EndsWith(".eot") ||
                 path.EndsWith(".svg") || path.EndsWith(".otf"))
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=604800,must-revalidate";
        }
        // For CSS and JS files (not framework), always revalidate so deploys take effect immediately
        else if (path.EndsWith(".css") || path.EndsWith(".js"))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
        // For HTML files, no cache
        else if (path.EndsWith(".html"))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache,no-store,must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";
        }
        // For images, cache moderately
        else if (path.EndsWith(".png") || path.EndsWith(".jpg") ||
                 path.EndsWith(".jpeg") || path.EndsWith(".gif") ||
                 path.EndsWith(".ico") || path.EndsWith(".webp"))
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=86400";
        }
        // Default: cache briefly with revalidation
        else
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=300,must-revalidate";
        }
    }
};

app.UseStaticFiles(staticFileOptions);

app.UseAuthentication();

// Custom middleware to enforce security binding checks
// This middleware checks if the user's IP and User-Agent match the ones stored in the JWT claims
app.UseMiddleware<SecurityBindingMiddleware>();

// UseRouting must be called before UseAuthorization
// This middleware is responsible for routing requests to the appropriate endpoints
app.UseAuthorization();


app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Publicly readable (no auth) so the UI footer can show it on every page, including
// pre-login. Mirrors the same .version file the Docker/standalone auto-update scripts
// write next to the app (see check-update.sh, Dockerfile, standalone/start.ps1/.sh).
app.MapGet("/api/version", (IWebHostEnvironment env) =>
{
    var versionFile = Path.Combine(env.ContentRootPath, ".version");
    var version = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "dev";
    return Results.Ok(new { version });
});

// Pre-read index.html once at startup so the SPA fallback can serve it directly.
var indexHtmlContent = string.Empty;
{
    string? indexPath = null;
    var candidates = new List<string>();

    if (!string.IsNullOrEmpty(app.Environment.WebRootPath))
        candidates.Add(Path.Combine(app.Environment.WebRootPath, "index.html"));

    // Local dev fallback: Frontend project's wwwroot
    var frontendWwwroot = Path.Combine(app.Environment.ContentRootPath, "..", "RustRconServerManager.Frontend", "wwwroot", "index.html");
    candidates.Add(Path.GetFullPath(frontendWwwroot));

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            indexPath = candidate;
            break;
        }
    }

    if (indexPath != null)
    {
        indexHtmlContent = await File.ReadAllTextAsync(indexPath);
        app.Logger.LogInformation("[STARTUP] index.html loaded from {Path}", indexPath);

        // Append a version-based cache-busting query string to the CSS links. These files
        // keep the same URL across deployments, so - unlike index.html itself, which is
        // never cached - a browser or intermediary proxy that doesn't revalidate strictly
        // correctly against Cache-Control can still serve a stale cached copy after an
        // update (reported as "some parts of the stylesheet don't load until a refresh").
        // Changing the URL on every version forces a fresh fetch regardless of how any
        // particular cache layer handles revalidation.
        var versionFilePath = Path.Combine(app.Environment.ContentRootPath, ".version");
        var cssVersion = File.Exists(versionFilePath) ? File.ReadAllText(versionFilePath).Trim() : "dev";
        indexHtmlContent = indexHtmlContent
            .Replace("/css/tailwind-output.css\"", $"/css/tailwind-output.css?v={cssVersion}\"")
            .Replace("/css/app.css\"", $"/css/app.css?v={cssVersion}\"");
    }
    else
    {
        app.Logger.LogWarning("[STARTUP] index.html not found in any candidate path");
    }
}

// Serve the pre-built index.html for all Blazor fallback routes.
// Uses no-store so browsers and proxies never cache it — ensuring a fresh
// copy (with the correct CSS URLs) is always served after a deployment.
app.MapFallback(async (HttpContext ctx) =>
{
    if (string.IsNullOrEmpty(indexHtmlContent))
    {
        ctx.Response.StatusCode = 404;
        return;
    }

    ctx.Response.ContentType = "text/html; charset=utf-8";
    ctx.Response.Headers.CacheControl = "no-cache,no-store,must-revalidate";
    ctx.Response.Headers.Pragma = "no-cache";
    ctx.Response.Headers.Expires = "0";
    await ctx.Response.WriteAsync(indexHtmlContent);
});

app.MapHub<LiveConsoleHub>("/liveconsole");
app.MapHub<LiveChatHub>("/livechat");
app.Run();
