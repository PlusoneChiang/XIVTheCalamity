using System.Text.Json;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Dalamud.Json;

namespace XIVTheCalamity.Dalamud.Services;

/// <summary>
/// Dalamud path management service
/// </summary>
public class DalamudPathService
{
    public DalamudPathService()
    {
    }
    
    /// <summary>Dalamud root directory (shared among all profiles)</summary>
    public string BasePath => 
        Path.Combine(PlatformPathService.Instance.UserDataDirectory, "Dalamud");
    
    /// <summary>Hooks directory (Dalamud main program)</summary>
    public string HooksPath => Path.Combine(BasePath, "Hooks");
    
    /// <summary>Runtime directory (.NET Runtime)</summary>
    public string RuntimePath => Path.Combine(BasePath, "Runtime");
    
    /// <summary>Assets directory (UI resources)</summary>
    public string AssetsPath => Path.Combine(BasePath, "Assets");

    /// <summary>Profile-specific Dalamud directory</summary>
    private string ProfileDalamudPath => PlatformPathService.Instance.ActiveProfile == "default"
        ? BasePath
        : Path.Combine(PlatformPathService.Instance.UserDataDirectory, "profiles", PlatformPathService.Instance.ActiveProfile, "Dalamud");
    
    /// <summary>Configuration directory (Profile-specific)</summary>
    public string ConfigPath => Path.Combine(ProfileDalamudPath, "Config");
    
    /// <summary>Plugins directory (Profile-specific)</summary>
    public string PluginsPath => Path.Combine(ProfileDalamudPath, "Plugins");
    
    /// <summary>Get Hooks directory for specific version</summary>
    public string GetHooksVersionPath(string version) => 
        Path.Combine(HooksPath, version);
    
    /// <summary>Get dev version Hooks directory (always points to latest)</summary>
    public string HooksDevPath => Path.Combine(HooksPath, "dev");
    
    /// <summary>Get Assets directory for specific version</summary>
    public string GetAssetsVersionPath(int version) => 
        Path.Combine(AssetsPath, version.ToString());
    
    /// <summary>Get dev version Assets directory</summary>
    public string AssetsDevPath => Path.Combine(AssetsPath, "dev");
    
    /// <summary>Assets version file</summary>
    public string AssetsVersionFile => Path.Combine(AssetsPath, "asset.ver");
    
    /// <summary>Runtime version file</summary>
    public string RuntimeVersionFile => Path.Combine(RuntimePath, "version");
    
    /// <summary>Dalamud Injector path</summary>
    public string InjectorPath => Path.Combine(HooksDevPath, "Dalamud.Injector.exe");
    
    /// <summary>Dalamud config file path</summary>
    public string DalamudConfigPath => Path.Combine(ConfigPath, "dalamudConfig.json");
    
    /// <summary>Dalamud Log directory (stored in application logs/Dalamud directory)</summary>
    public string LogPath
    {
        get
        {
            var platformPaths = PlatformPathService.Instance;
            return Path.Combine(platformPaths.LogsDirectory, "Dalamud");
        }
    }
    
    /// <summary>Ensure all required directories exist</summary>
    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(HooksPath);
        Directory.CreateDirectory(RuntimePath);
        Directory.CreateDirectory(AssetsPath);
        Directory.CreateDirectory(ConfigPath);
        Directory.CreateDirectory(PluginsPath);
        Directory.CreateDirectory(LogPath);
    }
    
    /// <summary>Get locally installed Dalamud version</summary>
    public string? GetLocalVersion()
    {
        var versionFile = Path.Combine(HooksDevPath, "version.json");
        if (!File.Exists(versionFile))
            return null;
            
        try
        {
            var json = File.ReadAllText(versionFile);
            var versionInfo = JsonSerializer.Deserialize(json, DalamudJsonContext.Default.DalamudVersionInfo);
            return versionInfo?.AssemblyVersion;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>Get local Runtime version</summary>
    public string? GetLocalRuntimeVersion()
    {
        if (!File.Exists(RuntimeVersionFile))
            return null;
            
        try
        {
            return File.ReadAllText(RuntimeVersionFile).Trim();
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>Get local Assets version</summary>
    public int GetLocalAssetsVersion()
    {
        if (!File.Exists(AssetsVersionFile))
            return 0;
            
        try
        {
            var content = File.ReadAllText(AssetsVersionFile).Trim();
            return int.TryParse(content, out var version) ? version : 0;
        }
        catch
        {
            return 0;
        }
    }
}
