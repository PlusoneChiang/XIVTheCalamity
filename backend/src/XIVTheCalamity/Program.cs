using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Photino.NET;

namespace XIVTheCalamity;

public class Program
{
    private static WebApplication? _webApp;

    [STAThread]
    public static void Main(string[] args)
    {
        var port = 5050;
        
        // 1. 於背景非同步啟動 Kestrel 網頁伺服器
        StartKestrelServer(port);

        // 2. 初始化並設定 Photino UI 視窗
        var window = new PhotinoWindow()
            .SetTitle("XIV The Calamity")
            .SetSize(1024, 768)
            .SetUseOsDefaultSize(false)
            .Center()
            .Load(new Uri($"http://localhost:{port}/index.html"));

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

    private static void StartKestrelServer(int port)
    {
        var webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
        
        // 確保 BaseDirectory 有 wwwroot (若在開發時執行 dotnet run，可 fallback 回專案目錄)
        if (!Directory.Exists(webRoot))
        {
            var devWebRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            if (Directory.Exists(devWebRoot))
            {
                webRoot = devWebRoot;
            }
        }

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            WebRootPath = webRoot
        });

        // 設定 Kestrel 監聽本地埠口
        builder.WebHost.UseKestrel(options =>
        {
            options.ListenLocalhost(port);
        });

        _webApp = builder.Build();
        
        // 託管 wwwroot 目錄下的靜態資源
        _webApp.UseStaticFiles();

        // 於背景異步啟動，不阻塞 UI 執行緒
        _ = _webApp.RunAsync();
        Console.WriteLine($"[Kestrel] Static file server started on http://localhost:{port} serving from {webRoot}");
    }
}
