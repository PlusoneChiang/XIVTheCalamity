using System.Diagnostics;

namespace XIVTheCalamity.Dalamud.Models;

/// <summary>
/// Injection result
/// </summary>
public class DalamudInjectionResult
{
    public bool Success { get; set; }
    public int? ExitCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Game process PID — populated by EntryPoint mode after Dalamud.Injector starts the game.
    /// </summary>
    public int? GamePid { get; set; }

    /// <summary>
    /// The umu-run process handle (valid only when running without --no-wait).
    /// The injector stays alive until the game exits, so this process acts as a
    /// game-lifetime proxy — monitor it instead of scanning /proc.
    /// </summary>
    public Process? InjectorProcess { get; set; }

    /// <summary>
    /// Raw stdout from the injector process. In EntryPoint mode contains the JSON line
    /// {"pid": &lt;WinePID&gt;, "handle": &lt;handle&gt;} output by Dalamud.Injector.
    /// </summary>
    public string? StdOut { get; set; }
}
