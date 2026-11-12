using System.Diagnostics;

namespace XIVTheCalamity.Services;

public static class LauncherRestart
{
    private const string WaitArgument = "--restart-after";
    private static int _scheduled;

    // 新程序先等待舊程序退出，再取得單一實例鎖並開啟視窗。
    public static string[] WaitForPreviousProcess(string[] args)
    {
        if (args.Length == 0 || args[0] != WaitArgument) return args;
        if (args.Length < 2 || !int.TryParse(args[1], out var pid) || pid <= 0 || pid == Environment.ProcessId)
            throw new ArgumentException("重新啟動的程序識別碼無效。");
        try
        {
            using var previous = Process.GetProcessById(pid);
            if (!previous.WaitForExit(60_000))
                throw new TimeoutException("等待舊啟動器結束逾時，已取消重新啟動。");
        }
        catch (ArgumentException) { /* 舊程序已經結束。 */ }
        return args[2..];
    }

    public static void Schedule(HttpContext context)
    {
        if (Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0) return;
        try
        {
            var window = MainWindowContainer.MainWindow
                ?? throw new InvalidOperationException("找不到啟動器視窗。");
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("找不到啟動器執行檔。");
            var originalArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (originalArgs.Length >= 2 && originalArgs[0] == WaitArgument)
                originalArgs = originalArgs[2..];

            var start = new ProcessStartInfo { UseShellExecute = false };
            var bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
            if (OperatingSystem.IsMacOS() && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(bundle))
            {
                // 沿用 macOS 既有的乾淨環境，避免繼承 Wine Home Alias。
                start.FileName = "/usr/bin/env";
                foreach (var argument in new[] { "-i", "/usr/bin/open", "-n", bundle, "--args" })
                    start.ArgumentList.Add(argument);
            }
            else
            {
                var appImage = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("APPIMAGE") : null;
                start.FileName = string.IsNullOrWhiteSpace(appImage) ? executable : appImage;
                if (!File.Exists(start.FileName)) throw new FileNotFoundException("找不到重新啟動的執行檔。", start.FileName);
                if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            }
            start.ArgumentList.Add(WaitArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            foreach (var argument in originalArgs) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("無法啟動重新啟動程序。");
            context.Response.OnCompleted(() =>
            {
                window.Invoke(() => window.Close());
                return Task.CompletedTask;
            });
        }
        catch
        {
            Interlocked.Exchange(ref _scheduled, 0);
            throw;
        }
    }
}
