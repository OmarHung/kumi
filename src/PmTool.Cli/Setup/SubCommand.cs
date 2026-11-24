using PmTool.Cli.Commands;

namespace PmTool.Cli.Setup;

/// <summary>精靈呼叫別的 kumi 指令（kumi agent enable、kumi agent start）：沿用同一個環境（經過 <see cref="Login.LoginEnvironment.RunCommand"/>，測試才換得掉），並把 --server 與 --insecure 帶過去。</summary>
internal static class SubCommand
{
    public static Task<int> RunAsync(CommandCall call, IReadOnlyList<string> command, string server)
    {
        List<string> arguments = [.. command, "--" + GlobalOptions.Server.Name, server];
        if (call.Arguments.Has(GlobalOptions.Insecure.Name))
        {
            arguments.Add("--" + GlobalOptions.Insecure.Name);
        }

        return call.Context.Login.RunCommand(arguments, call.Context, call.CancellationToken);
    }
}
