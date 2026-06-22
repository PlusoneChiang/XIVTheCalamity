using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XIVTheCalamity.Core.Models;
using XIVTheCalamity.Dalamud.Models;
using XIVTheCalamity.Platform;

namespace XIVTheCalamity.Dalamud.Interfaces;

/// <summary>
/// Interface for Dalamud injectors
/// </summary>
public interface IDalamudInjector
{
    /// <summary>
    /// Inject Dalamud into an existing game process
    /// </summary>
    Task<DalamudInjectionResult> InjectAsync(
        WineLauncher? launcher,
        Dictionary<string, string>? environment,
        DalamudInjectionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Launch the game directly with Dalamud loaded at the entry point
    /// </summary>
    Task<DalamudInjectionResult> LaunchWithEntryPointAsync(
        WineLauncher? launcher,
        string gameExePath,
        string gameArguments,
        Dictionary<string, string>? environment,
        DalamudInjectionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensure the .NET Program Files symlink is set up inside Wine Prefix (macOS only)
    /// </summary>
    void EnsureDotnetProgramFilesSymlink(string runtimePath);
}
