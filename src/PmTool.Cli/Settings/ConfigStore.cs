using System.Text.Json;
using System.Text.Json.Serialization;

namespace PmTool.Cli.Settings;

/// <summary>存在設定檔裡的內容。權杖是機密：不印出來、不進記錄。</summary>
internal sealed record StoredConfig
{
    public string? Server { get; init; }

    public string? Token { get; init; }

    /// <summary>
    /// 本機代理（kumi agent start）用的權杖，和 <see cref="Token"/> 同一個伺服器。用瀏覽器登入（kumi agent login、kumi setup）時存的；
    /// 沒有時本機代理退回用 <see cref="Token"/>（舊版只有一把）。見 internal/HANDOFF-setup-wizard.md 第 4 節。
    /// </summary>
    public string? AgentToken { get; init; }

    /// <summary>自動產生的 ToString 會把權杖印出來；記錄或例外訊息不小心帶到這個物件時不能洩漏。</summary>
    public override string ToString() => $"StoredConfig {{ Server = {Server}, Token = {Mask(Token)}, AgentToken = {Mask(AgentToken)} }}";

    private static string Mask(string? token) => token is null ? "(無)" : "***";
}

/// <summary>登出時實際刪掉了哪幾把。</summary>
internal readonly record struct ClearedTokens(bool Cli, bool Agent)
{
    public bool Any => Cli || Agent;
}

/// <summary>
/// 設定檔：伺服器網址與權杖。Unix 上檔案只有擁有者讀寫（600），資料夾 700；
/// 先寫到暫存檔再換名，寫到一半中斷不會留下壞掉的檔。
/// </summary>
internal sealed class ConfigStore(string path)
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Path { get; } = path;

    private const string DirectoryName = "kumi";

    // 改名 Kumi 之前的資料夾：舊版登入過的人升級之後照樣讀得到存的權杖，不必重新登入。
    private const string LegacyDirectoryName = "pm-tool";

    /// <summary>
    /// 預設放在 $XDG_CONFIG_HOME/kumi/config.json，沒設時是 ~/.config/kumi/config.json。
    /// 新的還沒有、舊的 pm-tool/config.json 在時用舊的（之後也存回那裡），新舊都有時用新的。
    /// </summary>
    public static string DefaultPath(Func<string, string?> environment)
    {
        var xdg = environment("XDG_CONFIG_HOME");
        var root = !string.IsNullOrWhiteSpace(xdg)
            ? xdg
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        var path = System.IO.Path.Combine(root, DirectoryName, "config.json");
        var legacy = System.IO.Path.Combine(root, LegacyDirectoryName, "config.json");
        return !File.Exists(path) && File.Exists(legacy) ? legacy : path;
    }

    public StoredConfig Load()
    {
        if (!File.Exists(Path))
        {
            return new StoredConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<StoredConfig>(File.ReadAllText(Path), Options) ?? new StoredConfig();
        }
        catch (JsonException)
        {
            throw new CliException(ExitCodes.Error, $"設定檔讀不懂：{Path}。請刪掉它，再執行 kumi auth login。");
        }
        catch (IOException exception)
        {
            throw new CliException(ExitCodes.Error, $"讀不到設定檔 {Path}：{exception.Message}");
        }
    }

    public void Save(StoredConfig config) => WriteAtomically(Path, JsonSerializer.Serialize(config, Options));

    /// <summary>
    /// 把文字寫進一個只有擁有者能讀寫的檔案（資料夾不存在就建立）。設定檔與本機代理的電腦編號共用。
    /// </summary>
    internal static void WriteAtomically(string path, string text)
    {
        var directory = System.IO.Path.GetDirectoryName(path)!;
        try
        {
            EnsureDirectory(directory);
            WriteThenReplace(directory, path, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliException(ExitCodes.Error, $"寫不進設定檔 {path}：{exception.Message}");
        }
    }

    /// <summary>寫到暫存檔再換名；任何一步失敗，暫存檔都要清掉，不能在設定資料夾留下一份帶著權杖的檔案。</summary>
    private static void WriteThenReplace(string directory, string path, string text)
    {
        var temporary = System.IO.Path.Combine(directory, $".config-{Guid.NewGuid():N}.tmp");
        try
        {
            WriteOwnerOnly(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 清不掉就算了：原本的錯誤比較重要。
        }
    }

    /// <summary>刪掉命令列與本機代理的兩把權杖，伺服器網址留著，下次登入不必再打一次。</summary>
    public ClearedTokens ClearTokens()
    {
        var current = Load();
        var cleared = new ClearedTokens(current.Token is not null, current.AgentToken is not null);
        if (cleared.Any)
        {
            Save(current with { Token = null, AgentToken = null });
        }

        return cleared;
    }

    private static void EnsureDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, OwnerOnlyDirectory);
        }
    }

    private static void WriteOwnerOnly(string file, string text)
    {
        // 建立檔案的當下就是 600，沒有「先建出來、再改權限」之間別人讀得到的空檔。
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFile;
        }

        using var stream = new FileStream(file, options);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.Write(text);
    }
}
