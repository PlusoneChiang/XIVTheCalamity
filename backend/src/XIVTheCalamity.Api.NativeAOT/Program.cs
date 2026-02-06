using Serilog;
using System.Runtime.InteropServices;
using XIVTheCalamity.Api.NativeAOT;
using XIVTheCalamity.Api.NativeAOT.Endpoints;
using XIVTheCalamity.Core.Services;
using XIVTheCalamity.Platform.MacOS.Wine;
using XIVTheCalamity.Game.Services;
using XIVTheCalamity.Game.Launcher;
using XIVTheCalamity.Game.Authentication;
using XIVTheCalamity.Dalamud.Services;

// Configure Serilog
var logPath = GetLogFilePath();
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
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
    Log.Information("Starting XIVTheCalamity API Server (NativeAOT)");

    var builder = WebApplication.CreateSlimBuilder(args);

    // Configure URLs
    builder.WebHost.UseUrls("http://localhost:5050");

    // Use Serilog
    builder.Host.UseSerilog();
    
    // Configure JSON with Source Generator
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    });

    // Register ConfigService
    builder.Services.AddSingleton<ConfigService>();

    // Platform services
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        builder.Services.AddSingleton<XIVTheCalamity.Platform.IEnvironmentService, 
            XIVTheCalamity.Platform.Windows.WindowsEnvironmentService>();
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        builder.Services.AddSingleton<XIVTheCalamity.Platform.IEnvironmentService, 
            XIVTheCalamity.Platform.MacOS.Wine.WineEnvironmentService>();
        builder.Services.AddSingleton<WinePrefixService>();
        builder.Services.AddSingleton<WineConfigService>();
        builder.Services.AddSingleton<XIVTheCalamity.Platform.MacOS.Audio.AudioRouterService>();
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        builder.Services.AddSingleton<XIVTheCalamity.Platform.IEnvironmentService, 
            XIVTheCalamity.Platform.Linux.Wine.WineXIVEnvironmentService>();
        builder.Services.AddSingleton<XIVTheCalamity.Platform.Linux.Wine.WineXIVDownloadService>();
    }

    // Configure HttpClient for TcAuthService
    builder.Services.AddHttpClient<TcAuthService>(client =>
    {
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (XIVTheCalamity/1.0)");
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    // Game services
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<GameVersionService>();
    builder.Services.AddSingleton<PatchListParser>();
    builder.Services.AddSingleton<PatchInstallService>();
    builder.Services.AddSingleton<PatchDownloadManager>();
    builder.Services.AddSingleton<UpdateManager>();
    builder.Services.AddSingleton<GameLaunchService>();

    // Dalamud services
    builder.Services.AddSingleton<DalamudPathService>();
    builder.Services.AddSingleton<DalamudUpdater>();
    builder.Services.AddSingleton<DalamudInjectorService>();

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    var app = builder.Build();

    // Load config for log level adjustment
    var configService = app.Services.GetRequiredService<ConfigService>();
    var config = await configService.LoadConfigAsync();
    
    if (!config.Launcher.DevelopmentMode)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
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
    }

    // Use CORS
    app.UseCors();

    // Map Minimal API endpoints
    app.MapConfigEndpoints();
    app.MapAuthEndpoints();
    app.MapDalamudEndpoints();
    app.MapEnvironmentEndpoints();
    app.MapGameEndpoints();
    app.MapUpdateEndpoints();
    app.MapWineEndpoints();

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new HealthResponse("healthy", DateTime.UtcNow)))
       .WithName("HealthCheck");

    Log.Information("═══════════════════════════════════════════");
    Log.Information("XIV The Calamity API Server (NativeAOT)");
    Log.Information("═══════════════════════════════════════════");
    Log.Information("Ready: http://localhost:5050");
    Log.Information("Health: http://localhost:5050/health");
    Log.Information("Log Path: {LogPath}", logPath);
    Log.Information("═══════════════════════════════════════════");

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
