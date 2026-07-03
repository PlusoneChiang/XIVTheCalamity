using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XIVTheCalamity.DTOs;
using XIVTheCalamity.Services;

namespace XIVTheCalamity.Endpoints;

public static class NativeBridgeEndpoints
{
    public static void MapNativeBridgeEndpoints(this WebApplication app)
    {
        // 1. Storage endpoints
        app.MapPost("/api/storage/save", async (StorageSaveRequest req) =>
        {
            try
            {
                var credentialsDir = Path.Combine(Program.GetAppSupportPath(), "XIVTheCalamity", "credentials");
                Directory.CreateDirectory(credentialsDir);
                var filePath = Path.Combine(credentialsDir, req.Filename);

                var json = JsonSerializer.Serialize(req.Data, AppJsonContext.Default.JsonElement);
                await File.WriteAllTextAsync(filePath, json);

                return Results.Ok(new StorageResponse(true));
            }
            catch (Exception ex)
            {
                return Results.Ok(new StorageResponse(false, Error: ex.Message));
            }
        });

        app.MapPost("/api/storage/load", async (StorageLoadRequest req) =>
        {
            try
            {
                var filePath = Path.Combine(Program.GetAppSupportPath(), "XIVTheCalamity", "credentials", req.Filename);
                if (!File.Exists(filePath))
                {
                    return Results.Ok(new StorageResponse(true, Data: null));
                }

                var json = await File.ReadAllTextAsync(filePath);
                using var doc = JsonDocument.Parse(json);
                return Results.Ok(new StorageResponse(true, Data: doc.RootElement.Clone()));
            }
            catch (Exception ex)
            {
                return Results.Ok(new StorageResponse(false, Error: ex.Message));
            }
        });

        app.MapPost("/api/storage/delete", (StorageLoadRequest req) =>
        {
            try
            {
                var filePath = Path.Combine(Program.GetAppSupportPath(), "XIVTheCalamity", "credentials", req.Filename);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                return Results.Ok(new StorageResponse(true));
            }
            catch (Exception ex)
            {
                return Results.Ok(new StorageResponse(false, Error: ex.Message));
            }
        });




        app.MapPost("/api/window/close", () =>
        {
            MainWindowContainer.MainWindow?.Invoke(() =>
            {
                MainWindowContainer.MainWindow.Close();
            });
            return Results.Ok(new GenericActionResponse(true));
        });

        app.MapPost("/api/window/start-drag", () =>
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                MainWindowContainer.MainWindow?.Invoke(() =>
                {
                    Program.StartMacWindowDrag();
                });
            }
            return Results.Ok(new GenericActionResponse(true));
        });

        // 3. Dialog operations
        app.MapPost("/api/app/select-directory", async (HttpContext context) =>
        {
            try
            {
                var paths = await MainWindowContainer.ShowOpenFolderAsync();
                if (paths.Length > 0)
                {
                    return Results.Ok(new SelectDirectoryResponse(true, Path: paths[0]));
                }
                return Results.Ok(new SelectDirectoryResponse(false, Canceled: true));
            }
            catch
            {
                return Results.Ok(new SelectDirectoryResponse(false, Canceled: true));
            }
        });

        app.MapPost("/api/dialog/show-message-box", (MessageDialogRequest req) =>
        {
            MainWindowContainer.MainWindow?.Invoke(() =>
            {
                MainWindowContainer.MainWindow.ShowMessage(req.Title, req.Message);
            });
            return Results.Ok(new MessageDialogResponse(0));
        });

        // 4. Shell operations
        app.MapPost("/api/shell/open-external", (OpenExternalRequest req) =>
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(req.Url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", req.Url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", req.Url);
                }
                return Results.Ok(new GenericActionResponse(true));
            }
            catch (Exception ex)
            {
                return Results.Ok(new GenericActionResponse(false, ex.Message));
            }
        });

        // 5. App version
        app.MapGet("/api/app/get-version", () =>
        {
            var informationalVersion = typeof(XIVTheCalamity.Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            // .NET 8+ 會在資訊版本號尾部加上 git commit hash (例如 2.0.0-alpha+1a2b3c)
            // 這裡做個分割處理，只保留前面乾淨的版本號部分
            if (informationalVersion != null && informationalVersion.Contains('+'))
            {
                informationalVersion = informationalVersion.Split('+')[0];
            }

            var appVersion = informationalVersion ?? "2.0.0";
            return Results.Ok(new VersionInfoResponse(appVersion, "XIVTheCalamity", "Final Fantasy XIV Cross-Platform Launcher"));
        });

        // 6. Log folder
        app.MapPost("/api/app/open-log-folder", () =>
        {
            try
            {
                var logDir = Path.GetDirectoryName(Program.GetLogFilePath());
                if (logDir != null && Directory.Exists(logDir))
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Process.Start("explorer.exe", logDir);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        Process.Start("open", logDir);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        Process.Start("xdg-open", logDir);
                    }
                }
                return Results.Ok(new GenericActionResponse(true));
            }
            catch (Exception ex)
            {
                return Results.Ok(new GenericActionResponse(false, ex.Message));
            }
        });

        // 7. Directory operations
        app.MapPost("/api/app/create-directory", (OpenExternalRequest req) => // reuse OpenExternalRequest for path
        {
            try
            {
                Directory.CreateDirectory(req.Url);
                return Results.Ok(new SelectDirectoryResponse(true, Path: req.Url));
            }
            catch
            {
                return Results.Ok(new SelectDirectoryResponse(false));
            }
        });

        app.MapPost("/api/app/validate-game-directory", (OpenExternalRequest req) =>
        {
            var path = req.Url;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return Results.Ok(new ValidateDirectoryResponse(false, "Directory does not exist"));
            }

            var gameDir = Path.Combine(path, "game");
            var bootDir = Path.Combine(path, "boot");

            if (!Directory.Exists(gameDir) || !Directory.Exists(bootDir))
            {
                return Results.Ok(new ValidateDirectoryResponse(false, "Missing required subdirectories (game, boot)"));
            }

            return Results.Ok(new ValidateDirectoryResponse(true));
        });

        // 8. Broadcaster events endpoints
        app.MapPost("/api/events/broadcast", (BroadcastEventRequest req, EventBroadcastHub hub) =>
        {
            hub.Broadcast(req.EventName, req.Data);
            return Results.Ok();
        });

        app.MapGet("/api/events/stream", async (
            HttpContext context,
            EventBroadcastHub hub,
            CancellationToken cancellationToken) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var channel = System.Threading.Channels.Channel.CreateUnbounded<EventStreamItem>();
            var id = hub.Subscribe(channel);

            try
            {
                await context.Response.WriteAsync("retry: 1000\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var item = await channel.Reader.ReadAsync(cancellationToken);
                    var json = JsonSerializer.Serialize(item, AppJsonContext.Default.EventStreamItem);
                    await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // client disconnected
            }
            finally
            {
                hub.Unsubscribe(id);
            }
        });
    }

}
