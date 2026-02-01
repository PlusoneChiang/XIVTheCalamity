using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Platform.MacOS.Wine;

/// <summary>
/// Wine Prefix management service
/// Reference: XoM Wine.swift boot() and ensurePrefix()
/// Integrates WineRegistryService, WineFontService and WineRegistryFile
/// </summary>
public class WinePrefixService
{
    private readonly WinePathService _paths;
    private readonly ILogger<WinePrefixService>? _logger;
    private readonly GraphicsInstallerService _graphicsInstaller;
    
    // Registry batch mode support (from WineRegistryService)
    private WineRegistryFile? _batchFile;
    private bool _inBatch = false;

    public WinePrefixService(ILogger<WinePrefixService>? logger = null)
    {
        _paths = WinePathService.Instance;
        _logger = logger;
        _graphicsInstaller = new GraphicsInstallerService(null); // TODO: inject logger
    }

    /// <summary>
    /// Ensure Wine Prefix exists and is initialized
    /// Reference XoM CompatibilityTools.EnsurePrefix():
    /// 1. Execute wineboot -u to initialize prefix
    /// 2. Execute wineserver -w to wait for initialization completion
    /// </summary>
    public async Task EnsurePrefixAsync(CancellationToken cancellationToken = default)
    {
        // Check if prefix already exists and contains basic files
        var userRegPath = Path.Combine(_paths.WinePrefix, "user.reg");
        var systemRegPath = Path.Combine(_paths.WinePrefix, "system.reg");
        
        if (Directory.Exists(_paths.PrefixDriveC) && 
            File.Exists(userRegPath) && 
            File.Exists(systemRegPath))
        {
            _logger?.LogInformation("Wine Prefix already initialized");
            return;
        }

        _logger?.LogInformation("Initializing Wine prefix at {Path}", _paths.WinePrefix);

        try
        {
            // Ensure prefix directory exists
            Directory.CreateDirectory(_paths.WinePrefix);

            var env = _paths.GetEnvironment();
            
            // Step 1: Execute wineboot -u to initialize prefix
            _logger?.LogInformation("Running wineboot -u to initialize prefix...");
            var winebootStartInfo = new ProcessStartInfo
            {
                FileName = _paths.Wine,
                ArgumentList = { "wineboot", "-u" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var (key, value) in env)
            {
                winebootStartInfo.Environment[key] = value;
            }

            using var winebootProcess = Process.Start(winebootStartInfo);
            if (winebootProcess is null)
            {
                throw new Exception("Failed to start wineboot");
            }

            await winebootProcess.WaitForExitAsync(cancellationToken);
            _logger?.LogInformation("wineboot completed with exit code {ExitCode}", winebootProcess.ExitCode);

            // Step 2: Execute wineserver -w to wait for all wine processes to complete
            _logger?.LogInformation("Running wineserver -w to wait for initialization...");
            var wineserverStartInfo = new ProcessStartInfo
            {
                FileName = _paths.WineServer,
                ArgumentList = { "-w" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var (key, value) in env)
            {
                wineserverStartInfo.Environment[key] = value;
            }

            using var wineserverProcess = Process.Start(wineserverStartInfo);
            if (wineserverProcess is null)
            {
                throw new Exception("Failed to start wineserver");
            }

            await wineserverProcess.WaitForExitAsync(cancellationToken);
            _logger?.LogInformation("wineserver completed - prefix fully initialized");

            // Validate prefix structure
            if (!Directory.Exists(_paths.PrefixDriveC))
            {
                throw new Exception("Prefix initialization failed: drive_c not found");
            }

            if (!File.Exists(userRegPath))
            {
                throw new Exception("Prefix initialization failed: user.reg not found");
            }

            _logger?.LogInformation("Wine Prefix initialized successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize prefix");
            throw;
        }
    }

    /// <summary>
    /// Check if Wine Prefix is fully initialized
    /// </summary>
    public bool IsFullyInitialized()
    {
        try
        {
            // Check if prefix directory exists
            if (!Directory.Exists(_paths.PrefixDriveC))
            {
                _logger?.LogDebug("Prefix not initialized: drive_c not found");
                return false;
            }

            // Check if font is installed
            var fontPath = Path.Combine(_paths.PrefixFonts, _paths.FontFile);
            if (!File.Exists(fontPath))
            {
                _logger?.LogDebug("Prefix not fully initialized: font not installed");
                return false;
            }

            // Check if registry is configured
            var userRegPath = Path.Combine(_paths.WinePrefix, "user.reg");
            if (!File.Exists(userRegPath))
            {
                _logger?.LogDebug("Prefix not fully initialized: user.reg not found");
                return false;
            }

            var content = File.ReadAllText(userRegPath);
            if (!content.Contains("DllOverrides") || !content.Contains("winegstreamer"))
            {
                _logger?.LogDebug("Prefix not fully initialized: MediaFoundation not configured");
                return false;
            }

            // Check if graphics DLLs are installed (DXMT d3d11.dll should be ~4.7MB)
            var d3d11Path = Path.Combine(_paths.PrefixSystem32, "d3d11.dll");
            var dxgiPath = Path.Combine(_paths.PrefixSystem32, "dxgi.dll");
            if (!File.Exists(d3d11Path) || !File.Exists(dxgiPath))
            {
                _logger?.LogDebug("Prefix not fully initialized: Graphics DLLs not installed");
                return false;
            }
            
            // Verify DXMT DLL size (should be > 2MB, Wine built-in is ~1MB)
            var d3d11Size = new FileInfo(d3d11Path).Length;
            if (d3d11Size < 2_000_000)
            {
                _logger?.LogDebug("Prefix not fully initialized: d3d11.dll is Wine built-in, not DXMT");
                return false;
            }

            _logger?.LogDebug("Wine Prefix is fully initialized");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error checking initialization status");
            return false;
        }
    }

    /// <summary>
    /// Initialize Wine Prefix (Fast mode - following XoM pattern)
    /// Only performs essential synchronous initialization, other steps run in background
    /// </summary>
    /// <summary>
    /// Initialize Wine Prefix with progress reporting using IAsyncEnumerable
    /// NEW: Returns async enumerable for SSE-friendly progress reporting
    /// </summary>
    public async IAsyncEnumerable<WineInitProgress> InitializePrefixAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("[WINE-INIT] ========== Wine Prefix Initialization Started ==========");

        // Check if already fully initialized
        if (IsFullyInitialized())
        {
            _logger?.LogInformation("[WINE-INIT] Wine Prefix already fully initialized, skipping");
            yield return new WineInitProgress
            {
                Stage = WineInitStage.Complete,
                MessageKey = "progress.already_initialized",
                IsComplete = true
            };
            _logger?.LogInformation("[WINE-INIT] ========== Completed (Already Initialized) ==========");
            yield break;
        }
        
        _logger?.LogInformation("[WINE-INIT] Not fully initialized, proceeding with initialization");

        // Collect all progress events first to avoid yield in try-catch
        var events = new List<WineInitProgress>();
        WineInitProgress? finalEvent = null;
        
        try
        {
            // 1. Check Prefix
            events.Add(new WineInitProgress
            {
                Stage = WineInitStage.Checking,
                MessageKey = "progress.checking"
            });
            _logger?.LogDebug("[WINE-INIT] Stage 1: Checking Wine Prefix at {Path}", _paths.WinePrefix);

            // 2. Create Prefix
            if (!Directory.Exists(_paths.PrefixDriveC))
            {
                events.Add(new WineInitProgress
                {
                    Stage = WineInitStage.CreatingPrefix,
                    MessageKey = "progress.creating_prefix"
                });
                _logger?.LogInformation("[WINE-INIT] Stage 2: Creating Wine Prefix");
                await EnsurePrefixAsync(cancellationToken);
            }

            // 3. Install fonts
            var fontPath = Path.Combine(_paths.PrefixFonts, _paths.FontFile);
            if (!File.Exists(fontPath))
            {
                events.Add(new WineInitProgress
                {
                    Stage = WineInitStage.InstallingFonts,
                    MessageKey = "progress.installing_fonts"
                });
                _logger?.LogInformation("[WINE-INIT] Stage 3: Installing fonts");
                await InstallFontIfNeededAsync(cancellationToken);
            }

            // 4. Set locale
            events.Add(new WineInitProgress
            {
                Stage = WineInitStage.SettingLocale,
                MessageKey = "progress.setting_locale"
            });
            _logger?.LogInformation("[WINE-INIT] Stage 4: Setting locale to zh-TW");
            await SetLocaleToZhTWAsync(cancellationToken);

            // 5. Configure MediaFoundation
            events.Add(new WineInitProgress
            {
                Stage = WineInitStage.ConfiguringMedia,
                MessageKey = "progress.configuring_media"
            });
            _logger?.LogInformation("[WINE-INIT] Stage 5: Configuring MediaFoundation");
            await ConfigureMediaFoundationAsync(cancellationToken);

            // 6. Install graphics backend
            events.Add(new WineInitProgress
            {
                Stage = WineInitStage.ConfiguringMedia,
                MessageKey = "progress.installing_graphics"
            });
            _logger?.LogInformation("[WINE-INIT] Stage 6: Installing graphics backend");
            
            var defaultConfig = new WineConfig
            {
                DxmtEnabled = true,
                NativeResolution = false,
                LeftOptionIsAlt = true,
                RightOptionIsAlt = true,
                LeftCommandIsCtrl = false,
                RightCommandIsCtrl = false
            };
            await ApplyGraphicsSettingsAsync(defaultConfig, cancellationToken);

            // 7. Complete
            finalEvent = new WineInitProgress
            {
                Stage = WineInitStage.Complete,
                MessageKey = "progress.complete",
                IsComplete = true
            };

            _logger?.LogInformation("[WINE-INIT] ========== Completed Successfully ==========");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[WINE-INIT] ========== FAILED ==========");

            finalEvent = new WineInitProgress
            {
                HasError = true,
                ErrorMessageKey = "error.init_failed",
                ErrorParams = new Dictionary<string, object>
                {
                    { "message", ex.Message }
                }
            };
        }
        
        // Yield all collected events
        foreach (var evt in events)
        {
            yield return evt;
        }
        
        if (finalEvent != null)
        {
            yield return finalEvent;
        }
    }

    /// <summary>
    /// Initialize Wine Prefix with progress reporting
    /// LEGACY: Kept for backward compatibility, will be removed in Phase 6
    /// </summary>
    public async Task InitializePrefixAsync(
        IProgress<WineInitProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("[WINE-INIT] ========== Wine Prefix Initialization Started ==========");

        try
        {
            // 0. Check if already fully initialized
            _logger?.LogDebug("[WINE-INIT] Checking if already fully initialized...");
            if (IsFullyInitialized())
            {
                _logger?.LogInformation("[WINE-INIT] Wine Prefix already fully initialized, skipping");
                progress?.Report(new WineInitProgress
                {
                    Stage = WineInitStage.Complete,
                    MessageKey = "progress.already_initialized",
                    IsComplete = true
                });
                _logger?.LogInformation("[WINE-INIT] ========== Completed (Already Initialized) ==========");
                return;
            }
            
            _logger?.LogInformation("[WINE-INIT] Not fully initialized, proceeding with initialization");

            // 1. Check Prefix
            progress?.Report(new WineInitProgress
            {
                Stage = WineInitStage.Checking,
                MessageKey = "progress.checking"
            });
            _logger?.LogDebug("[WINE-INIT] Stage 1: Checking Wine Prefix at {Path}", _paths.WinePrefix);

            // 2. Create Prefix (fast, synchronous)
            if (!Directory.Exists(_paths.PrefixDriveC))
            {
                progress?.Report(new WineInitProgress
                {
                    Stage = WineInitStage.CreatingPrefix,
                    MessageKey = "progress.creating_prefix"
                });
                _logger?.LogInformation("[WINE-INIT] Stage 2: Creating Wine Prefix (drive_c not found)");
                await EnsurePrefixAsync(cancellationToken);
                _logger?.LogInformation("[WINE-INIT] Prefix created successfully");
            }
            else
            {
                _logger?.LogInformation("[WINE-INIT] Stage 2: Wine Prefix already exists, checking configuration");
            }

            // 3. Install fonts (synchronous, required before game launch)
            // Check if font exists before installing
            var fontPath = Path.Combine(_paths.PrefixFonts, _paths.FontFile);
            _logger?.LogDebug("[WINE-INIT] Stage 3: Checking font at {FontPath}", fontPath);
            if (!File.Exists(fontPath))
            {
                progress?.Report(new WineInitProgress
                {
                    Stage = WineInitStage.InstallingFonts,
                    MessageKey = "progress.installing_fonts"
                });
                _logger?.LogInformation("[WINE-INIT] Installing fonts (not found)");
                await InstallFontIfNeededAsync(cancellationToken);
                _logger?.LogInformation("[WINE-INIT] Fonts installed successfully");
            }
            else
            {
                _logger?.LogInformation("[WINE-INIT] Fonts already installed, skipping");
            }

            // 4. Set locale (synchronous, required before game launch)
            progress?.Report(new WineInitProgress
            {
                Stage = WineInitStage.SettingLocale,
                MessageKey = "progress.setting_locale"
            });
            _logger?.LogInformation("[WINE-INIT] Stage 4: Setting locale to zh-TW");
            await SetLocaleToZhTWAsync(cancellationToken);
            _logger?.LogInformation("[WINE-INIT] Locale set successfully");

            // 5. Configure MediaFoundation (fast file operation, synchronous)
            progress?.Report(new WineInitProgress
            {
                Stage = WineInitStage.ConfiguringMedia,
                MessageKey = "progress.configuring_media"
            });
            _logger?.LogInformation("[WINE-INIT] Stage 5: Configuring MediaFoundation");
            await ConfigureMediaFoundationAsync(cancellationToken);
            _logger?.LogInformation("[WINE-INIT] MediaFoundation configured successfully");

            // 6. Install graphics backend and configure Mac Driver
            progress?.Report(new WineInitProgress
            {
                Stage = WineInitStage.ConfiguringMedia, // Reuse stage for now
                MessageKey = "progress.installing_graphics"
            });
            _logger?.LogInformation("[WINE-INIT] Stage 6: Installing graphics backend and Mac Driver settings");
            
            // Use default config for first init
            var defaultConfig = new WineConfig
            {
                DxmtEnabled = true,
                NativeResolution = false, // Use scaling by default for better performance
                LeftOptionIsAlt = true,
                RightOptionIsAlt = true,
                LeftCommandIsCtrl = false,
                RightCommandIsCtrl = false
            };
            await ApplyGraphicsSettingsAsync(defaultConfig, cancellationToken);
            _logger?.LogInformation("[WINE-INIT] Graphics backend and Mac Driver configured successfully");

            // 7. Complete initialization
            progress?.Report(new WineInitProgress
            {
                Stage = WineInitStage.Complete,
                MessageKey = "progress.complete",
                IsComplete = true
            });

            _logger?.LogInformation("[WINE-INIT] ========== Completed Successfully ==========");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[WINE-INIT] ========== FAILED ==========");

            progress?.Report(new WineInitProgress
            {
                HasError = true,
                ErrorMessageKey = "error.init_failed",
                ErrorParams = new Dictionary<string, object>
                {
                    { "message", ex.Message }
                }
            });

            throw;
        }
    }

    /// <summary>
    /// 套用圖形設定變更
    /// 當 Wine 設定（如 DXMT 開關）變更時呼叫
    /// </summary>
    public async Task ApplyGraphicsSettingsAsync(WineConfig config, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("[WINE-SETTINGS] Applying graphics settings (DXMT={DxmtEnabled})", config.DxmtEnabled);
        
        try
        {
            // Install DLLs
            _graphicsInstaller.EnsureBackend(config.DxmtEnabled);
            
            // Configure Mac Driver (Retina mode, keyboard mapping)
            await ConfigureMacDriverAsync(config, cancellationToken);
            
            _logger?.LogInformation("[WINE-SETTINGS] Graphics settings applied successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[WINE-SETTINGS] Failed to apply graphics settings");
            throw;
        }
    }

    /// <summary>
    /// Configure Wine MediaFoundation for video playback support
    /// Fast version: Direct registry file manipulation instead of wine64 execution
    /// Reference: XoM Wine.swift configureMediaFoundation() (Lines 226-242)
    /// </summary>
    private async Task ConfigureMediaFoundationAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Configuring MediaFoundation with GStreamer support (fast mode)");

        try
        {
            // Direct registry file manipulation - much faster than calling wine64
            var userRegPath = Path.Combine(_paths.WinePrefix, "user.reg");
            
            if (!File.Exists(userRegPath))
            {
                _logger?.LogWarning("user.reg not found, creating minimal registry");
                await CreateMinimalUserRegistryAsync(userRegPath, cancellationToken);
            }

            // Read registry file
            var content = await File.ReadAllTextAsync(userRegPath, cancellationToken);
            
            // Add DllOverrides section if not exists
            if (!content.Contains("[Software\\\\Wine\\\\DllOverrides]"))
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var dllOverrides = $"\n[Software\\\\Wine\\\\DllOverrides] {timestamp}\n" +
                    $"#time=1dc904b5ea27e4e\n" +
                    $"\"winegstreamer\"=\"native,builtin\"\n" +
                    $"\"mfplat\"=\"native,builtin\"\n" +
                    $"\"mf\"=\"native,builtin\"\n" +
                    $"\"mfreadwrite\"=\"native,builtin\"\n";
                content += dllOverrides;
            }
            else
            {
                // Update existing entries - find the section and add missing entries
                var dllOverridesIndex = content.IndexOf("[Software\\\\Wine\\\\DllOverrides]");
                var nextSectionIndex = content.IndexOf("\n[", dllOverridesIndex + 1);
                var sectionEnd = nextSectionIndex > 0 ? nextSectionIndex : content.Length;
                var dllOverridesSection = content.Substring(dllOverridesIndex, sectionEnd - dllOverridesIndex);
                
                var missingEntries = new List<string>();
                if (!content.Contains("\"winegstreamer\"="))
                    missingEntries.Add("\"winegstreamer\"=\"native,builtin\"");
                if (!content.Contains("\"mfplat\"="))
                    missingEntries.Add("\"mfplat\"=\"native,builtin\"");
                if (!content.Contains("\"mf\"=") || content.Contains("\"mfplat\"="))
                {
                    // Only add "mf" if it's truly missing, not just part of "mfplat"
                    if (!System.Text.RegularExpressions.Regex.IsMatch(content, "\"mf\"=(?!plat)"))
                        missingEntries.Add("\"mf\"=\"native,builtin\"");
                }
                if (!content.Contains("\"mfreadwrite\"="))
                    missingEntries.Add("\"mfreadwrite\"=\"native,builtin\"");
                
                if (missingEntries.Count > 0)
                {
                    var insertPoint = dllOverridesIndex + "[Software\\\\Wine\\\\DllOverrides]".Length;
                    var firstLineBreak = content.IndexOf('\n', insertPoint);
                    if (firstLineBreak > 0)
                    {
                        var newEntries = "\n" + string.Join("\n", missingEntries);
                        content = content.Insert(firstLineBreak + 1, newEntries);
                    }
                }
            }

            // Add MediaFoundation section if not exists
            if (!content.Contains("[Software\\\\Wine\\\\MediaFoundation]"))
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var mediaFoundation = $"\n[Software\\\\Wine\\\\MediaFoundation] {timestamp}\n" +
                    $"#time=1dc904b614e67fc\n" +
                    $"\"DisableGstByteStreamHandler\"=\"0\"\n";
                content += mediaFoundation;
            }

