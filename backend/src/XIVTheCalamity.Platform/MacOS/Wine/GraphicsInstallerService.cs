using Microsoft.Extensions.Logging;

namespace XIVTheCalamity.Platform.MacOS.Wine;

/// <summary>
/// Graphics backend installation service
/// Reference: XoM GraphicsInstaller.swift
/// Copies DXMT/DXVK DLLs to Wine Prefix
/// </summary>
public class GraphicsInstallerService
{
    private readonly WinePathService _paths;
    private readonly ILogger<GraphicsInstallerService>? _logger;
    
    private string System32 => _paths.PrefixSystem32;
    
    public GraphicsInstallerService(ILogger<GraphicsInstallerService>? logger = null)
    {
        _paths = WinePathService.Instance;
        _logger = logger;
    }
    
    /// <summary>
    /// Get resources directory path
    /// </summary>
    private string GetResourcesPath()
    {
        var appDir = AppContext.BaseDirectory;
        _logger?.LogDebug("[GraphicsInstaller] AppContext.BaseDirectory: {AppDir}", appDir);
        
        // Bundle environment: backend in Contents/Resources/backend/
        // resources in Contents/Resources/resources/
        var bundleResources = Path.Combine(appDir, "..", "resources");
        bundleResources = Path.GetFullPath(bundleResources);
        if (Directory.Exists(bundleResources))
        {
            _logger?.LogDebug("[GraphicsInstaller] Found bundle resources: {Path}", bundleResources);
            return bundleResources;
        }
        
        // Development environment: search upward for shared/resources
        var currentDir = new DirectoryInfo(appDir);
        while (currentDir != null)
        {
            var resourcesPath = Path.Combine(currentDir.FullName, "shared", "resources");
            if (Directory.Exists(resourcesPath))
            {
                _logger?.LogDebug("[GraphicsInstaller] Found dev resources: {Path}", resourcesPath);
                return resourcesPath;
            }
            currentDir = currentDir.Parent;
        }
        
        throw new DirectoryNotFoundException($"Resources directory not found. Searched from: {appDir}");
    }
    
