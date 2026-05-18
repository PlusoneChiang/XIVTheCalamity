using System.Runtime.InteropServices;

namespace XIVTheCalamity.Core.Services;

/// <summary>
/// Resolves home paths with optional macOS alias support.
/// </summary>
public static class HomePathService
{
    private const string HomeAliasEnv = "XIV_HOME_ALIAS";
    private const string RealHomeEnv = "XIV_REAL_HOME";

    public static string GetRealHomePath()
    {
        var envHome = Environment.GetEnvironmentVariable(RealHomeEnv);
        if (!string.IsNullOrWhiteSpace(envHome))
        {
            return NormalizePath(envHome);
        }

        return NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public static string GetEffectiveHomePath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var aliasHome = Environment.GetEnvironmentVariable(HomeAliasEnv);
            if (!string.IsNullOrWhiteSpace(aliasHome) && Directory.Exists(aliasHome))
            {
                return NormalizePath(aliasHome);
            }
        }

        return GetRealHomePath();
    }

    public static string MapToEffectiveHomePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return path;
        }

        var normalizedPath = NormalizePath(path);
        var realHome = GetRealHomePath();
        var effectiveHome = GetEffectiveHomePath();

        if (string.Equals(realHome, effectiveHome, StringComparison.Ordinal))
        {
            return normalizedPath;
        }

        if (string.Equals(normalizedPath, realHome, StringComparison.Ordinal))
        {
            return effectiveHome;
        }

        if (normalizedPath.StartsWith(realHome + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return effectiveHome + normalizedPath[realHome.Length..];
        }

        return normalizedPath;
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.Length <= 1)
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