            // Write back to file
            await File.WriteAllTextAsync(userRegPath, content, cancellationToken);
            
            _logger?.LogInformation("MediaFoundation configured successfully (fast mode)");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to configure MediaFoundation");
            throw;
        }
    }

    /// <summary>
    /// Configure Mac Driver settings (Retina mode, keyboard mapping)
    /// 參考 XoM Wine.swift - Wine Registry 中的 Mac Driver 設定
    /// </summary>
    public async Task ConfigureMacDriverAsync(WineConfig config, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Configuring Mac Driver settings");

        try
        {
            const string macDriverKey = "HKEY_CURRENT_USER\\Software\\Wine\\Mac Driver";
            
            await BeginBatchAsync(cancellationToken);
            
            // Retina mode: NativeResolution=true → RetinaMode=y
            await AddRegAsync(macDriverKey, "RetinaMode", config.NativeResolution ? "y" : "n", cancellationToken);
            
            // Keyboard mapping
            await AddRegAsync(macDriverKey, "LeftOptionIsAlt", config.LeftOptionIsAlt ? "y" : "n", cancellationToken);
            await AddRegAsync(macDriverKey, "RightOptionIsAlt", config.RightOptionIsAlt ? "y" : "n", cancellationToken);
            await AddRegAsync(macDriverKey, "LeftCommandIsCtrl", config.LeftCommandIsCtrl ? "y" : "n", cancellationToken);
            await AddRegAsync(macDriverKey, "RightCommandIsCtrl", config.RightCommandIsCtrl ? "y" : "n", cancellationToken);
            
            await CommitBatchAsync(cancellationToken);
            
            _logger?.LogInformation("Mac Driver configured: RetinaMode={RetinaMode}", config.NativeResolution ? "y" : "n");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to configure Mac Driver");
            throw;
        }
    }

    /// <summary>
    /// Create minimal user registry file
    /// </summary>
    private async Task CreateMinimalUserRegistryAsync(string path, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var minimalRegistry = $"WINE REGISTRY Version 2\n" +
            $";; All keys relative to \\\\User\\\\S-1-5-21-0-0-0-1000\n\n" +
            $"[Software\\\\Wine] {timestamp}\n" +
            $"#time=1dc904b55ea0d62\n" +
            $"\"Version\"=\"wine-10.0\"\n\n";
        
        await File.WriteAllTextAsync(path, minimalRegistry, cancellationToken);
        _logger?.LogInformation("Created minimal user registry");
    }

    /// <summary>
    /// 檢查 Wine 是否已安裝
    /// </summary>
    public bool IsWineInstalled()
    {
        return File.Exists(_paths.Wine) && File.Exists(_paths.WineExecutable);
    }

    /// <summary>
    /// 檢查 Prefix 是否已初始化
    /// </summary>
    public bool IsPrefixInitialized()
    {
        return Directory.Exists(_paths.PrefixDriveC) &&
               Directory.Exists(_paths.PrefixWindows) &&
               Directory.Exists(_paths.PrefixSystem32);
    }

    // ========================================
    // Registry Service Methods (from WineRegistryService)
    // ========================================

    /// <summary>
    /// Start batch mode (recommended for initialization)
    /// </summary>
    public async Task BeginBatchAsync(CancellationToken cancellationToken = default)
    {
        if (_inBatch)
        {
            throw new InvalidOperationException("Already in batch mode");
        }

        _batchFile = new WineRegistryFile();
        var userRegPath = Path.Combine(_paths.WinePrefix, "user.reg");
        await _batchFile.LoadAsync(userRegPath, cancellationToken);
        _inBatch = true;
        
        _logger?.LogInformation("[Wine Registry] Batch mode started (fast mode)");
    }

    /// <summary>
    /// Commit batch operations (write to file)
    /// </summary>
    public async Task CommitBatchAsync(CancellationToken cancellationToken = default)
    {
        if (!_inBatch || _batchFile is null)
        {
            throw new InvalidOperationException("Not in batch mode");
        }

        await _batchFile.SaveAsync(cancellationToken);
        _batchFile = null;
        _inBatch = false;
        
        _logger?.LogInformation("[Wine Registry] Batch committed successfully");
    }

    /// <summary>
    /// Add string registry value (REG_SZ)
    /// Batch mode: in-memory operation (fast)
    /// Non-batch mode: direct file operation (medium speed)
    /// </summary>
    public async Task AddRegAsync(string key, string value, string data, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_inBatch && _batchFile is not null)
            {
                // Batch mode: add to memory (<1ms)
                _batchFile.SetValue(key, value, data, RegistryValueType.String);
            }
            else
            {
                // Non-batch mode: direct file operation (~25ms)
                var file = new WineRegistryFile();
                var userRegPath = Path.Combine(_paths.WinePrefix, "user.reg");
                await file.LoadAsync(userRegPath, cancellationToken);
                file.SetValue(key, value, data, RegistryValueType.String);
                await file.SaveAsync(cancellationToken);
            }
            
            _logger?.LogDebug("[Wine Registry] Added: {Key}\\{Value} = {Data}", key, value, data);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Wine Registry] Failed to add {Key}\\{Value}", key, value);
            throw;
        }
    }

    /// <summary>
    /// Add DWORD registry value (REG_DWORD)
    /// </summary>
    public async Task AddRegDwordAsync(string key, string value, uint data, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_inBatch && _batchFile is not null)
            {
                // Batch mode: add to memory
                _batchFile.SetValue(key, value, $"{data:x8}", RegistryValueType.DWord);
            }
            else
            {
                // Non-batch mode: direct file operation
                var file = new WineRegistryFile();
                var userRegPath = Path.Combine(_paths.WinePrefix, "user.reg");
                await file.LoadAsync(userRegPath, cancellationToken);
                file.SetValue(key, value, $"{data:x8}", RegistryValueType.DWord);
                await file.SaveAsync(cancellationToken);
            }
            
            _logger?.LogDebug("[Wine Registry] Added DWORD: {Key}\\{Value} = {Data}", key, value, data);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Wine Registry] Failed to add DWORD {Key}\\{Value}", key, value);
            throw;
        }
    }

    /// <summary>
    /// Add binary registry value (REG_BINARY)
    /// </summary>
    public async Task AddRegBinaryAsync(string key, string value, string hexData, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_inBatch && _batchFile is not null)
            {
                // Batch mode: add to memory
                _batchFile.SetValue(key, value, hexData, RegistryValueType.Binary);
            }
            else
            {
                // Non-batch mode: direct file operation
                var file = new WineRegistryFile();
                var userRegPath = Path.Combine(_paths.WinePrefix, "user.reg");
                await file.LoadAsync(userRegPath, cancellationToken);
                file.SetValue(key, value, hexData, RegistryValueType.Binary);
                await file.SaveAsync(cancellationToken);
            }
            
            _logger?.LogDebug("[Wine Registry] Added BINARY: {Key}\\{Value}", key, value);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Wine Registry] Failed to add BINARY {Key}\\{Value}", key, value);
            throw;
        }
    }

    /// <summary>
    /// 設定 DLL Override
    /// 參考 XoM Wine.swift override(dll:type:)
    /// </summary>
    public async Task SetDllOverrideAsync(string dll, string type, CancellationToken cancellationToken = default)
    {
        const string key = @"HKEY_CURRENT_USER\Software\Wine\DllOverrides";
        
        // 移除 .dll 副檔名（Wine 註冊表不需要）
        var dllName = dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? dll[..^4]
            : dll;

        try
        {
            await AddRegAsync(key, dllName, type, cancellationToken);
            _logger?.LogInformation("[Wine Registry] DLL Override: {DllName} = {Type}", dllName, type);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Wine Registry] Failed to set DLL override for {DllName}", dllName);
            throw;
        }
    }

    // ========================================
    // Font Service Methods (from WineFontService)
    // ========================================

    /// <summary>
    /// Install Noto Sans TC font if not already installed
    /// Based on XoM Wine.swift installFontIfNeeded()
    /// </summary>
    public async Task InstallFontIfNeededAsync(CancellationToken cancellationToken = default)
    {
        var fontFile = _paths.FontFile;
        var targetFontPath = Path.Combine(_paths.PrefixFonts, fontFile);

        try
        {
            if (File.Exists(targetFontPath))
            {
                _logger?.LogInformation("[Wine Fonts] Font already installed: {FontFile}", fontFile);
                
                // 即使字體已安裝，也要配置註冊表（使用快速批處理模式）
                await BeginBatchAsync(cancellationToken);
                
                // 在 Wine 註冊表中註冊字體
                await AddRegAsync(
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Fonts",
                    $"{_paths.FontName} (TrueType)",
                    fontFile,
                    cancellationToken);

                // 設定字體替換和連結
                await ConfigureFontSubstitutionAndLinkingAsync(cancellationToken);
                
                await CommitBatchAsync(cancellationToken);
                _logger?.LogInformation("[Wine Fonts] Font configuration updated in batch mode");
                return;
            }

            // 取得字體來源路徑
            var fontSourcePath = GetFontSourcePath();

            // 確認來源檔案存在
            if (!File.Exists(fontSourcePath))
            {
                throw new FileNotFoundException($"Font source file not found: {fontSourcePath}");
            }

            _logger?.LogInformation("[Wine Fonts] Font source found: {FontSourcePath}", fontSourcePath);

            // 確保目錄存在
            Directory.CreateDirectory(_paths.PrefixFonts);

            // 複製字體檔案
            File.Copy(fontSourcePath, targetFontPath, overwrite: true);
            _logger?.LogInformation("[Wine Fonts] Font copied to: {TargetFontPath}", targetFontPath);

            // 設定檔案權限為 644 (rw-r--r--)
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(targetFontPath, 
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | 
                    UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            // 使用批處理模式配置所有註冊表項（極速）
            await BeginBatchAsync(cancellationToken);
            
            // 在 Wine 註冊表中註冊字體
            await AddRegAsync(
                @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Fonts",
                $"{_paths.FontName} (TrueType)",
                fontFile,
                cancellationToken);

            // 設定字體替換和連結
            await ConfigureFontSubstitutionAndLinkingAsync(cancellationToken);
            
            await CommitBatchAsync(cancellationToken);

            _logger?.LogInformation("[Wine Fonts] Font installed successfully: {FontFile}", fontFile);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Wine Fonts] Failed to install font");
            throw;
        }
    }

    /// <summary>
    /// 取得字體來源路徑
    /// </summary>
    private string GetFontSourcePath()
    {
        var appDir = AppContext.BaseDirectory;
        _logger?.LogDebug("[Wine Fonts] Looking for font from base directory: {AppDir}", appDir);
        
        // 方案 1：開發環境 - shared/resources/fonts/
        // AppContext.BaseDirectory: .../XIVTheCalamity.Api/bin/Debug/net9.0/
        // 往上找到專案根目錄的 shared/resources/fonts/
        var currentDir = new DirectoryInfo(appDir);
        while (currentDir is not null && currentDir.Parent is not null)
        {
            var sharedPath = Path.Combine(currentDir.FullName, "shared", "resources", "fonts", _paths.FontFile);
            _logger?.LogDebug("[Wine Fonts] Checking: {SharedPath}", sharedPath);
            if (File.Exists(sharedPath))
            {
                _logger?.LogInformation("[Wine Fonts] Found in development: {SharedPath}", sharedPath);
                return sharedPath;
            }
            currentDir = currentDir.Parent;
        }

        // 方案 2：打包後的 macOS app bundle
        // XIVTheCalamity.app/Contents/Resources/resources/fonts/
        var bundleResourcesPath = Path.Combine(appDir, "..", "..", "Resources", "resources", "fonts", _paths.FontFile);
        bundleResourcesPath = Path.GetFullPath(bundleResourcesPath);
        _logger?.LogDebug("[Wine Fonts] Checking bundle path: {BundleResourcesPath}", bundleResourcesPath);
        if (File.Exists(bundleResourcesPath))
        {
            _logger?.LogInformation("[Wine Fonts] Found in bundle: {BundleResourcesPath}", bundleResourcesPath);
            return bundleResourcesPath;
        }

        // 方案 3：發布後的 Linux/通用部署
        // 與可執行文件同級的 resources/fonts/
        var deployedPath = Path.Combine(appDir, "resources", "fonts", _paths.FontFile);
        _logger?.LogDebug("[Wine Fonts] Checking deployed path: {DeployedPath}", deployedPath);
        if (File.Exists(deployedPath))
        {
            _logger?.LogInformation("[Wine Fonts] Found in deployed: {DeployedPath}", deployedPath);
            return Path.GetFullPath(deployedPath);
        }

        var errorMsg = $"Font file not found: {_paths.FontFile}\n" +
                      $"Base directory: {appDir}\n" +
                      $"Please ensure font file exists in shared/resources/fonts/";
        _logger?.LogError("[Wine Fonts] ERROR: {ErrorMsg}", errorMsg);
        throw new FileNotFoundException(errorMsg);
    }

    /// <summary>
    /// Configure font substitution and linking
    /// Based on XoM Wine.swift configureFontSubstitutionAndLinking()
    /// </summary>
    private async Task ConfigureFontSubstitutionAndLinkingAsync(CancellationToken cancellationToken = default)
    {
        var fontName = _paths.FontName;
        var fontFile = _paths.FontFile;

        try
        {
            // Note: This method runs in batch mode, all operations are in-memory (fast)
            
            // 1. Wine font replacements
            _logger?.LogDebug("[Wine Fonts] Configuring font replacements");
            const string wineReplacementKey = @"HKEY_CURRENT_USER\Software\Wine\Fonts\Replacements";

            var replacements = new[]
            {
                "MS Shell Dlg", "MS Shell Dlg 2", "MS Sans Serif",
                "Microsoft Sans Serif", "Tahoma", "Segoe UI", "Arial", "Courier New"
            };

            foreach (var replacement in replacements)
            {
                await AddRegAsync(wineReplacementKey, replacement, fontName, cancellationToken);
            }

            // 2. Font linking
            _logger?.LogDebug("[Wine Fonts] Configuring font linking");
            const string linkKey = @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\FontLink\SystemLink";
            var fallbackValue = $"{fontFile},{fontName}";

            var linkFonts = new[]
            {
                "Tahoma", "Microsoft Sans Serif", "MS Sans Serif",
                "Lucida Sans Unicode", "Arial"
            };

            foreach (var font in linkFonts)
            {
                await AddRegAsync(linkKey, font, fallbackValue, cancellationToken);
            }

            // 3. Set system locale (zh-TW, 0404)
            _logger?.LogDebug("[Wine Fonts] Configuring system locale");
            const string nlsKey = @"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Nls\Language";

            await AddRegAsync(nlsKey, "InstallLanguage", "0404", cancellationToken);
            await AddRegAsync(nlsKey, "Default", "0404", cancellationToken);

            _logger?.LogInformation("[Wine Fonts] Font substitution and linking configured");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Wine Fonts] Failed to configure font substitution");
            throw;
        }
    }

    /// <summary>
    /// Set Wine locale to zh-TW
    /// Based on XoM Wine.swift setLocaleToZhTW()
    /// </summary>
    public async Task SetLocaleToZhTWAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("[Wine Fonts] Setting locale to zh-TW");

        try
        {
            // 使用批處理模式（快速）
            await BeginBatchAsync(cancellationToken);
            
            const string key = @"HKEY_CURRENT_USER\Control Panel\International";

            // 設定區域為繁體中文-台灣 (0404 = zh-TW)
            await AddRegAsync(key, "Locale", "00000404", cancellationToken);
            await AddRegAsync(key, "LocaleName", "zh-TW", cancellationToken);
            await AddRegAsync(key, "sLanguage", "CHT", cancellationToken);
            await AddRegAsync(key, "sCountry", "Taiwan", cancellationToken);

            await CommitBatchAsync(cancellationToken);
            
            _logger?.LogInformation("[Wine Fonts] Locale set to zh-TW successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Wine Fonts] Failed to set locale");
            throw;
        }
    }

    // ========================================
    // Internal Registry File Class (from WineRegistryFile)
    // ========================================

    /// <summary>
    /// Wine 注册表文件操作类
    /// 直接读写 .reg 文本文件，避免启动 wine reg.exe 进程
    /// 性能提升：从 ~300ms/次 降到 ~25ms/批次（约 168 倍提升）
    /// </summary>
    private class WineRegistryFile
    {
        private readonly Dictionary<string, RegistryKey> _keys = new();
        private string _filePath = string.Empty;

        /// <summary>
        /// 加载 .reg 文件
        /// </summary>
        public async Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            _filePath = filePath;

            if (!File.Exists(filePath))
            {
                CreateMinimalRegistry();
                return;
            }

            try
            {
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                ParseRegistryContent(content);
            }
            catch (Exception)
            {
                CreateMinimalRegistry();
            }
        }

        /// <summary>
        /// 设置注册表值（内存操作）
        /// </summary>
        public void SetValue(string keyPath, string valueName, string data, RegistryValueType type = RegistryValueType.String)
        {
            var normalizedPath = NormalizeKeyPath(keyPath);

            if (!_keys.ContainsKey(normalizedPath))
            {
                _keys[normalizedPath] = new RegistryKey(normalizedPath);
            }

            _keys[normalizedPath].SetValue(valueName, data, type);
        }

        /// <summary>
        /// 保存到 .reg 文件
        /// </summary>
        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();
            sb.AppendLine("WINE REGISTRY Version 2");
            sb.AppendLine(";; All keys relative to \\\\User\\\\S-1-5-21-0-0-0-1000");
            sb.AppendLine();

            foreach (var (path, key) in _keys.OrderBy(k => k.Key))
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                sb.AppendLine($"[{path}] {timestamp}");
                sb.AppendLine($"#time={timestamp:x}");

                foreach (var (name, value, valueType) in key.Values)
                {
                    sb.AppendLine(FormatValue(name, value, valueType));
                }

                sb.AppendLine();
            }

            await File.WriteAllTextAsync(_filePath, sb.ToString(), cancellationToken);
        }

        /// <summary>
        /// 规范化注册表键路径
        /// HKEY_CURRENT_USER\Software\Wine → Software\\Wine
        /// HKEY_LOCAL_MACHINE\Software\... → Software\\... (user.reg 中也存储在 User 下)
        /// </summary>
        private string NormalizeKeyPath(string keyPath)
        {
            // 移除 HKEY_ 前缀
            var path = keyPath
                .Replace("HKEY_CURRENT_USER\\", "")
                .Replace("HKEY_LOCAL_MACHINE\\", "");

            // 转换单反斜杠为双反斜杠（.reg 文件格式）
            path = path.Replace("\\", "\\\\");

            return path;
        }

        /// <summary>
        /// 解析 .reg 文件内容
        /// </summary>
        private void ParseRegistryContent(string content)
        {
            // 匹配键：[Software\\Wine] 1234567890
            var keyRegex = new Regex(@"^\[(.+?)\]\s*\d*$", RegexOptions.Multiline);
            
            // 匹配值："Name"="Value" 或 "Name"=dword:00000001
            var valueRegex = new Regex(@"^""([^""]+)""=(.+)$", RegexOptions.Multiline);

            RegistryKey? currentKey = null;

            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();

                // 跳过空行和注释
                if (string.IsNullOrWhiteSpace(trimmed) || 
                    trimmed.StartsWith(";") || 
                    trimmed.StartsWith("#time=") ||
                    trimmed.StartsWith("WINE REGISTRY"))
                {
                    continue;
                }

                // 解析键
                var keyMatch = keyRegex.Match(trimmed);
                if (keyMatch.Success)
                {
                    var keyPath = keyMatch.Groups[1].Value;
                    currentKey = new RegistryKey(keyPath);
                    _keys[keyPath] = currentKey;
                    continue;
                }

                // 解析值
                if (currentKey is not null)
                {
                    var valueMatch = valueRegex.Match(trimmed);
                    if (valueMatch.Success)
                    {
                        var name = valueMatch.Groups[1].Value;
                        var valueStr = valueMatch.Groups[2].Value;

                        // 解析值类型和数据
                        if (valueStr.StartsWith("dword:"))
                        {
                            var hexValue = valueStr.Substring(6); // 移除 "dword:"
                            currentKey.SetValue(name, hexValue, RegistryValueType.DWord);
                        }
                        else if (valueStr.StartsWith("\"") && valueStr.EndsWith("\""))
                        {
                            var data = valueStr.Substring(1, valueStr.Length - 2); // 移除引号
                            currentKey.SetValue(name, UnescapeValue(data), RegistryValueType.String);
                        }
                        else if (valueStr.StartsWith("hex:"))
                        {
                            var hexData = valueStr.Substring(4); // 移除 "hex:"
                            currentKey.SetValue(name, hexData, RegistryValueType.Binary);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 格式化注册表值
        /// </summary>
        private string FormatValue(string name, string data, RegistryValueType type)
        {
            return type switch
            {
                RegistryValueType.String => $"\"{name}\"=\"{EscapeValue(data)}\"",
                RegistryValueType.DWord => $"\"{name}\"=dword:{data}",  // data 已經是 hex 格式
                RegistryValueType.Binary => $"\"{name}\"=hex:{data}",
                _ => $"\"{name}\"=\"{EscapeValue(data)}\""
            };
        }

        /// <summary>
        /// 转义值中的特殊字符
        /// </summary>
        private string EscapeValue(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        /// <summary>
        /// 反转义值中的特殊字符
        /// </summary>
        private string UnescapeValue(string value)
        {
            return value
                .Replace("\\\\", "\\")
                .Replace("\\\"", "\"");
        }

        /// <summary>
        /// 创建最小注册表结构
        /// </summary>
        private void CreateMinimalRegistry()
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var wineKey = new RegistryKey("Software\\\\Wine");
            wineKey.SetValue("Version", "wine-10.0", RegistryValueType.String);
            _keys["Software\\\\Wine"] = wineKey;
        }
    }

    /// <summary>
    /// 注册表键
    /// </summary>
    private class RegistryKey
    {
        public string Path { get; }
        private readonly Dictionary<string, (string Value, RegistryValueType Type)> _values = new();

        public RegistryKey(string path)
        {
            Path = path;
        }

        public void SetValue(string name, string value, RegistryValueType type)
        {
            _values[name] = (value, type);
        }

        public IEnumerable<(string Name, string Value, RegistryValueType Type)> Values =>
            _values.Select(kvp => (kvp.Key, kvp.Value.Value, kvp.Value.Type));
    }

    /// <summary>
    /// 注册表值类型
    /// </summary>
    private enum RegistryValueType
    {
        String,
        DWord,
        Binary
    }
}
