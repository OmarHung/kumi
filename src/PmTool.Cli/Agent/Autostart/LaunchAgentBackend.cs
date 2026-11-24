using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace PmTool.Cli.Agent.Autostart;

/// <summary>macOS：<c>~/Library/LaunchAgents/kumi.local-agent.plist</c>，用 launchctl 載入與卸載。</summary>
internal sealed partial class LaunchAgentBackend(AutostartEnvironment environment) : IAutostartBackend
{
    public const string Label = "kumi.local-agent";
    private const int BootstrapAttempts = 3;

    public string PlistPath => Path.Combine(environment.HomeDirectory, "Library", "LaunchAgents", $"{Label}.plist");

    public bool IsInstalled() => File.Exists(PlistPath);

    public Task InstallAsync(AutostartSpec spec, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        EnsurePrivateDirectory(Path.GetDirectoryName(spec.LogPath)!);
        File.WriteAllText(PlistPath, RenderPlist(spec), new UTF8Encoding(false));
        return Task.CompletedTask;
    }

    /// <summary>記錄檔會有伺服器網址、電腦名稱與工作的錯誤訊息：資料夾只給自己讀寫。已經存在的也改成 700。</summary>
    private static void EnsurePrivateDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public async Task LaunchAsync(AutostartSpec spec, CancellationToken cancellation)
    {
        var uid = await environment.GetUid(cancellation);
        // 先卸載再載入：已經載入的同名項目會讓 bootstrap 失敗。卸載失敗（本來就沒載入）不是問題。
        await environment.RunCommand("launchctl", ["bootout", Target(uid)], cancellation);
        CommandOutcome result = new(1, string.Empty);
        for (var attempt = 1; attempt <= BootstrapAttempts; attempt++)
        {
            result = await environment.RunCommand("launchctl", ["bootstrap", $"gui/{uid}", PlistPath], cancellation);
            if (result.ExitCode == 0)
            {
                return;
            }

            // 剛 bootout 完，launchd 還在收尾時會回 Input/output error：等一下再試。
            await environment.Delay(TimeSpan.FromMilliseconds(500), cancellation);
        }

        throw new CliException(ExitCodes.Error, $"launchctl 沒辦法載入啟動項目：{result.Output.Trim()}");
    }

    public async Task StopAsync(CancellationToken cancellation) =>
        await environment.RunCommand("launchctl", ["bootout", Target(await environment.GetUid(cancellation))], cancellation);

    public Task<bool> RemoveAsync(CancellationToken cancellation)
    {
        if (!File.Exists(PlistPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(PlistPath);
        return Task.FromResult(true);
    }

    public async Task<int?> QueryPidAsync(CancellationToken cancellation)
    {
        var result = await environment.RunCommand("launchctl", ["print", Target(await environment.GetUid(cancellation))], cancellation);
        return result.ExitCode == 0 ? ParsePid(result.Output) : null;
    }

    /// <summary>從 <c>launchctl print</c> 的輸出讀 <c>pid = 123</c>；沒在跑時沒有這一行。</summary>
    public static int? ParsePid(string output)
    {
        var match = PidLine().Match(output);
        return match.Success && int.TryParse(match.Groups[1].Value, out var pid) && pid > 0 ? pid : null;
    }

    public static string RenderPlist(AutostartSpec spec)
    {
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false };
        var buffer = new StringBuilder();
        using (var writer = new StringWriterWithEncoding(buffer))
        using (var xml = XmlWriter.Create(writer, settings))
        {
            xml.WriteStartDocument();
            xml.WriteDocType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null);
            xml.WriteStartElement("plist");
            xml.WriteAttributeString("version", "1.0");
            xml.WriteStartElement("dict");
            WriteString(xml, "Label", Label);
            WriteStrings(xml, "ProgramArguments", [spec.KumiPath, .. spec.Arguments]);
            xml.WriteElementString("key", "EnvironmentVariables");
            xml.WriteStartElement("dict");
            foreach (var (name, value) in spec.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                WriteString(xml, name, value);
            }

            xml.WriteEndElement();
            xml.WriteElementString("key", "RunAtLoad");
            xml.WriteElementString("true", string.Empty);
            WriteString(xml, "StandardOutPath", spec.LogPath);
            WriteString(xml, "StandardErrorPath", spec.LogPath);
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        return buffer.ToString();
    }

    private static string Target(string uid) => $"gui/{uid}/{Label}";

    private static void WriteString(XmlWriter xml, string key, string value)
    {
        xml.WriteElementString("key", key);
        xml.WriteElementString("string", value);
    }

    private static void WriteStrings(XmlWriter xml, string key, IEnumerable<string> values)
    {
        xml.WriteElementString("key", key);
        xml.WriteStartElement("array");
        foreach (var value in values)
        {
            xml.WriteElementString("string", value);
        }

        xml.WriteEndElement();
    }

    [GeneratedRegex(@"^\s*pid = (\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex PidLine();

    /// <summary>StringWriter 的編碼預設是 UTF-16，XML 宣告會寫成 utf-16：plist 要 UTF-8。</summary>
    private sealed class StringWriterWithEncoding(StringBuilder builder) : StringWriter(builder)
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
