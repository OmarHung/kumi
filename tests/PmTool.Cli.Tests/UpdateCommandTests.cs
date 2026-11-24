using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PmTool.Cli.Commands;

namespace PmTool.Cli.Tests;

/// <summary>kumi update：版本比對、選平台、下載核對、試跑與換掉自己（Windows 的改名流程也在 macOS 上測）。</summary>
[TestFixture]
public sealed class UpdateCommandTests
{
    internal const string NewVersion = "99.0.0";
    internal const string OldBody = "old executable";
    internal const string NewBody = "new executable content";

    private static string Current => HelpText.VersionNumber;

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static object Package(string platform, string body, string? sha = null, long? size = null) => new
    {
        platform,
        fileName = $"kumi-{NewVersion}-{platform}",
        sizeBytes = size ?? Encoding.UTF8.GetByteCount(body),
        sha256 = sha ?? Sha(body),
    };

    /// <summary>伺服器提供新版：目錄加上 macos-arm64 的下載。</summary>
    internal static CliHarness ServerWithUpdate(string body = NewBody, string? sha = null, long? size = null, string version = NewVersion)
    {
        var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/cli", new { version, packages = new[] { Package("macos-arm64", body, sha, size) } });
        cli.Server.On("GET", "/v1/cli/download/macos-arm64", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
        });
        return cli;
    }

    internal static string Install(CliHarness cli)
    {
        var path = cli.Update.ExecutablePath!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, OldBody);
        return path;
    }

    internal static void UseSmokeRun(CliHarness cli, Func<string, string?> run) =>
        cli.Update = cli.Update with { SmokeRun = (path, _) => Task.FromResult(run(path)) };

    internal static void UseSmokeVersion(CliHarness cli) =>
        UseSmokeRun(cli, _ => $"kumi {NewVersion}");

    // ---- 版本比對 ----

    [Test]
    public async Task Update_ServerHasSameVersion_SaysAlreadyLatestWithoutDownloading()
    {
        // Arrange
        using var cli = ServerWithUpdate(version: Current);
        Install(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain($"已經是最新版（kumi {Current}）。"));
            Assert.That(cli.Server.Requests.Select(request => request.Path), Is.EqualTo(["/v1/cli"]));
        }
    }

    [Test]
    public async Task Update_ServerVersionIsOlder_SaysSoAndSucceeds()
    {
        // Arrange
        using var cli = ServerWithUpdate(version: "0.0.1");
        Install(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("伺服器上的是 0.0.1，比這份（" + Current + "）舊，不更新。"));
            Assert.That(File.ReadAllText(cli.Update.ExecutablePath!), Is.EqualTo(OldBody));
        }
    }

    [Test]
    public async Task Update_VersionsAreComparedByNumber_NotByString()
    {
        // Arrange：字串比的話 "99.0.0" 小於 "9.0.0" 之外的都會錯；這裡用 10 比 9。
        using var cli = ServerWithUpdate(version: "10.0.0");
        Install(cli);
        UseSmokeRun(cli, _ => "kumi 10.0.0");

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        Assert.That(result.Out, Does.Contain("已更新到 kumi 10.0.0"));
    }

    [Test]
    public async Task Update_ServerOffersNoDownload_FailsWithAMessage()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/cli", new { version = (string?)null, packages = Array.Empty<object>() });

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("沒有提供 kumi 的下載"));
        }
    }

    // ---- --check ----

    [Test]
    public async Task Update_Check_ReportsNewVersionWithoutDownloading()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        Install(cli);

        // Act
        var result = await cli.RunAsync("update", "--check");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain($"有新版：{NewVersion}（目前是 {Current}）。執行 kumi update 更新。"));
            Assert.That(cli.Server.Requests, Has.Count.EqualTo(1));
            Assert.That(File.ReadAllText(cli.Update.ExecutablePath!), Is.EqualTo(OldBody));
        }
    }

    [Test]
    public async Task Update_NotStandalone_CheckStillWorksButUpdateExplainsDotnetTool()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        cli.Update = cli.Update with { IsStandalone = false };

        // Act
        var check = await cli.RunAsync("update", "--check");
        var update = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(check.ExitCode, Is.EqualTo(ExitCodes.Success), check.Error);
            Assert.That(check.Out, Does.Contain("有新版"));
            Assert.That(update.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(update.Error, Does.Contain("dotnet tool update --global PmTool.Cli"));
            Assert.That(cli.Server.To("GET", "/v1/cli/download/macos-arm64"), Is.Empty);
        }
    }

    // ---- 選平台 ----

    [Test]
    public async Task Update_NoPackageForThisPlatform_Fails()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        cli.Update = cli.Update with { OsArchitecture = Architecture.X64, ProcessArchitecture = Architecture.X64 };

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("沒有提供適合這台電腦的 kumi"));
        }
    }

    [Test]
    public async Task Update_UnsupportedOperatingSystem_Fails()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        cli.Update = cli.Update with { Os = UpdateOs.Other };

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("只有 macOS 與 Windows"));
        }
    }

    [Test]
    public async Task Update_WindowsOnArm_FallsBackToTheProcessArchitecture()
    {
        // Arrange：ARM 的 Windows 電腦跑的是 x64 的程序，伺服器只有 windows-x64。
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/cli", new { version = NewVersion, packages = new[] { Package("windows-x64", NewBody) } });
        cli.Server.On("GET", "/v1/cli/download/windows-x64", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(NewBody)),
        });
        cli.Update = cli.Update with
        {
            Os = UpdateOs.Windows,
            OsArchitecture = Architecture.Arm64,
            ProcessArchitecture = Architecture.X64,
            ExecutablePath = cli.PathOf("install/kumi.exe"),
        };
        Install(cli);
        UseSmokeVersion(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(cli.Server.To("GET", "/v1/cli/download/windows-x64"), Has.Count.EqualTo(1));
        }
    }

    // ---- 下載與核對 ----

    [Test]
    public async Task Update_HashDiffers_KeepsTheOriginalAndRemovesTheTempFile()
    {
        // Arrange
        using var cli = ServerWithUpdate(sha: new string('a', 64));
        var path = Install(cli);
        UseSmokeVersion(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("sha256"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(OldBody));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Is.EqualTo([path]));
        }
    }

    [Test]
    public async Task Update_ContentLargerThanListed_StopsAndCleansUp()
    {
        // Arrange：目錄說 5 個位元組，實際給的比較多。
        using var cli = ServerWithUpdate(size: 5);
        var path = Install(cli);
        UseSmokeVersion(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("比伺服器說的大"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(OldBody));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Is.EqualTo([path]));
        }
    }

    [Test]
    public async Task Update_SmokeRunReportsAnotherVersion_KeepsTheOriginal()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        var path = Install(cli);
        UseSmokeRun(cli, _ => "kumi 1.2.3");

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("跑不起來"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(OldBody));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Is.EqualTo([path]));
        }
    }

    [Test]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Update_NotAllowedToWriteTheFolder_ExplainsThePermissionProblem()
    {
        // Arrange
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Ignore("需要能擋住寫入的檔案系統權限。");
        }

        using var cli = ServerWithUpdate();
        var path = Install(cli);
        var folder = Path.GetDirectoryName(path)!;
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            // Act
            var result = await cli.RunAsync("update");

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
                Assert.That(result.Error, Does.Contain("沒有權限寫入").And.Contain("~/.local/bin"));
            }
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // ---- 換掉 ----

    [Test]
    public async Task Update_Succeeds_ReplacesTheFileWithExecutablePermissionAndLeavesNoTempFile()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        var path = Install(cli);
        string? tempSeen = null;
        UseSmokeRun(cli, temp =>
        {
            tempSeen = temp;
            return $"kumi {NewVersion}";
        });

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain($"已更新到 kumi {NewVersion}（原本是 {Current}）。"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(NewBody));
            Assert.That(OperatingSystem.IsWindows() || File.GetUnixFileMode(path) == (UnixFileMode)Convert.ToInt32("755", 8), Is.True, "Unix 上是 755");
            Assert.That(Path.GetDirectoryName(tempSeen), Is.EqualTo(Path.GetDirectoryName(path)), "暫存檔和目標在同一個資料夾");
            Assert.That(Path.GetFileName(tempSeen), Does.StartWith("."));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Is.EqualTo([path]));
        }
    }

    [Test]
    public async Task Update_ExecutableIsASymbolicLink_ReplacesTheTargetAndKeepsTheLink()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows 建符號連結需要特別的權限。");
        }

        using var cli = ServerWithUpdate();
        var real = cli.PathOf("real/kumi");
        Directory.CreateDirectory(Path.GetDirectoryName(real)!);
        File.WriteAllText(real, OldBody);
        var link = cli.Update.ExecutablePath!;
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, real);
        UseSmokeVersion(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.ReadAllText(real), Is.EqualTo(NewBody));
            Assert.That(new FileInfo(link).LinkTarget, Is.EqualTo(real));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(real)!), Is.EqualTo([real]));
        }
    }

    [Test]
    public async Task Update_OnWindows_RenamesTheOldFileAndUsesAnExeTempFile()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/cli", new { version = NewVersion, packages = new[] { Package("windows-x64", NewBody) } });
        cli.Server.On("GET", "/v1/cli/download/windows-x64", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(NewBody)),
        });
        cli.Update = cli.Update with
        {
            Os = UpdateOs.Windows,
            OsArchitecture = Architecture.X64,
            ProcessArchitecture = Architecture.X64,
            ExecutablePath = cli.PathOf("install/kumi.exe"),
        };
        var path = Install(cli);
        var leftover = $"{path}.old";
        File.WriteAllText(leftover, "from the previous update");
        string? tempSeen = null;
        UseSmokeRun(cli, temp =>
        {
            tempSeen = temp;
            return $"kumi {NewVersion}";
        });

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(tempSeen, Does.EndWith(".exe"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(NewBody));
            Assert.That(File.ReadAllText(leftover), Is.EqualTo(OldBody), "上次留下的 .old 先刪掉，這次換下來的舊檔改成 .old");
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!).Order(), Is.EqualTo(new[] { path, leftover }.Order()));
        }
    }

    // ---- 輸出與請求 ----

    [Test]
    public async Task Update_Json_HasTheDocumentedShape()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        Install(cli);
        UseSmokeVersion(cli);

        // Act
        var updated = await cli.RunAsync("update", "--json");
        var check = await cli.RunAsync("update", "--check", "--json");

        // Assert
        var first = JsonNode.Parse(updated.Out)!;
        var second = JsonNode.Parse(check.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first["current"]!.GetValue<string>(), Is.EqualTo(Current));
            Assert.That(first["latest"]!.GetValue<string>(), Is.EqualTo(NewVersion));
            Assert.That(first["updateAvailable"]!.GetValue<bool>(), Is.True);
            Assert.That(first["updated"]!.GetValue<bool>(), Is.True);
            Assert.That(first["standalone"]!.GetValue<bool>(), Is.True);
            Assert.That(second["updateAvailable"]!.GetValue<bool>(), Is.True);
            Assert.That(second["updated"]!.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public async Task Update_Requests_CarryTheTokenAndTheKumiCliUserAgent()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        Install(cli);
        UseSmokeVersion(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(cli.Server.Requests, Has.Count.EqualTo(2));
        foreach (var request in cli.Server.Requests)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(request.Authorization, Is.EqualTo($"Bearer {CliHarness.Token}"));
                Assert.That(request.UserAgent, Does.Contain("kumi-cli"));
            }
        }
    }

    [Test]
    public async Task Help_ListsTheUpdateCommand()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync("--help");

        // Assert
        Assert.That(result.Out, Does.Contain("update").And.Contain("下載新版的 kumi"));
    }
}
