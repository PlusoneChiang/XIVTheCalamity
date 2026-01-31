using Serilog;
using System.Runtime.InteropServices;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform.MacOS.Wine;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Game.Services;
using XIVTheCalamity.Game.Launcher;
using XIVTheCalamity.Game.Authentication;
using XIVTheCalamity.Dalamud.Services;

// Configure Serilog
var logPath = GetLogFilePath();
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithThreadId()
    .Enrich.WithMachineName()
    .WriteTo.Console()
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting XIVTheCalamity API Server");

    var builder = WebApplication.CreateBuilder(args);

    // Configure URLs explicitly for all environments
    builder.WebHost.UseUrls("http://localhost:5050");

    // Use Serilog
    builder.Host.UseSerilog();

    // Add services to the container
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // Use camelCase naming policy
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

    // Register ConfigService
    builder.Services.AddSingleton<ConfigService>();

    // 平台相關服務註冊 - 使用 IEnvironmentService 介面
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        // Windows: 使用空實作
        builder.Services.AddSingleton<XIVTheCalamity.Platform.IEnvironmentService, XIVTheCalamity.Platform.Windows.WindowsEnvironmentService>();
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        // macOS: 使用 Wine
        builder.Services.AddSingleton<XIVTheCalamity.Platform.IEnvironmentService, XIVTheCalamity.Platform.MacOS.Wine.WineEnvironmentService>();
        builder.Services.AddSingleton<WinePrefixService>();
        builder.Services.AddSingleton<WineConfigService>();
        builder.Services.AddSingleton<XIVTheCalamity.Platform.MacOS.Audio.AudioRouterService>();
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        // Linux: 使用 Proton
        builder.Services.AddSingleton<XIVTheCalamity.Platform.IEnvironmentService, XIVTheCalamity.Platform.Linux.Proton.ProtonEnvironmentService>();
        builder.Services.AddSingleton<XIVTheCalamity.Platform.Linux.Proton.ProtonDownloadService>();
    }

    // Configure HttpClient for TcAuthService
    builder.Services.AddHttpClient<TcAuthService>(client =>
    {
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (XIVTheCalamity/1.0)");
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    // Register Game Update services
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<GameVersionService>();
    builder.Services.AddSingleton<PatchListParser>();
    builder.Services.AddSingleton<PatchInstallService>();
    builder.Services.AddSingleton<UpdateManager>();
    
    // Register Game Launch service
    builder.Services.AddSingleton<GameLaunchService>();
    
    // Register Dalamud services
    builder.Services.AddSingleton<DalamudPathService>();
    builder.Services.AddSingleton<DalamudUpdater>();
    builder.Services.AddSingleton<DalamudInjectorService>();

    // Configure CORS for Electron frontend
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()      // Allow custom protocol origin
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    // Add OpenAPI for development
    builder.Services.AddOpenApi();

    var app = builder.Build();

    // Load config to determine log level
    var configService = app.Services.GetRequiredService<ConfigService>();
    var config = await configService.LoadConfigAsync();
    
    // Adjust log level based on DevelopmentMode
    if (!config.Launcher.DevelopmentMode)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithMachineName()
            .WriteTo.Console()
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        
        Log.Information("Verbose logging disabled");
    }

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    // Use CORS
    app.UseCors();

    // Map controllers
    app.MapControllers();

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
       .WithName("HealthCheck");

    // Output connection info for frontend
    var urls = app.Urls.Count > 0 ? app.Urls : new[] { "http://localhost:5050" };
    var url = urls.First();
    
    Log.Information("═══════════════════════════════════════════");
    Log.Information("XIV The Calamity API Server");
    Log.Information("═══════════════════════════════════════════");
    Log.Information("Ready: {Url}", url);
    Log.Information("Health: {Url}/health", url);
    Log.Information("Login: {Url}/api/auth/login", url);
    Log.Information("Config: {Url}/api/config", url);
    Log.Information("Log Path: {LogPath}", logPath);
    Log.Information("═══════════════════════════════════════════");

    // Wine initialization is triggered by frontend via SSE /api/environment/initialize
    // This ensures progress can be reported back to the UI

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static string GetLogFilePath()
{
    var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        appSupport = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support");
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        appSupport = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config");
    }
    
    var logDir = Path.Combine(appSupport, "XIVTheCalamity", "logs");
    Directory.CreateDirectory(logDir);
    
    return Path.Combine(logDir, "backend-.log");
}
