using System.Text;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Game.Models;

namespace XIVTheCalamity.Game.Services;

/// <summary>
/// Patch list parsing service (Taiwan API)
/// </summary>
public class PatchListParser
{
    private readonly ILogger<PatchListParser> _logger;
    private readonly HttpClient _httpClient;

    // Taiwan public API
    private const string TaiwanPatchListUrl = "https://user-cdn.ffxiv.com.tw/launcher/patch/v2.txt";
    
    // Taiwan official API (requires login) - Note: HTTP not HTTPS
    private const string TaiwanOfficialApiBaseUrl = "http://patch-gamever.ffxiv.com.tw/http/win32/ffxivtc_release_tc_game";

    public PatchListParser(ILogger<PatchListParser> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Fetch Taiwan patch list (background update: public API)
    /// </summary>
    public async Task<List<PatchInfo>> FetchPatchListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching patch list: {Url}", TaiwanPatchListUrl);

            var response = await _httpClient.GetStringAsync(TaiwanPatchListUrl, cancellationToken);
            var patches = ParsePatchList(response);

            _logger.LogInformation("Successfully fetched {Count} patches", patches.Count);
            return patches;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch patch list");
            throw;
        }
    }

    /// <summary>
    /// Parse patch list (Tab-separated format - public API v2.txt)
    /// Format: size\ttotal\tcount\tparts\tversion\trepo\tx\thash\turl
    /// </summary>
    private List<PatchInfo> ParsePatchList(string content)
    {
        var patches = new List<PatchInfo>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var fields = line.Split('\t');
            if (fields.Length < 9)
            {
                _logger.LogWarning("Skipping invalid line: {Line}", line);
                continue;
            }

            try
            {
                var patch = new PatchInfo
                {
                    Size = long.Parse(fields[0]),
                    Version = fields[4],
                    Repository = ParseRepository(fields[5]),
                    Hash = fields[7],
                    Url = fields[8],
                    FileName = Path.GetFileName(fields[8])
                };

                patches.Add(patch);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse patch info: {Line}", line);
            }
        }

        return patches;
    }

    /// <summary>
    /// Parse official API response
    /// Reference: XIVLauncher.Common.Game.Patch.PatchList.PatchListParser
    /// Format: Skip first 5 lines and last 2 lines, middle is patch data
    /// Fields: size\t?\t?\t?\tversion\thashType\tblockSize\thashes\turl (9 fields)
    /// Or: size\t?\t?\t?\tversion\turl (6 fields, boot)
    /// </summary>
    private List<PatchInfo> ParseOfficialApiResponse(string content)
    {
        var patches = new List<PatchInfo>();
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        const int START_OFFSET = 5;
        
        if (lines.Length < START_OFFSET + 2)
        {
            _logger.LogWarning("Patch list too short: {Length} lines", lines.Length);
            return patches;
        }

        // Reference XIVLauncher.Common: skip first 5 and last 2 lines
        for (var i = START_OFFSET; i < lines.Length - 2; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < 6)
            {
                continue;
            }

            try
            {
                // Field index reference from XIVLauncher.Common:
                // fields[0] = Length (Size)
                // fields[4] = VersionId
                // fields[5] = HashType (if 9 fields) or Url (if 6 fields)
                // fields[6] = HashBlockSize (if 9 fields)
                // fields[7] = Hashes (if 9 fields)
                // fields[8] = Url (if 9 fields)
                
                var url = fields.Length == 9 ? fields[8] : fields[5];
                
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    continue;
                }

                var repository = ParseRepositoryFromUrl(url);
                
                // Taiwan region won't have boot updates, skip
                if (repository == GameRepository.Boot)
                {
                    continue;
                }
                
                var hash = fields.Length == 9 ? fields[7] : "";

                var patch = new PatchInfo
                {
                    Size = long.Parse(fields[0]),
                    Version = fields[4],
                    Repository = repository,
                    Hash = hash,
                    Url = url,
                    FileName = Path.GetFileName(url)
                };

                patches.Add(patch);
                _logger.LogDebug("Parsed patch: {Version} [{Repo}] {Size} bytes", 
                    patch.Version, patch.Repository, patch.Size);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse patch info: {Line}", line);
            }
        }

        return patches;
    }

    /// <summary>
    /// Parse repository name (for public API)
    /// </summary>
    private GameRepository ParseRepository(string repoName)
    {
        return repoName.ToLower() switch
        {
            "ffxivboot" or "boot" => GameRepository.Boot,
            "ffxivgame" or "game" or "0" => GameRepository.Game,
            "ex1" or "1" => GameRepository.Ex1,
            "ex2" or "2" => GameRepository.Ex2,
            "ex3" or "3" => GameRepository.Ex3,
            "ex4" or "4" => GameRepository.Ex4,
            "ex5" or "5" => GameRepository.Ex5,
            _ => GameRepository.Game
        };
    }

    /// <summary>
    /// Parse repository from URL
    /// http://patch-dl.ffxiv.com.tw/game/ex1/... → Ex1
    /// http://patch-dl.ffxiv.com.tw/game/0b90d03e/... → Game
    /// </summary>
    private GameRepository ParseRepositoryFromUrl(string url)
    {
        if (url.Contains("/ex1/")) return GameRepository.Ex1;
        if (url.Contains("/ex2/")) return GameRepository.Ex2;
        if (url.Contains("/ex3/")) return GameRepository.Ex3;
        if (url.Contains("/ex4/")) return GameRepository.Ex4;
        if (url.Contains("/ex5/")) return GameRepository.Ex5;
        if (url.Contains("/boot/")) return GameRepository.Boot;
        return GameRepository.Game;
    }

    /// <summary>
    /// Filter required patches (based on local version)
    /// </summary>
    public List<PatchInfo> GetRequiredPatches(List<PatchInfo> allPatches, GameVersionInfo localVersions)
    {
        var requiredPatches = new List<PatchInfo>();

        foreach (var patch in allPatches)
        {
            var localVersion = localVersions.GetVersion(patch.Repository);
            
            // If local doesn't have this repository version, use base version
            if (localVersion == null)
            {
                localVersion = new GameVersion
                {
                    Repository = patch.Repository,
                    Version = "2012.01.01.0000.0000"
                };
            }

            // Compare versions: patch version > local version
            if (string.Compare(patch.Version, localVersion.Version, StringComparison.Ordinal) > 0)
            {
                requiredPatches.Add(patch);
                _logger.LogDebug("Need patch: [{Repo}] {Patch} (local: {LocalVersion})", 
                    patch.Repository, patch.FileName, localVersion.Version);
            }
        }

        // Sort by version (ensure installation order) and remove duplicates
        // Keep only the first occurrence of each unique (Repository, FileName) combination
        requiredPatches = requiredPatches
            .OrderBy(p => p.Repository)
            .ThenBy(p => p.Version)
            .GroupBy(p => new { p.Repository, p.FileName })
            .Select(g => g.First())
            .ToList();

        _logger.LogInformation("Need to download {Count} patches, total size: {Size} MB", 
            requiredPatches.Count, 
            requiredPatches.Sum(p => p.Size) / 1024.0 / 1024.0);

        return requiredPatches;
    }

    /// <summary>
    /// Filter already downloaded patches (for resume support)
    /// </summary>
    public async Task<List<PatchInfo>> FilterDownloadedPatchesAsync(
        List<PatchInfo> patches, 
        string downloadPath,
        CancellationToken cancellationToken = default)
    {
        var pendingPatches = new List<PatchInfo>();

        foreach (var patch in patches)
        {
            // Include repository subdirectory in path (e.g., .patches/Game/, .patches/Ex1/)
            var localPath = Path.Combine(downloadPath, patch.Repository.ToString(), patch.FileName);
            patch.LocalPath = localPath;

            // Check if file exists
            if (!File.Exists(localPath))
            {
                pendingPatches.Add(patch);
                continue;
            }

            // Check file size
            var fileInfo = new FileInfo(localPath);
            if (fileInfo.Length != patch.Size)
            {
                _logger.LogWarning("File size mismatch: {File} (local: {LocalSize}, expected: {ExpectedSize})",
                    patch.FileName, fileInfo.Length, patch.Size);
                pendingPatches.Add(patch);
                continue;
            }

            // File exists and size is correct, skip
            _logger.LogInformation("Patch already downloaded: {File}", patch.FileName);
        }

        _logger.LogInformation("Need to download {Pending} / {Total} patches", 
            pendingPatches.Count, patches.Count);

        return pendingPatches;
    }

    /// <summary>
    /// <summary>
    /// Get patches from official API (Taiwan version does NOT require sessionId)
    /// Reference: XIVTCLauncher.Cross GameUpdateService.CheckVersionWithOfficialApiAsync
    /// </summary>
    /// <remarks>
    /// Taiwan version format:
    /// - POST http://patch-gamever.ffxiv.com.tw/http/win32/ffxivtc_release_tc_game/{version}/
    /// - Body starts with newline (TC Region skips boot version check)
    /// - Each line: ex{n}\t{version}
    /// - Response is multipart/mixed format
    /// </remarks>
    public async Task<List<PatchInfo>> GetPatchesFromOfficialApiAsync(
        string gameVersion, 
        GameVersionInfo? localVersions = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // URL format: http://patch-gamever.ffxiv.com.tw/http/win32/ffxivtc_release_tc_game/{version}/
            var apiUrl = $"{TaiwanOfficialApiBaseUrl}/{gameVersion}/";
            _logger.LogInformation("Calling official API: {Url}", apiUrl);

            // Build request body - Taiwan version format
            // Start with newline (skip boot version check), then expansion versions
            var bodyBuilder = new StringBuilder();
            bodyBuilder.Append("\n"); // TC Region: skip boot version check
            
            if (localVersions != null)
            {
                // Add expansion pack versions (ex1 = Ex1, ex2 = Ex2, etc.)
                // GameRepository enum: Boot=0, Game=1, Ex1=2, Ex2=3, Ex3=4, Ex4=5, Ex5=6
                // API expects: ex1, ex2, ex3, ex4, ex5
                // Only include versions that actually exist (have a FilePath)
                for (int exNum = 1; exNum <= 5; exNum++)
                {
                    // ex1 corresponds to GameRepository.Ex1 (value 2), ex2 to Ex2 (value 3), etc.
                    var repo = (GameRepository)(exNum + 1);
                    var version = localVersions.GetVersion(repo);
                    // Only include if version file actually exists (FilePath is not empty)
                    if (version != null && !string.IsNullOrEmpty(version.FilePath))
                    {
                        bodyBuilder.Append($"ex{exNum}\t{version.Version}\n");
                    }
                }
            }
            
            var body = bodyBuilder.ToString();
            _logger.LogDebug("Request body: {Body}", body.Replace("\n", "\\n"));

            // Create request
            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Headers.Add("X-Hash-Check", "enabled");
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));

            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            _logger.LogInformation("Official API response: {StatusCode}", response.StatusCode);

            // 204 No Content = no update needed
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _logger.LogInformation("Server returned 204 No Content - game is up to date");
                return new List<PatchInfo>();
            }
            
            // Check response
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Official API error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Official API response error: {response.StatusCode}");
            }

            // Get X-Latest-Version from header
            if (response.Headers.TryGetValues("X-Latest-Version", out var latestVersions))
            {
                var latestVersion = latestVersions.FirstOrDefault();
                _logger.LogInformation("X-Latest-Version: {LatestVersion}", latestVersion);
            }
            
            // Parse patch list (using official API format parser)
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("Official API response length: {Length} bytes", content.Length);
            
            // Check if there are actual patch URLs
            if (!content.Contains("http://patch-dl.ffxiv.com.tw"))
            {
                _logger.LogInformation("No patch URLs in response - game is up to date");
                return new List<PatchInfo>();
            }
            
            var patches = ParseOfficialApiResponse(content);
            _logger.LogInformation("Fetched {Count} patches from official API", patches.Count);
            
            return patches;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call official API");
            throw;
        }
    }
}
