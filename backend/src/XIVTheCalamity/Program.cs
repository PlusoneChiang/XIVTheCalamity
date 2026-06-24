using Serilog;
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Photino.NET;
using XIVTheCalamity.Endpoints;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform.MacOS.Wine;
using XIVTheCalamity.Platform.MacOS.Discord;
using XIVTheCalamity.Game.Services;
using XIVTheCalamity.Game.Launcher;
using XIVTheCalamity.Game.Authentication;
using XIVTheCalamity.Dalamud.Services;
using XIVTheCalamity.Dalamud.Interfaces;
using XIVTheCalamity.Services;

namespace XIVTheCalamity;

public class Program
{
    private static WebApplication? _webApp;

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();

    [STAThread]
    public static void Main(string[] args)
    {
        var port = 5050;
        
        // 1. 配置日誌 (Configure Serilog)
        var logPath = GetLogFilePath();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Starting XIVTheCalamity with Photino.NET (NativeAOT)");

            // 2. 載入配置並決定是否啟用 Home Alias (僅 macOS 支援)
            PrepareEnvironment();

            // 3. 於背景非同步啟動 Kestrel 伺服器 (Hosting Kestrel + Static files)
            MainWindowContainer.Port = port;
            StartKestrelServer(port, args, logPath);

            // 4. 初始化並載入 Photino 視窗 UI
            string platformStr = "linux";
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                platformStr = "darwin";
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                platformStr = "win32";

            // VirtualHost：使用自訂 scheme "xivtc" 模擬 Electron 的 baseURLForDataURL 功能
            // macOS WKWebView 禁止攔截 https 等內建 scheme，必須使用自訂 scheme
            // 載入 xivtc://user.ffxiv.com.tw/login.html 使 window.location.hostname = "user.ffxiv.com.tw"
            // 讓 reCAPTCHA 驗證正確通過（與 Electron 版本行為完全相同）
            // API 呼叫由 polyfill 直接使用 http://localhost:{port}，不透過此 handler
            var virtualHostClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

            var window = new PhotinoWindow()
                .SetTitle("XIV The Calamity")
                .SetSize(910, 714) // 910 width, 682 content size + 32px macOS titlebar height
                .SetUseOsDefaultSize(false)
                .SetResizable(false)
                .SetDevToolsEnabled(true)
                .SetContextMenuEnabled(true)
                .SetWebSecurityEnabled(false)
                .RegisterCustomSchemeHandler("xivtc", (object sender, string scheme, string request, out string contentType) =>
                {
                    // 將 xivtc://user.ffxiv.com.tw/{path} 代理到 Kestrel 的靜態檔案
                    // 只處理靜態頁面資源（HTML/CSS/JS），API 請求由 polyfill 直接走 http://localhost
                    try
                    {
                        var uri = new Uri(request);
                        var pathAndQuery = uri.PathAndQuery; // e.g. "/login.html?platform=darwin"

                        Log.Debug("[VirtualHost] xivtc:// proxy: {PathAndQuery} -> Kestrel", pathAndQuery);

                        var response = virtualHostClient.GetAsync(pathAndQuery).GetAwaiter().GetResult();
                        contentType = response.Content.Headers.ContentType?.MediaType ?? "text/html";
                        return response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[VirtualHost] Failed to proxy xivtc request: {Request}", request);
                        contentType = "text/html";
                        return System.IO.Stream.Null;
                    }
                })
                .Center()
                .Load(new Uri($"xivtc://user.ffxiv.com.tw/login.html?platform={platformStr}"));

            MainWindowContainer.MainWindow = window;

            // 監聽視窗關閉以優雅停止 Kestrel 伺服器
            window.WindowClosing += (sender, e) =>
            {
                if (_webApp != null)
                {
                    _ = _webApp.StopAsync();
                }
                return false; // 允許關閉視窗
            };

            // 啟動主訊息循環 (會阻塞當前主執行緒)
            window.WaitForClose();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void PrepareEnvironment()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        var appSupport = GetAppSupportPath();
        var configPath = Path.Combine(appSupport, "XIVTheCalamity", "config.json");
        bool useHomeAlias = false;

        if (File.Exists(configPath))
        {
            try
            {
                var configJson = File.ReadAllText(configPath);
                var config = System.Text.Json.JsonSerializer.Deserialize(configJson, AppJsonContext.Default.AppConfig);
                useHomeAlias = config?.Wine?.UseHomeAlias ?? false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[HomeAlias] Failed to read config to check UseHomeAlias");
            }
        }

        if (useHomeAlias)
        {
            EnsureHomeAliasSymlink();
        }
    }

