using System;
using System.Threading.Tasks;
using Photino.NET;
using Serilog;

namespace XIVTheCalamity;

public static class MainWindowContainer
{
    public static PhotinoWindow? MainWindow { get; set; }
    public static int Port { get; set; }
    
    private static PhotinoWindow? _settingsWindow;
    private static readonly object _lock = new();

    public static Task<string[]> ShowOpenFolderAsync()
    {
        var tcs = new TaskCompletionSource<string[]>();
        if (MainWindow == null)
        {
            tcs.SetResult(Array.Empty<string>());
            return tcs.Task;
        }

        MainWindow.Invoke(() =>
        {
            try
            {
                var result = MainWindow.ShowOpenFolder();
                tcs.SetResult(result ?? Array.Empty<string>());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    public static Task OpenSettingsWindowAsync()
    {
        var tcs = new TaskCompletionSource();
        if (MainWindow == null)
        {
            Log.Warning("[MainWindowContainer] Cannot open settings window: MainWindow is null");
            tcs.SetResult();
            return tcs.Task;
        }

        MainWindow.Invoke(() =>
        {
            try
            {
                lock (_lock)
                {
                    if (_settingsWindow != null)
                    {
                        Log.Information("[MainWindowContainer] Settings window already exists, skipping creation");
                        tcs.SetResult();
                        return;
                    }

                    string platformStr = "linux";
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                        platformStr = "darwin";
                    else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                        platformStr = "win32";

                    Log.Information("[MainWindowContainer] Instantiating settings window (Width: 800, Height: 632)");
                    _settingsWindow = new PhotinoWindow()
                        .SetTitle("Settings - XIVTheCalamity")
                        .SetSize(800, 632) // 800 width, 600 content size + 32px macOS titlebar height
                        .SetResizable(false)
                        .SetDevToolsEnabled(true)
                        .SetContextMenuEnabled(true)
                        .SetWebSecurityEnabled(false)
                        .Center()
                        .Load(new Uri($"http://localhost:{Port}/settings.html?platform={platformStr}"));

                    Log.Information("[MainWindowContainer] Settings window instantiated successfully");

                    _settingsWindow.WindowClosing += (sender, e) =>
                    {
                        Log.Information("[MainWindowContainer] Settings window closing");
                        lock (_lock)
                        {
                            _settingsWindow = null;
                        }
                        return false; // allow close
                    };
                }
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindowContainer] Exception inside MainWindow.Invoke during settings window creation");
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}
