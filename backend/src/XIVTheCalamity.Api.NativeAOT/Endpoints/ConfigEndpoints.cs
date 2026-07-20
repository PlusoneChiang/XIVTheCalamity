using XIVTheCalamity.Api.NativeAOT.DTOs;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform;

namespace XIVTheCalamity.Api.NativeAOT.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/config");

        // GET /api/config
        group.MapGet("/", async (
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "profile")] string? profile,
            ConfigService configService,
            ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("Getting application config for profile: {Profile}", profile ?? "active");
                var config = await configService.LoadConfigAsync(profile);
                return Results.Ok(ApiResponse<AppConfig>.Ok(config));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get config");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to load configuration", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // PUT /api/config
        group.MapPut("/", async (
            [Microsoft.AspNetCore.Mvc.FromQuery(Name = "profile")] string? profile,
            AppConfig config,
            ConfigService configService,
            ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("Updating application config for profile: {Profile}", profile ?? "active");
                await configService.SaveConfigAsync(config, profile);
                return Results.Ok(ApiResponse<AppConfig>.Ok(config));
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Invalid config data");
                return Results.BadRequest(ApiErrorResponse.Create("CONFIG_INVALID", "Invalid configuration", ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update config");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to save configuration", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // PATCH /api/config
        group.MapPatch("/", async (
            AppConfig partialConfig,
            ConfigService configService,
            IEnvironmentService environmentService,
            ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("Patching application config");
                
                var currentConfig = await configService.LoadConfigAsync();
                
                // Merge changes
                if (!string.IsNullOrEmpty(partialConfig.Game.GamePath))
                    currentConfig.Game.GamePath = partialConfig.Game.GamePath;
                
                if (partialConfig.Game.Region != "TraditionalChinese")
                    currentConfig.Game.Region = partialConfig.Game.Region;
                
                // Wine configuration
                if (partialConfig.Wine != null)
                {
                    currentConfig.Wine.MetalFxSpatialEnabled = partialConfig.Wine.MetalFxSpatialEnabled;
                    currentConfig.Wine.MetalFxSpatialFactor = partialConfig.Wine.MetalFxSpatialFactor;
                    currentConfig.Wine.Metal3PerformanceOverlay = partialConfig.Wine.Metal3PerformanceOverlay;
                    currentConfig.Wine.HudScale = partialConfig.Wine.HudScale;
                    currentConfig.Wine.NativeResolution = partialConfig.Wine.NativeResolution;
                    currentConfig.Wine.MaxFramerate = partialConfig.Wine.MaxFramerate;
                    currentConfig.Wine.AudioRouting = partialConfig.Wine.AudioRouting;
                    currentConfig.Wine.Msync = partialConfig.Wine.Msync;
                    currentConfig.Wine.WineDebug = partialConfig.Wine.WineDebug;
                    currentConfig.Wine.UseHomeAlias = partialConfig.Wine.UseHomeAlias;
                    currentConfig.Wine.LeftOptionIsAlt = partialConfig.Wine.LeftOptionIsAlt;
                    currentConfig.Wine.RightOptionIsAlt = partialConfig.Wine.RightOptionIsAlt;
                    currentConfig.Wine.LeftCommandIsCtrl = partialConfig.Wine.LeftCommandIsCtrl;
                    currentConfig.Wine.RightCommandIsCtrl = partialConfig.Wine.RightCommandIsCtrl;
                }
                
                // Dalamud configuration
                if (partialConfig.Dalamud != null)
                {
                    currentConfig.Dalamud.Enabled = partialConfig.Dalamud.Enabled;
                    currentConfig.Dalamud.InjectDelay = partialConfig.Dalamud.InjectDelay;
                    currentConfig.Dalamud.SafeMode = partialConfig.Dalamud.SafeMode;
                    if (!string.IsNullOrEmpty(partialConfig.Dalamud.PluginRepoUrl))
                        currentConfig.Dalamud.PluginRepoUrl = partialConfig.Dalamud.PluginRepoUrl;
                }

                // Discord RPC configuration — no fields to update (DiscordRpcConfig is now empty)
                
                // Launcher configuration
                if (partialConfig.Launcher != null)
                {
                    currentConfig.Launcher.EncryptedArguments = partialConfig.Launcher.EncryptedArguments;
                    currentConfig.Launcher.ExitWithGame = partialConfig.Launcher.ExitWithGame;
                    currentConfig.Launcher.NonZeroExitError = partialConfig.Launcher.NonZeroExitError;
                    currentConfig.Launcher.DevelopmentMode = partialConfig.Launcher.DevelopmentMode;
                    currentConfig.Launcher.ShowDalamudTab = partialConfig.Launcher.ShowDalamudTab;
                    currentConfig.Launcher.EnablePreRelease = partialConfig.Launcher.EnablePreRelease;
                    currentConfig.Launcher.Language = partialConfig.Launcher.Language;
                }
                
                await configService.SaveConfigAsync(currentConfig);
                
                try
                {
                    logger.LogInformation("Applying platform configuration");
                    await environmentService.ApplyConfigAsync();
                }
                catch (Exception envEx)
                {
                    logger.LogWarning(envEx, "Failed to apply platform configuration, but config was saved");
                }
                
                return Results.Ok(ApiResponse<AppConfig>.Ok(currentConfig));
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Invalid config data");
                return Results.BadRequest(ApiErrorResponse.Create("CONFIG_INVALID", "Invalid configuration", ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to patch config");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to update configuration", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // POST /api/config/reset
        group.MapPost("/reset", async (
            ConfigService configService,
            ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("Resetting application config to default");
                var config = await configService.ResetToDefaultAsync();
                return Results.Ok(ApiResponse<AppConfig>.Ok(config));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reset config");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to reset configuration", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // GET /api/config/path
        group.MapGet("/path", (
            ConfigService configService,
            ILogger<Program> logger) =>
        {
            try
            {
                var path = configService.GetConfigPath();
                return Results.Ok(ApiResponse<ConfigPathResponse>.Ok(new ConfigPathResponse(path)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get config path");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to get config path", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // GET /api/config/profiles
        group.MapGet("/profiles", async (ConfigService configService, ILogger<Program> logger) =>
        {
            try
            {
                var pathService = PlatformPathService.Instance;
                var active = pathService.ActiveProfile;
                var profilesDir = Path.Combine(pathService.AppDataDirectory, "profiles");
                var profilesList = new List<ProfileItem>();

                var defaultConfig = await configService.LoadConfigAsync("default");
                profilesList.Add(new ProfileItem("default", defaultConfig.Launcher?.Description ?? ""));

                if (Directory.Exists(profilesDir))
                {
                    var dirs = Directory.GetDirectories(profilesDir);
                    foreach (var dir in dirs)
                    {
                        var name = Path.GetFileName(dir);
                        if (!string.IsNullOrEmpty(name))
                        {
                            var profileConfig = await configService.LoadConfigAsync(name);
                            profilesList.Add(new ProfileItem(name, profileConfig.Launcher?.Description ?? ""));
                        }
                    }
                }

                return Results.Ok(ApiResponse<ProfilesResponse>.Ok(new ProfilesResponse(active, profilesList.ToArray())));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get profiles");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to get profiles", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // POST /api/config/profiles (Add Profile only, does NOT switch)
        group.MapPost("/profiles", async (
            ProfileSwitchRequest request,
            ConfigService configService,
            ILogger<Program> logger) =>
        {
            try
            {
                var pathService = PlatformPathService.Instance;
                var targetName = request.Name;

                logger.LogInformation("Creating new profile: {Name} (copyDefault: {CopyDefault})", targetName, request.CopyDefault);

                var targetAppDataDir = Path.Combine(pathService.AppDataDirectory, "profiles", targetName);
                Directory.CreateDirectory(targetAppDataDir);

                if (request.CopyDefault && targetName != "default")
                {
                    // Copy config.json
                    var defaultJson = Path.Combine(pathService.AppDataDirectory, "config.json");
                    var targetJson = Path.Combine(targetAppDataDir, "config.json");
                    if (File.Exists(defaultJson) && !File.Exists(targetJson))
                    {
                        File.Copy(defaultJson, targetJson);
                    }

                    // Copy ffxivConfig
                    var defaultFfxiv = Path.Combine(pathService.AppDataDirectory, "ffxivConfig");
                    var targetFfxiv = Path.Combine(targetAppDataDir, "ffxivConfig");
                    if (Directory.Exists(defaultFfxiv) && !Directory.Exists(targetFfxiv))
                    {
                        CopyDirectoryRecursive(defaultFfxiv, targetFfxiv);
                    }

                    // Copy Dalamud Config & Plugins if they exist in default profile
                    var defaultDalamudConfig = Path.Combine(pathService.UserDataDirectory, "dalamud", "config");
                    var targetDalamudConfig = Path.Combine(pathService.UserDataDirectory, "profiles", targetName, "dalamud", "config");
                    if (Directory.Exists(defaultDalamudConfig) && !Directory.Exists(targetDalamudConfig))
                    {
                        CopyDirectoryRecursive(defaultDalamudConfig, targetDalamudConfig);
                    }

                    var defaultDalamudPlugins = Path.Combine(pathService.UserDataDirectory, "dalamud", "installedPlugins");
                    var targetDalamudPlugins = Path.Combine(pathService.UserDataDirectory, "profiles", targetName, "dalamud", "installedPlugins");
                    if (Directory.Exists(defaultDalamudPlugins) && !Directory.Exists(targetDalamudPlugins))
                    {
                        CopyDirectoryRecursive(defaultDalamudPlugins, targetDalamudPlugins);
                    }
                }

                var active = pathService.ActiveProfile;
                var profilesDir = Path.Combine(pathService.AppDataDirectory, "profiles");
                var profilesList = new List<ProfileItem>();

                var defaultConfig = await configService.LoadConfigAsync("default");
                profilesList.Add(new ProfileItem("default", defaultConfig.Launcher?.Description ?? ""));

                if (Directory.Exists(profilesDir))
                {
                    var dirs = Directory.GetDirectories(profilesDir);
                    foreach (var dir in dirs)
                    {
                        var name = Path.GetFileName(dir);
                        if (!string.IsNullOrEmpty(name))
                        {
                            var profileConfig = await configService.LoadConfigAsync(name);
                            profilesList.Add(new ProfileItem(name, profileConfig.Launcher?.Description ?? ""));
                        }
                    }
                }

                return Results.Ok(ApiResponse<ProfilesResponse>.Ok(new ProfilesResponse(active, profilesList.ToArray())));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create profile");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to create profile", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // POST /api/config/profiles/switch (Switch active profile persistently)
        group.MapPost("/profiles/switch", async (
            ProfileSwitchRequest request,
            ConfigService configService,
            ILogger<Program> logger) =>
        {
            try
            {
                var pathService = PlatformPathService.Instance;
                var targetName = request.Name;

                logger.LogInformation("Switching active profile persistently to: {Name}", targetName);

                pathService.SwitchProfile(targetName);
                var config = await configService.LoadConfigAsync();

                return Results.Ok(ApiResponse<AppConfig>.Ok(config));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to switch active profile");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to switch active profile", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });

        // DELETE /api/config/profiles/{name}
        group.MapDelete("/profiles/{name}", (string name, ILogger<Program> logger) =>
        {
            try
            {
                if (name == "default")
                    return Results.BadRequest(ApiErrorResponse.Create("INVALID_OPERATION", "Default profile cannot be deleted"));

                var pathService = PlatformPathService.Instance;
                
                // Delete appData profile folder
                var appDataProfileDir = Path.Combine(pathService.AppDataDirectory, "profiles", name);
                if (Directory.Exists(appDataProfileDir))
                {
                    Directory.Delete(appDataProfileDir, true);
                }

                // Delete userData profile folder
                var userDataProfileDir = Path.Combine(pathService.UserDataDirectory, "profiles", name);
                if (Directory.Exists(userDataProfileDir))
                {
                    Directory.Delete(userDataProfileDir, true);
                }

                logger.LogInformation("Deleted profile: {Name}", name);
                return Results.Ok(ApiResponse<object>.Ok(new { success = true }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete profile");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "Failed to delete profile", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, dest, true);
        }
        foreach (string folder in Directory.GetDirectories(sourceDir))
        {
            string dest = Path.Combine(destinationDir, Path.GetFileName(folder));
            CopyDirectoryRecursive(folder, dest);
        }
    }
}
