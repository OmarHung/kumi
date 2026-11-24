using PmTool.Cli.Commands;

namespace PmTool.Cli.Setup;

/// <summary>精靈的是非題：Enter 用預設、<c>y</c>／<c>是</c> 是、<c>n</c>／<c>否</c> 否，其他的重問。</summary>
internal sealed class SetupPrompt(CliContext context, CancellationToken cancellation)
{
    private static readonly string[] Yes = ["y", "yes", "是"];
    private static readonly string[] No = ["n", "no", "否"];

    public async Task<bool> AskAsync(string question, bool defaultYes = true)
    {
        var hint = defaultYes ? "[Y/n]" : "[y/N]";
        while (true)
        {
            await context.Out.WriteAsync($"{question}{hint} ");
            // 輸入結束（例如管線關了）不能當成一直答錯而無限重問。
            var line = await context.In.ReadLineAsync(cancellation)
                ?? throw new CliException(ExitCodes.Error, "已取消（沒有收到回答）。");
            var answer = line.Trim().ToLowerInvariant();
            if (answer.Length == 0)
            {
                return defaultYes;
            }

            if (Yes.Contains(answer))
            {
                return true;
            }

            if (No.Contains(answer))
            {
                return false;
            }

            await context.Out.WriteLineAsync("請回答 y（是）或 n（否），直接按 Enter 是預設。");
        }
    }

    public async Task<string> AskTextAsync(string question)
    {
        await context.Out.WriteAsync(question);
        return (await context.In.ReadLineAsync(cancellation))?.Trim()
            ?? throw new CliException(ExitCodes.Error, "已取消（沒有收到回答）。");
    }
}
