namespace XIVTheCalamity.Dalamud.Services;

/// <summary>
/// Dalamud path management service
/// </summary>
public class DalamudPathService
{
    private readonly string _basePath;
    
    public DalamudPathService()
    {
        // Use Application Support directory
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _basePath = Path.Combine(appSupport, "XIVTheCalamity", "Dalamud");
    }
    
    /// <summary>Dalamud root directory</summary>
    public string BasePath => _basePath;
    
    /// <summary>Hooks directory (Dalamud main program)</summary>
    public string HooksPath => Path.Combine(_basePath, "Hooks");
    
    /// <summary>Runtime directory (.NET Runtime)</summary>
    public string RuntimePath => Path.Combine(_basePath, "Runtime");
    
    /// <summary>Assets directory (UI resources)</summary>
    public string AssetsPath => Path.Combine(_basePath, "Assets");
    
    /// <summary>Configuration directory</summary>
    public string ConfigPath => Path.Combine(_basePath, "Config");
    
    /// <summary>Plugins directory</summary>
    public string PluginsPath => Path.Combine(_basePath, "Plugins");
    
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
    public string DalamudConfigPath => Path.Combine(ConfigPath, "dalamud.json");
    
    /// <summary>Dalamud Log directory (stored in application logs/Dalamud directory)</summary>
    public string LogPath
    {
        get
        {
            var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appSupport, "XIVTheCalamity", "logs", "Dalamud");
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
            var versionInfo = System.Text.Json.JsonSerializer.Deserialize<Models.DalamudVersionInfo>(json);
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
