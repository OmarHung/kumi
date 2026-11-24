using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using PmTool.Cli.Commands;
using static PmTool.Cli.Tests.UpdateCommandTests;

namespace PmTool.Cli.Tests;

/// <summary>kumi update 的防護：遠端 http、閒置逾時、下載中斷、Windows 改名的重試與還原、清理上次留下的檔案、真的試跑。</summary>
[TestFixture]
public sealed class UpdateRobustnessTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(200);

    private static void UseWindows(CliHarness cli) =>
        cli.Update = cli.Update with
        {
            Os = UpdateOs.Windows,
            OsArchitecture = Architecture.X64,
            ProcessArchitecture = Architecture.X64,
            ExecutablePath = cli.PathOf("install/kumi.exe"),
            MoveRetryDelay = TimeSpan.Zero,
        };

    private static CliHarness WindowsServer()
    {
        var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/cli", new { version = NewVersion, packages = new[] { Package("windows-x64", NewBody) } });
        cli.Server.On("GET", "/v1/cli/download/windows-x64", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(NewBody)),
        });
        UseWindows(cli);
        return cli;
    }

    private static void RespondWith(CliHarness cli, Stream stream) =>
        cli.Server.On("GET", "/v1/cli/download/macos-arm64", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        });

    // ---- 遠端的 http ----

    [TestCase("--insecure")]
    [TestCase("")]
    public async Task Update_RemoteHttpServer_RefusesToDownloadEvenWhenInsecureIsAllowed(string flag)
    {
        // Arrange
        using var cli = ServerWithUpdate();
        cli.Environment["KUMI_SERVER"] = "http://pm.example.com";
        cli.Environment["KUMI_ALLOW_INSECURE"] = "1";
        var path = Install(cli);
        UseSmokeVersion(cli);
        var args = flag.Length == 0 ? new[] { "update" } : new[] { "update", flag };

        // Act
        var result = await cli.RunAsync(args);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("https").And.Contain("換掉"));
            Assert.That(cli.Server.To("GET", "/v1/cli/download/macos-arm64"), Is.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo(OldBody));
        }
    }

    [Test]
    public async Task Update_RemoteHttpServer_CheckStillWorks()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        cli.Environment["KUMI_SERVER"] = "http://pm.example.com";
        Install(cli);

        // Act
        var result = await cli.RunAsync("update", "--check", "--insecure");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("有新版"));
        }
    }

    [TestCase("http://localhost:8080")]
    [TestCase("http://127.0.0.1:8080")]
    [TestCase("http://[::1]:8080")]
    public async Task Update_LocalHttpServer_UpdatesAsUsual(string server)
    {
        // Arrange
        using var cli = ServerWithUpdate();
        cli.Environment["KUMI_SERVER"] = server;
        var path = Install(cli);
        UseSmokeVersion(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.ReadAllText(path), Is.EqualTo(NewBody));
        }
    }

    // ---- 下載 ----

    [Test]
    public async Task Update_ContentSmallerThanListed_FailsAndCleansUp()
    {
        // Arrange
        using var cli = ServerWithUpdate(size: 1000);
        var path = Install(cli);
        UseSmokeVersion(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("大小和伺服器說的不一樣"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(OldBody));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Is.EqualTo([path]));
        }
    }

    [Test]
    public async Task Update_DownloadBreaksHalfway_FailsAndCleansUp()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        var path = Install(cli);
        UseSmokeVersion(cli);
        RespondWith(cli, new ScriptedStream(Encoding.UTF8.GetBytes("new exec"), then: () => throw new IOException("connection reset")));

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("連不上伺服器").And.Contain("connection reset"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(OldBody));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Is.EqualTo([path]));
        }
    }

    [Test]
    public async Task Update_NoDataForTooLong_FailsWithTheIdleTimeAndCleansUp()
    {
        // Arrange：收到一點資料之後就再也沒有消息。
        using var cli = ServerWithUpdate();
        var path = Install(cli);
        UseSmokeVersion(cli);
        cli.Update = cli.Update with { DownloadIdleTimeout = ShortTimeout };
        RespondWith(cli, new ScriptedStream(Encoding.UTF8.GetBytes("new exec"), then: null));

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("秒沒有收到伺服器").And.Contain("沒有更新"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(OldBody));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Is.EqualTo([path]));
        }
    }

    // ---- Windows 的改名 ----

    [Test]
    public async Task Update_OnWindowsWhenTheSecondRenameFails_PutsTheOldFileBack()
    {
        // Arrange：試跑時暫存檔被拿走（例如防毒軟體隔離），第二次改名必然失敗。
        using var cli = WindowsServer();
        var path = Install(cli);
        UseSmokeRun(cli, temp =>
        {
            File.Delete(temp);
            return $"kumi {NewVersion}";
        });

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("原本的 kumi 還在"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(OldBody));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Is.EqualTo([path]));
        }
    }

    // ---- 清理上次留下的 ----

    [Test]
    public async Task Update_OnWindows_DeletesOnlyTheExactLeftoverNames()
    {
        // Arrange
        using var cli = WindowsServer();
        var path = Install(cli);
        var folder = Path.GetDirectoryName(path)!;
        var hex = Guid.NewGuid().ToString("N");
        string[] gone = [$".kumi-update-{hex}.exe", $"{path}.old", $"{path}.{hex}.old"];
        string[] kept = [$"{path}.gold", $".kumi-update-{hex}", $".kumi-update-short.exe", $"{path}.old.txt", $"other.exe.{hex}.old"];
        foreach (var file in gone.Concat(kept))
        {
            File.WriteAllText(Path.Combine(folder, Path.GetFileName(file)), "x");
        }

        UseSmokeVersion(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert：.old 先刪掉，再由這次換下來的舊檔補上。
        var remaining = Directory.GetFiles(folder).Select(Path.GetFileName).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(remaining, Does.Not.Contain($".kumi-update-{hex}.exe"));
            Assert.That(remaining, Does.Not.Contain($"kumi.exe.{hex}.old"));
            Assert.That(remaining, Is.SupersetOf(kept.Select(Path.GetFileName)));
        }
    }

    [Test]
    public async Task Update_OnUnix_DeletesLeftoverTempFilesButNotOtherFiles()
    {
        // Arrange
        using var cli = ServerWithUpdate();
        var path = Install(cli);
        var folder = Path.GetDirectoryName(path)!;
        var hex = Guid.NewGuid().ToString("N");
        var leftover = Path.Combine(folder, $".kumi-update-{hex}");
        var other = Path.Combine(folder, $".kumi-update-{hex}.exe");
        var unrelated = Path.Combine(folder, "kumi.gold");
        File.WriteAllText(leftover, "x");
        File.WriteAllText(other, "x");
        File.WriteAllText(unrelated, "x");
        UseSmokeVersion(cli);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.Exists(leftover), Is.False);
            Assert.That(File.Exists(other), Is.True, "Unix 上 .exe 結尾的不是我們留下的");
            Assert.That(File.Exists(unrelated), Is.True);
        }
    }

    // ---- 真的試跑 ----

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static async Task<string?> RunScriptAsync(string body, Action? arrange = null)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"kumi-run-{Guid.NewGuid():N}")).FullName;
        try
        {
            var script = Path.Combine(folder, "fake-kumi");
            await File.WriteAllTextAsync(script, $"#!/bin/sh\n{body}\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            arrange?.Invoke();
            return await UpdateEnvironment.Current().SmokeRun(script, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    [Platform(Exclude = "Win")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task SmokeRun_ScriptPrintsAVersion_ReturnsTheTrimmedOutput()
    {
        // Arrange & Act
        var output = await RunScriptAsync("echo \"kumi 1.2.3\"");

        // Assert
        Assert.That(output, Is.EqualTo("kumi 1.2.3"));
    }

    [Test]
    [Platform(Exclude = "Win")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task SmokeRun_ScriptExitsWithOne_ReturnsNull()
    {
        // Arrange & Act
        var output = await RunScriptAsync("echo \"kumi 1.2.3\"\nexit 1");

        // Assert
        Assert.That(output, Is.Null);
    }

    [Test]
    [Platform(Exclude = "Win")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task SmokeRun_ChildProcess_DoesNotInheritTheTokens()
    {
        // Arrange
        string[] names = ["KUMI_TOKEN", "PM_TOKEN", "KUMI_MCP_TOKEN"];
        var before = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, "pmt_secret_value");
            }

            // Act
            var output = await RunScriptAsync("echo \"[$KUMI_TOKEN][$PM_TOKEN][$KUMI_MCP_TOKEN]\"");

            // Assert
            Assert.That(output, Is.EqualTo("[][][]"));
        }
        finally
        {
            for (var index = 0; index < names.Length; index++)
            {
                Environment.SetEnvironmentVariable(names[index], before[index]);
            }
        }
    }

    /// <summary>先給一段資料，之後要不丟例外，要不永遠沒有下文（直到被取消）。</summary>
    private sealed class ScriptedStream(byte[] first, Action? then) : Stream
    {
        private bool _sent;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                first.CopyTo(buffer);
                return first.Length;
            }

            if (then is not null)
            {
                then();
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
