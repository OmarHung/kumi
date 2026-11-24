using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PmTool.Api.Cli;

/// <summary>伺服器上提供下載的一個 kumi 執行檔（公開是為了讓測試專案用得到）。<see cref="FilePath"/> 只給伺服器自己開檔用，不回給用戶端。</summary>
public sealed record CliPackage(string Platform, string FileName, string FilePath, long SizeBytes, string Sha256);

/// <summary>
/// 放在伺服器上的 kumi 執行檔目錄。映像檔裡的檔案不會變，所以第一次用到時掃一次、算好雜湊就不再讀；
/// 資料夾不存在或沒有符合的檔案，就是「沒有提供下載」，不是錯誤（開發時用 dotnet run 就是這樣）。
/// </summary>
public sealed partial class CliPackageCatalog
{
    public const string PackagesPathSetting = "Cli:PackagesPath";

    /// <summary>平台代碼，順序就是回應裡列出的順序。</summary>
    public static readonly IReadOnlyList<string> Platforms = ["macos-arm64", "macos-x64", "windows-x64"];

    private const string WindowsPlatform = "windows-x64";

    private readonly string _directory;
    private readonly ILogger<CliPackageCatalog> _logger;
    private readonly Lazy<Snapshot> _snapshot;

    public CliPackageCatalog(string directory, ILogger<CliPackageCatalog> logger)
    {
        _directory = directory;
        _logger = logger;
        _snapshot = new Lazy<Snapshot>(ScanSafely);
    }

    /// <summary>最新那一版的版本號；沒有提供下載時是 null。</summary>
    public string? Version => _snapshot.Value.Version;

    public IReadOnlyList<CliPackage> Packages => _snapshot.Value.Packages;

    /// <summary>只用平台代碼查目錄；呼叫端給的字串不會變成檔案路徑的一部分。</summary>
    public CliPackage? Find(string platform) =>
        Packages.FirstOrDefault(package => string.Equals(package.Platform, platform, StringComparison.Ordinal));

    [GeneratedRegex(@"^kumi-(\d+\.\d+\.\d+)-(macos-arm64|macos-x64|windows-x64)(\.exe)?$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    /// <summary>
    /// Lazy 會把例外快取起來：掃描丟了沒接住的例外（例如設定的路徑帶不合法的字元），之後每個請求都會是 500 直到重啟。
    /// 下載只是附帶的功能，出錯時當成沒有提供下載，記一筆錯誤讓維運看得到。
    /// </summary>
    private Snapshot ScanSafely()
    {
        try
        {
            return Scan();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "掃描 kumi 的下載資料夾 {Directory} 失敗，當成沒有提供下載。", _directory);
            return Snapshot.Empty;
        }
    }

    private Snapshot Scan()
    {
        if (!Directory.Exists(_directory))
        {
            return Snapshot.Empty;
        }

        var candidates = ReadCandidates();
        if (candidates.Count == 0)
        {
            return Snapshot.Empty;
        }

        var latest = candidates.Max(candidate => candidate.Version)!;
        foreach (var old in candidates.Where(candidate => candidate.Version != latest))
        {
            _logger.LogWarning("kumi 的下載資料夾裡有較舊的版本 {FileName}，只提供最新的 {Latest}。", old.FileName, latest);
        }

        var packages = new List<CliPackage>();
        foreach (var platform in Platforms)
        {
            var match = candidates.FirstOrDefault(candidate => candidate.Version == latest && candidate.Platform == platform);
            if (match is not null && TryDescribe(match) is { } package)
            {
                packages.Add(package);
            }
        }

        return packages.Count == 0 ? Snapshot.Empty : new Snapshot(latest.ToString(), packages);
    }

    private List<Candidate> ReadCandidates()
    {
        string[] paths;
        try
        {
            paths = Directory.GetFiles(_directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "讀不到 kumi 的下載資料夾 {Directory}。", _directory);
            return [];
        }

        var candidates = new List<Candidate>();
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(path);
            if (Parse(path, fileName) is { } candidate)
            {
                candidates.Add(candidate);
            }
            else
            {
                _logger.LogWarning("kumi 的下載資料夾裡的 {FileName} 不符合檔名規則，略過。", fileName);
            }
        }

        return candidates;
    }

    private static Candidate? Parse(string path, string fileName)
    {
        var match = FileNamePattern().Match(fileName);
        if (!match.Success)
        {
            return null;
        }

        // .exe 只有 Windows 的版本有，而且一定要有。
        var platform = match.Groups[2].Value;
        var hasExe = match.Groups[3].Success;
        if (hasExe != (platform == WindowsPlatform) || !System.Version.TryParse(match.Groups[1].Value, out var version))
        {
            return null;
        }

        return new Candidate(path, fileName, platform, version);
    }

    private CliPackage? TryDescribe(Candidate candidate)
    {
        try
        {
            using var stream = new FileStream(
                candidate.Path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, FileOptions.SequentialScan);
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            return new CliPackage(candidate.Platform, candidate.FileName, candidate.Path, stream.Length, hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "讀不到 kumi 的下載檔 {FileName}，略過。", candidate.FileName);
            return null;
        }
    }

    private sealed record Candidate(string Path, string FileName, string Platform, System.Version Version);

    private sealed record Snapshot(string? Version, IReadOnlyList<CliPackage> Packages)
    {
        public static readonly Snapshot Empty = new(null, []);
    }
}
