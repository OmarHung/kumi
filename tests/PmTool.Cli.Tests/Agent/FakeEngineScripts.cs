using System.Diagnostics;

namespace PmTool.Cli.Tests.Agent;

/// <summary>
/// 假的 claude／codex：一支小的 bash 指令稿，印固定的事件，順便把收到的命令列、標準輸入、環境變數、工作目錄記到旁邊的資料夾。
/// 測試絕不呼叫真的引擎（會用掉使用者的訂閱額度）。只能在 Unix 上跑。
/// </summary>
internal sealed class FakeEngineScripts : IDisposable
{
    public FakeEngineScripts()
    {
        Directory = System.IO.Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"kumi-fake-engine-{Guid.NewGuid():N}")).FullName;
        Out = System.IO.Directory.CreateDirectory(Path.Combine(Directory, "out")).FullName;
    }

    public string Directory { get; }

    /// <summary>指令稿把它看到的東西寫到這裡。</summary>
    public string Out { get; }

    public string Read(string name) => File.ReadAllText(Path.Combine(Out, name));

    public bool Exists(string name) => File.Exists(Path.Combine(Out, name));

    public static string FixturePath(string fixture) => Path.Combine(AppContext.BaseDirectory, "Agent", "Fixtures", fixture);

    /// <summary>記錄一切、印固定的事件檔、用給的結束代碼結束。</summary>
    public string Recording(string name, string? fixture, int exitCode = 0, string beforeOutput = "", string afterOutput = "") =>
        Script(name, $$"""
            cat > "{{Out}}/stdin.txt"
            printf '%s\n' "$@" > "{{Out}}/args.txt"
            env | sort > "{{Out}}/env.txt"
            pwd > "{{Out}}/cwd.txt"
            ls -ld . | cut -c1-10 > "{{Out}}/cwd-mode.txt"
            ls -A | sort > "{{Out}}/cwd-files.txt"
            cp mcp.json "{{Out}}/mcp.json" 2>/dev/null
            cp system.md "{{Out}}/system.md" 2>/dev/null
            {{beforeOutput}}
            {{(fixture is null ? string.Empty : $"cat \"{FixturePath(fixture)}\"")}}
            {{afterOutput}}
            exit {{exitCode}}
            """);

    public string Script(string name, string body)
    {
        var path = Path.Combine(Directory, name);
        File.WriteAllText(path, "#!/bin/bash\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    public static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // 暫時資料夾刪不掉不影響測試。
        }
    }
}
