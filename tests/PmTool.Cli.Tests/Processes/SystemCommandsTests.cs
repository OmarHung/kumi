using System.Diagnostics;
using PmTool.Cli.Agent.Autostart;
using PmTool.Cli.Processes;

namespace PmTool.Cli.Tests.Processes;

[TestFixture]
public sealed class SystemCommandsTests
{
    private const string SystemDirectory = @"C:\Windows\System32";

    private static readonly string[] UsedNames = ["open", "xdg-open", "id", "launchctl", "ps", "kill", "rundll32"];

    private static string? Resolve(SystemPlatform platform, string name, Func<string, bool>? exists = null) =>
        SystemCommands.Resolve(platform, name, exists ?? (_ => true), SystemDirectory);

    [TestCase("open", "/usr/bin/open")]
    [TestCase("id", "/usr/bin/id")]
    [TestCase("launchctl", "/bin/launchctl")]
    [TestCase("ps", "/bin/ps")]
    [TestCase("kill", "/bin/kill")]
    public void Resolve_OnMacOs_UsesTheFixedSystemLocation(string name, string expected)
    {
        var path = Resolve(SystemPlatform.MacOS, name);

        Assert.That(path, Is.EqualTo(expected));
    }

    [Test]
    public void Resolve_OnLinux_PicksTheFirstSystemFolderThatHasTheCommand()
    {
        var path = Resolve(SystemPlatform.Linux, "xdg-open", candidate => candidate == "/usr/local/bin/xdg-open" || candidate == "/bin/xdg-open");

        Assert.That(path, Is.EqualTo("/bin/xdg-open"));
    }

    [Test]
    public void Resolve_OnLinuxWithoutTheCommand_ReturnsNullInsteadOfABareName()
    {
        var path = Resolve(SystemPlatform.Linux, "xdg-open", _ => false);

        Assert.That(path, Is.Null);
    }

    [TestCase("rundll32", @"C:\Windows\System32\rundll32.exe")]
    public void Resolve_OnWindows_BuildsAFullPathUnderTheSystemFolders(string name, string expected)
    {
        var path = Resolve(SystemPlatform.Windows, name);

        Assert.That(path, Is.EqualTo(expected));
    }

    [Test]
    public void Resolve_OnWindowsWithAnEmptySystemFolder_ReturnsNullInsteadOfARootRelativePath()
    {
        var path = SystemCommands.Resolve(SystemPlatform.Windows, "rundll32", _ => true, string.Empty);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void Resolve_ForEveryNameKumiUses_GivesAnAbsolutePathOnEveryPlatform()
    {
        foreach (var platform in Enum.GetValues<SystemPlatform>())
        {
            foreach (var name in UsedNames)
            {
                var path = Resolve(platform, name);
                if (path is null)
                {
                    continue;
                }

                var rooted = platform == SystemPlatform.Windows ? path.Length > 2 && path[1] == ':' : Path.IsPathRooted(path);
                Assert.That(rooted, Is.True, $"{platform} {name} -> {path}");
            }
        }
    }

    [Test]
    public void Resolve_ForAnUnknownCommandOnMacOs_ReturnsNull()
    {
        Assert.That(Resolve(SystemPlatform.MacOS, "frobnicate"), Is.Null);
    }

    [TestCase("open")]
    [TestCase("./id")]
    [TestCase("")]
    [TestCase(@"\x.exe")]
    [TestCase("C:x.exe")]
    public void Start_WithAFileNameThatIsNotAbsolute_Throws(string fileName)
    {
        var info = new ProcessStartInfo(fileName) { UseShellExecute = false };

        Assert.That(() => SystemProcess.Start(info), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Start_WithUseShellExecute_Throws()
    {
        var info = new ProcessStartInfo("/usr/bin/true") { UseShellExecute = true };

        Assert.That(() => SystemProcess.Start(info), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task GetUid_WithAFakeIdInTheWorkingDirectory_DoesNotRunTheFake()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("只在 macOS 與 Linux 驗證。");
        }

        var folder = Directory.CreateTempSubdirectory("kumi-cwd-").FullName;
        var marker = Path.Combine(folder, "ran");
        var fake = Path.Combine(folder, "id");
        await File.WriteAllTextAsync(fake, $"#!/bin/sh\ntouch '{marker}'\necho 99999\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = folder;

            var uid = await AutostartSystem.GetUidAsync(CancellationToken.None);

            Assert.That(File.Exists(marker), Is.False);
            Assert.That(uid, Is.Not.EqualTo("99999"));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public async Task Run_WithABareName_ResolvesItToTheSystemLocationInsteadOfTheWorkingDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("只在 macOS 與 Linux 驗證。");
        }

        var result = await AutostartSystem.RunAsync("id", ["-u"], CancellationToken.None);

        Assert.That(result.ExitCode, Is.EqualTo(0));
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string file = "") => file;

    [TestCase("var p = new Process(); p.Start();")]
    [TestCase("var p = new Process{ StartInfo = info };")]
    [TestCase("var p = new Process\n{\n StartInfo = info };")]
    [TestCase("var p = new  Process ( );")]
    [TestCase("Process.Start(info);")]
    [TestCase("Process.Start (info);")]
    [TestCase("Process.Start\n(info);")]
    [TestCase("var s = new ProcessStartInfo(f) { UseShellExecute=true };")]
    [TestCase("info.UseShellExecute  =\n true;")]
    public void FindForbiddenLaunch_ForEachWayOfStartingAProcessDirectly_IsReported(string source)
    {
        Assert.That(SourceRules.FindForbiddenLaunch(source), Is.Not.Null);
    }

    [TestCase("using var p = SystemProcess.Start(info);")]
    [TestCase("var s = new ProcessStartInfo(f) { UseShellExecute = false };")]
    [TestCase("Process? process = null;")]
    [TestCase("var x = new ProcessRequest(a);")]
    public void FindForbiddenLaunch_ForAllowedCode_IsNotReported(string source)
    {
        Assert.That(SourceRules.FindForbiddenLaunch(source), Is.Null);
    }

    [Test]
    public void CliSources_StartProcessesOnlyThroughSystemProcess()
    {
        var testFile = ThisFile();
        // 建置輸出可能在 repo 之外，所以從這個測試檔往上找。
        var root = Path.GetDirectoryName(testFile);
        while (root is not null && !File.Exists(Path.Combine(root, "PmTool.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        if (root is null)
        {
            Assert.Ignore("找不到原始碼資料夾。");
        }

        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src", "PmTool.Cli"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && Path.GetFileName(file) != "SystemProcess.cs")
            .Where(file => SourceRules.FindForbiddenLaunch(File.ReadAllText(file)) is not null)
            .ToList();

        Assert.That(offenders, Is.Empty);
    }
}

internal static partial class SourceRules
{
    [System.Text.RegularExpressions.GeneratedRegex(@"new\s+Process\s*[({]|(?<!System)\bProcess\s*\.\s*Start\s*\(|UseShellExecute\s*=\s*true")]
    private static partial System.Text.RegularExpressions.Regex Forbidden();

    /// <summary>原始碼裡直接啟動程式的寫法（不經過 SystemProcess）；沒有回 null。</summary>
    public static string? FindForbiddenLaunch(string source) => Forbidden().Match(source) is { Success: true } match ? match.Value : null;
}