    private static void EnsureHomeAliasSymlink()
    {
        try
        {
            var realHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            uint uid = GetUid();
            var aliasPath = Path.Combine("/tmp", $"xivtc-home-{uid}");

            bool shouldRecreate = true;

            if (Path.Exists(aliasPath))
            {
                try
                {
                    var target = File.ResolveLinkTarget(aliasPath, true);
                    if (target != null && target.FullName == realHome)
                    {
                        shouldRecreate = false;
                    }
                }
                catch
                {
                    // 解析失敗，準備重新建立
                }
            }

            if (shouldRecreate)
            {
                if (Path.Exists(aliasPath))
                {
                    try
                    {
                        File.Delete(aliasPath);
                    }
                    catch
                    {
                        Directory.Delete(aliasPath, true);
                    }
                }

                File.CreateSymbolicLink(aliasPath, realHome);
                Log.Information("[HomeAlias] Created home alias symlink: {AliasPath} -> {RealHome}", aliasPath, realHome);
            }
            else
            {
                Log.Information("[HomeAlias] Reusing home alias symlink: {AliasPath} -> {RealHome}", aliasPath, realHome);
            }

            // 在目前進程中設定環境變數，讓子進程繼承
            Environment.SetEnvironmentVariable("XIV_HOME_ALIAS", aliasPath);
            Environment.SetEnvironmentVariable("XIV_REAL_HOME", realHome);
            Environment.SetEnvironmentVariable("XIV_USE_HOME_ALIAS", "1");
            Environment.SetEnvironmentVariable("HOME", aliasPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HomeAlias] Failed to prepare home alias symlink");
        }
    }

    private static void StartKestrelServer(int port, string[] args, string logPath)
    {
        var contentRoot = AppDomain.CurrentDomain.BaseDirectory;
        var webRoot = "wwwroot";

        if (!Directory.Exists(Path.Combine(contentRoot, webRoot)))
        {
            var devContentRoot = Directory.GetCurrentDirectory();
            if (Directory.Exists(Path.Combine(devContentRoot, webRoot)))
            {
                contentRoot = devContentRoot;
            }
            else
            {
                var parentDir = Directory.GetParent(devContentRoot)?.FullName;
                if (parentDir != null && Directory.Exists(Path.Combine(parentDir, webRoot)))
                {
                    contentRoot = parentDir;
                }
            }
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRoot,
            WebRootPath = webRoot
        });

        // 設定 Kestrel 監聽埠口
        builder.WebHost.UseKestrel(options =>
        {
            options.ListenLocalhost(port);
        });

        // 套用 Serilog
        builder.Host.UseSerilog();

