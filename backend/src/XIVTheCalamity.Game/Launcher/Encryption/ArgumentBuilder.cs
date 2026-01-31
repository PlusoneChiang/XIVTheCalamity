using System.Text;

namespace XIVTheCalamity.Game.Launcher.Encryption;

/// <summary>
/// Game launch argument builder
/// Reference: XIVLauncher.Common.Encryption.ArgumentBuilder
/// </summary>
public sealed class ArgumentBuilder
{
    private static readonly uint Version = 3;
    
    private static readonly char[] ChecksumTable =
    {
        'f', 'X', '1', 'p', 'G', 't', 'd', 'S',
        '5', 'C', 'A', 'P', '4', '_', 'V', 'L'
    };
    
    private readonly List<KeyValuePair<string, string>> _arguments;
    
    public ArgumentBuilder()
    {
        _arguments = new List<KeyValuePair<string, string>>();
    }
    
    public ArgumentBuilder Append(string key, string value)
    {
        _arguments.Add(new KeyValuePair<string, string>(key, value));
        return this;
    }
    
    /// <summary>
    /// Build unencrypted argument string
    /// </summary>
    public string Build()
    {
        var sb = new StringBuilder();
        var isFirst = true;
        foreach (var arg in _arguments)
        {
            if (!isFirst)
            {
                sb.Append(' ');
            }
            isFirst = false;
            
            // Add quotes if value contains spaces
            var value = arg.Value;
            if (value.Contains(' '))
            {
                sb.Append($"{arg.Key}=\"{value}\"");
            }
            else
            {
                sb.Append($"{arg.Key}={value}");
            }
        }
        return sb.ToString();
    }
    
    /// <summary>
    /// Build encrypted argument string
    /// </summary>
    public string BuildEncrypted()
    {
        var key = DeriveKey();
        return BuildEncrypted(key);
    }
    
    /// <summary>
    /// Build encrypted argument string with specified key
    /// </summary>
    public string BuildEncrypted(uint key)
    {
        var sb = new StringBuilder();
        foreach (var arg in _arguments)
        {
            // Format: " /{key} ={value}"
            sb.Append($" /{EscapeValue(arg.Key)} ={EscapeValue(arg.Value)}");
        }
        
        var arguments = sb.ToString();
        var blowfish = new LegacyBlowfish(GetKeyBytes(key));
        var ciphertext = blowfish.Encrypt(Encoding.UTF8.GetBytes(arguments));
        var base64Str = ToMangledSeBase64(ciphertext);
        var checksum = DeriveChecksum(key);
        
        return $"//**sqex{Version:D04}{base64Str}{checksum}**//";
    }
    
    private uint DeriveKey()
    {
        var rawTickCount = GetRawTickCount();
        var ticks = rawTickCount & 0xFFFF_FFFFu;
        var key = ticks & 0xFFFF_0000u;
        
        // Insert T parameter at beginning of argument list
        var keyPair = new KeyValuePair<string, string>("T", Convert.ToString(ticks));
        if (_arguments.Count > 0 && _arguments[0].Key == "T")
        {
            _arguments[0] = keyPair;
        }
        else
        {
            _arguments.Insert(0, keyPair);
        }
        
        return key;
    }
    
    private static uint GetRawTickCount()
    {
        // macOS: 使用 clock_gettime_nsec_np (CLOCK_MONOTONIC_RAW = 4)
        // 參考 XoM: clock_gettime_nsec_np(CLOCK_MONOTONIC_RAW) / 1000000
        if (OperatingSystem.IsMacOS())
        {
            return (uint)(GetClockTimeNsecNp(4) / 1000000);
        }
        // Linux: 使用 clock_gettime (CLOCK_MONOTONIC_RAW = 4)
        else if (OperatingSystem.IsLinux())
        {
            return (uint)(Environment.TickCount64 & 0xFFFF_FFFF);
        }
        // Windows: 使用 Environment.TickCount
        else
        {
            return (uint)Environment.TickCount;
        }
    }
    
    [System.Runtime.InteropServices.DllImport("c")]
    private static extern ulong clock_gettime_nsec_np(int clock_id);
    
    private static ulong GetClockTimeNsecNp(int clockId)
    {
        try
        {
            return clock_gettime_nsec_np(clockId);
        }
        catch
        {
            // Fallback if P/Invoke fails
            return (ulong)(Environment.TickCount64 * 1000000);
        }
    }
    
    private static char DeriveChecksum(uint key)
    {
        var index = (key & 0x000F_0000) >> 16;
        
        if (index < ChecksumTable.Length)
        {
            return ChecksumTable[index];
        }
        
        return '!';
    }
    
    private static byte[] GetKeyBytes(uint key)
    {
        var format = $"{key:x08}";
        return Encoding.UTF8.GetBytes(format);
    }
    
    private static string EscapeValue(string input)
    {
        // Spaces need doubling
        return input.Replace(" ", "  ");
    }
    
    /// <summary>
    /// Convert byte array to SE's special Base64 encoding
    /// </summary>
    private static string ToMangledSeBase64(byte[] input)
    {
        var base64 = Convert.ToBase64String(input);
        
        // SE uses special Base64 variant
        // Replace '+' -> '-', '/' -> '_', remove '='
        return base64
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
