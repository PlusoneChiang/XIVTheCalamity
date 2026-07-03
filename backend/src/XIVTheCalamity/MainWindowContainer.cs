using System;
using System.Threading.Tasks;
using Photino.NET;
using Serilog;

namespace XIVTheCalamity;

public static class MainWindowContainer
{
    public static PhotinoWindow? MainWindow { get; set; }
    public static int Port { get; set; }

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

    public static void UpdateDevMode(bool isDevMode)
    {
        // DevTools/ContextMenu runtime updates are disabled here to prevent WebView2 reloads.
        // The blocking/allowing of context menus and DevTools shortcuts is now handled on the frontend (via polyfill.js).
        Log.Information("[MainWindowContainer] DevMode updated to {IsDevMode} (handled by frontend)", isDevMode);
    }
}