        // 設置 JSON Source Generator 解決器 (開發環境相容反射)
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
            options.SerializerOptions.TypeInfoResolverChain.Add(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        });

        // 註冊 Config 服務與 Discord RPC
        builder.Services.AddSingleton<ConfigService>();
        builder.Services.AddSingleton<DiscordRpcBridgeService>();

        // 平台相依服務
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            builder.Services.AddSingleton<XIVTheCalamity.Platform.IEnvironmentService, 
                XIVTheCalamity.Platform.Windows.WindowsEnvironmentService>();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            builder.Services.AddSingleton<XIVTheCalamity.Platform.IEnvironmentService, 
                XIVTheCalamity.Platform.MacOS.Wine.WineEnvironmentService>();
            builder.Services.AddSingleton<WineMacOSDownloadService>();
            builder.Services.AddSingleton<WinePrefixService>();
            builder.Services.AddSingleton<WineConfigService>();
            builder.Services.AddSingleton<XIVTheCalamity.Platform.MacOS.Audio.AudioRouterService>();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            builder.Services.AddSingleton<XIVTheCalamity.Platform.IEnvironmentService, 
                XIVTheCalamity.Platform.Linux.Proton.ProtonGeEnvironmentService>();
            builder.Services.AddSingleton<XIVTheCalamity.Platform.Linux.Proton.ProtonGeDownloadService>();
            builder.Services.AddSingleton<XIVTheCalamity.Platform.Linux.Umu.UmuDownloadService>();
            builder.Services.AddSingleton<XIVTheCalamity.Platform.Linux.Wine.DxvkDownloadService>();
            builder.Services.AddSingleton<WinePrefixService>();
            builder.Services.AddSingleton<WineConfigService>();
        }

        // HttpClient 註冊
        builder.Services.AddHttpClient<TcAuthService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (XIVTheCalamity/1.0)");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        builder.Services.AddHttpClient();

        // 遊戲業務服務
        builder.Services.AddSingleton<GameVersionService>();
        builder.Services.AddSingleton<PatchListParser>();
        builder.Services.AddSingleton<PatchInstallService>();
        builder.Services.AddSingleton<PatchDownloadManager>();
        builder.Services.AddSingleton<UpdateManager>();
        builder.Services.AddSingleton<GameLaunchService>();

        // Dalamud 注入器相關服務
        builder.Services.AddSingleton<DalamudPathService>();
        builder.Services.AddSingleton<DalamudUpdater>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            builder.Services.AddSingleton<IDalamudInjector, WindowsDalamudInjector>();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            builder.Services.AddSingleton<IDalamudInjector, LinuxDalamudInjector>();
        }
        else
        {
            builder.Services.AddSingleton<IDalamudInjector, WineDalamudInjector>();
        }

        builder.Services.AddSingleton<EventBroadcastHub>();

        // CORS 設定
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        _webApp = builder.Build();

        // 調整日誌等級
        _ = AdjustLogLevelAsync(_webApp, logPath);

        _webApp.UseCors();
        _webApp.UseStaticFiles();

        // 映射 API Endpoints
        _webApp.MapConfigEndpoints();
        _webApp.MapAuthEndpoints();
        _webApp.MapDalamudEndpoints();
        _webApp.MapEnvironmentEndpoints();
        _webApp.MapGameEndpoints();
        _webApp.MapDiscordRpcEndpoints();
        _webApp.MapUpdateEndpoints();
        _webApp.MapWineEndpoints();
        _webApp.MapElectronBridgeEndpoints();

        _webApp.MapGet("/health", () => Results.Ok(new HealthResponse("healthy", DateTime.UtcNow)))
           .WithName("HealthCheck");

        _ = _webApp.RunAsync();
        Log.Information("[Kestrel] API Server and Static Files started on http://localhost:{Port}", port);
    }

    private static async Task AdjustLogLevelAsync(WebApplication app, string logPath)
    {
        var configService = app.Services.GetRequiredService<ConfigService>();
        var config = await configService.LoadConfigAsync();
        
        if (!config.Launcher.DevelopmentMode)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Warning()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
    }

    public static string GetAppSupportPath()
    {
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            appSupport = Path.Combine(
                HomePathService.GetEffectiveHomePath(),
                "Library", "Application Support");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            appSupport = Path.Combine(
                HomePathService.GetEffectiveHomePath(),
                ".config");
        }
        
        return appSupport;
    }

    public static string GetLogFilePath()
    {
        var appSupport = GetAppSupportPath();
        var logDir = Path.Combine(appSupport, "XIVTheCalamity", "logs");
        Directory.CreateDirectory(logDir);
        
        return Path.Combine(logDir, "backend-.log");
    }
}
