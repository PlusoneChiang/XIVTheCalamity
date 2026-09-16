using System.Runtime.InteropServices;

namespace XIVTheCalamity.Platform.MacOS.Wine;

public enum RosettaAvailability
{
    NotRequired,
    Available,
    Missing
}

public static class RosettaAvailabilityCheck
{
    public static RosettaAvailability Check(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            return RosettaAvailability.NotRequired;

        return File.Exists("/Library/Apple/System/Library/Receipts/com.apple.pkg.RosettaUpdateAuto.plist")
            ? RosettaAvailability.Available
            : RosettaAvailability.Missing;
    }
}
