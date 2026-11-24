using System.Text;
using PmTool.Cli;
using PmTool.Cli.Api;
using PmTool.Cli.Settings;

// 不用最上層陳述式：那會產生一個全域的 Program 類別，和 PmTool.Api 的 Program 在同一個測試專案裡撞名。
internal static class PmEntryPoint
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        using var http = new HttpClient(HttpSettings.CreateHandler()) { Timeout = HttpSettings.ClientTimeout };
        var context = new CliContext
        {
            Http = http,
            Out = Console.Out,
            Error = Console.Error,
            In = Console.In,
            RawInput = Console.OpenStandardInput(),
            ConfigPath = ConfigStore.DefaultPath(Environment.GetEnvironmentVariable),
            GetEnvironmentVariable = Environment.GetEnvironmentVariable,
            IsInteractive = !Console.IsInputRedirected && !Console.IsOutputRedirected,
            ReadSecret = ConsoleSecret.Read,
            RegisterInterrupt = ConsoleInterrupt.Register,
        };

        return await CliRunner.RunAsync(args, context, CancellationToken.None);
    }
}