    /// <summary>
    /// Install DLL to system32
    /// </summary>
    private void InstallDll(string sourcePath)
    {
        var dllName = Path.GetFileName(sourcePath);
        var targetPath = Path.Combine(System32, dllName);
        var backupPath = targetPath + ".old";
        
        // Ensure directory exists
        Directory.CreateDirectory(System32);
        
        // Compare if files are identical
        if (File.Exists(targetPath) && FilesAreEqual(sourcePath, targetPath))
        {
            _logger?.LogDebug("[GraphicsInstaller] {Dll} already up to date", dllName);
            return;
        }
        
        // Backup original Wine DLL
        if (File.Exists(targetPath) && !File.Exists(backupPath))
        {
            try
            {
                File.Move(targetPath, backupPath);
                _logger?.LogDebug("[GraphicsInstaller] Backed up {Dll} to {Backup}", dllName, backupPath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[GraphicsInstaller] Failed to backup {Dll}", dllName);
            }
        }
        
        // Copy new DLL
        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Copy(sourcePath, targetPath);
            
            // Set execute permission (Unix)
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(targetPath, 
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            
            _logger?.LogInformation("[GraphicsInstaller] Installed {Dll}", dllName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[GraphicsInstaller] Failed to install {Dll}", dllName);
            throw;
        }
    }
    
    /// <summary>
    /// Restore original Wine DLL
    /// </summary>
    private void RestoreDll(string dllName)
    {
        var targetPath = Path.Combine(System32, dllName);
        var backupPath = targetPath + ".old";
        
        if (!File.Exists(backupPath))
        {
            _logger?.LogDebug("[GraphicsInstaller] No backup found for {Dll}", dllName);
            return;
        }
        
        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(backupPath, targetPath);
            
            // Set execute permission (Unix)
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(targetPath, 
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            
            _logger?.LogInformation("[GraphicsInstaller] Restored {Dll}", dllName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[GraphicsInstaller] Failed to restore {Dll}", dllName);
        }
    }
    
    /// <summary>
    /// Install DXMT
    /// </summary>
    public void InstallDxmt()
    {
        var resources = GetResourcesPath();
        var dxmtPath = Path.Combine(resources, "dxmt");
        
        if (!Directory.Exists(dxmtPath))
        {
            var error = $"DXMT directory not found: {dxmtPath}";
            _logger?.LogError("[GraphicsInstaller] {Error}", error);
            throw new DirectoryNotFoundException(error);
        }
        
        var d3d11 = Path.Combine(dxmtPath, "d3d11.dll");
        var dxgi = Path.Combine(dxmtPath, "dxgi.dll");
        
        if (!File.Exists(d3d11))
        {
            var error = $"DXMT d3d11.dll not found: {d3d11}";
            _logger?.LogError("[GraphicsInstaller] {Error}", error);
            throw new FileNotFoundException(error);
        }
        
        if (!File.Exists(dxgi))
        {
            var error = $"DXMT dxgi.dll not found: {dxgi}";
            _logger?.LogError("[GraphicsInstaller] {Error}", error);
            throw new FileNotFoundException(error);
        }
        
        InstallDll(d3d11);
        InstallDll(dxgi);
    }
    
    /// <summary>
    /// Install DXVK
    /// </summary>
    public void InstallDxvk()
    {
        var resources = GetResourcesPath();
        var dxvkPath = Path.Combine(resources, "dxvk");
        
        if (!Directory.Exists(dxvkPath))
        {
            var error = $"DXVK directory not found: {dxvkPath}";
            _logger?.LogError("[GraphicsInstaller] {Error}", error);
            throw new DirectoryNotFoundException(error);
        }
        
        var d3d11 = Path.Combine(dxvkPath, "d3d11.dll");
        // DXVK doesn't need dxgi.dll, uses Wine's built-in version
        
        if (!File.Exists(d3d11))
        {
            var error = $"DXVK d3d11.dll not found: {d3d11}";
            _logger?.LogError("[GraphicsInstaller] {Error}", error);
            throw new FileNotFoundException(error);
        }
        
        InstallDll(d3d11);
    }
    
    /// <summary>
    /// Uninstall DXMT (restore original DLLs)
    /// </summary>
    public void UninstallDxmt()
    {
        RestoreDll("d3d11.dll");
        RestoreDll("dxgi.dll");
    }
    
    /// <summary>
    /// Install d3dcompiler_47.dll
    /// </summary>
    public void InstallD3dCompiler()
    {
        var resources = GetResourcesPath();
        var d3dcompilerPath = Path.Combine(resources, "d3dcompiler", "d3dcompiler_47.dll");
        
        if (!File.Exists(d3dcompilerPath))
        {
            // Try alternative path
            d3dcompilerPath = Path.Combine(resources, "d3dcompiler_47.dll");
        }
        
        if (File.Exists(d3dcompilerPath))
        {
            InstallDll(d3dcompilerPath);
        }
        else
        {
            _logger?.LogDebug("[GraphicsInstaller] d3dcompiler_47.dll not found, skipping");
        }
    }
    
    /// <summary>
    /// Ensure graphics backend is installed
    /// Choose DXMT or DXVK based on configuration
    /// </summary>
    public void EnsureBackend(bool dxmtEnabled)
    {
        _logger?.LogInformation("[GraphicsInstaller] Ensuring graphics backend (DXMT={DxmtEnabled})", dxmtEnabled);
        
        // Install d3dcompiler
        InstallD3dCompiler();
        
        if (dxmtEnabled)
        {
            InstallDxmt();
        }
        else
        {
            InstallDxvk();
            UninstallDxmt();
        }
    }
    
    /// <summary>
    /// Compare if two files have identical content
    /// </summary>
    private static bool FilesAreEqual(string path1, string path2)
    {
        var info1 = new FileInfo(path1);
        var info2 = new FileInfo(path2);
        
        if (info1.Length != info2.Length)
            return false;
        
        // For large files, only compare size and modification time
        if (info1.Length > 10 * 1024 * 1024) // > 10MB
        {
            return info1.LastWriteTimeUtc == info2.LastWriteTimeUtc;
        }
        
        // Compare content for small files
        return File.ReadAllBytes(path1).SequenceEqual(File.ReadAllBytes(path2));
    }
}
