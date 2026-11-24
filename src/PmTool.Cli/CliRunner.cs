using PmTool.Cli.Commands;

namespace PmTool.Cli;

/// <summary>進入點：找出要跑的指令、解析參數、執行，並把失敗統一寫到標準錯誤、換成結束代碼。</summary>
public static class CliRunner
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, CliContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);
        context = context.WithSafeOutput();
        try
        {
            return await DispatchAsync(args, context, cancellationToken);
        }
        catch (CliException exception)
        {
            await context.Error.WriteLineAsync($"錯誤：{exception.Message}");
            foreach (var (path, message) in exception.Details)
            {
                // 細項和主要訊息一樣的話，已經說過了。
                if (message == exception.Message)
                {
                    continue;
                }

                await context.Error.WriteLineAsync(path.Length == 0 ? $"  {message}" : $"  {path}：{message}");
            }

            return exception.ExitCode;
        }
        catch (OperationCanceledException)
        {
            await context.Error.WriteLineAsync("已取消。");
            return ExitCodes.Error;
        }
        catch (Exception exception)
        {
            // 只印例外的型別：訊息與堆疊可能帶著伺服器回的內容、路徑，甚至權杖。
            await context.Error.WriteLineAsync($"發生預期之外的錯誤（{exception.GetType().Name}）。這是 kumi 的問題，請回報。");
            return ExitCodes.Error;
        }
    }

    private static async Task<int> DispatchAsync(IReadOnlyList<string> args, CliContext context, CancellationToken cancellationToken)
    {
        if (args.Count == 0 || args[0] is "--help" or "-h" or "help")
        {
            await HelpText.WriteOverviewAsync(context.Out);
            return ExitCodes.Success;
        }

        if (args[0] == "--version")
        {
            await context.Out.WriteLineAsync(HelpText.Version);
            return ExitCodes.Success;
        }

        var group = args[0];
        var known = CommandRegistry.All.Where(command => command.Group == group).ToArray();
        if (known.Length == 0)
        {
            throw CliException.Usage($"不認得的指令 {group}。執行 kumi --help 看有哪些指令。");
        }

        var (command, skip) = Resolve(known, args);
        if (command is null)
        {
            // 群組後面沒有指令，或只有指令的前半（kumi import monday）：列出這個群組的指令。
            var isPrefix = args.Count > 1 && known.Any(candidate => candidate.Name.StartsWith(args[1] + " ", StringComparison.Ordinal));
            if (args.Count == 1 || args[1] is "--help" or "-h" || (isPrefix && (args.Count == 2 || args[2] is "--help" or "-h")))
            {
                await HelpText.WriteGroupAsync(context.Out, group, known);
                return ExitCodes.Success;
            }

            if (isPrefix)
            {
                throw CliException.Usage($"kumi {group} {args[1]} 沒有 {args[2]} 這個指令。執行 kumi {group} --help 看有哪些。");
            }

            throw CliException.Usage($"kumi {group} 沒有 {args[1]} 這個指令。執行 kumi {group} --help 看有哪些。");
        }

        var parsed = ArgumentParser.Parse(command, [.. args.Skip(skip)]);
        if (parsed.Has(GlobalOptions.Help.Name))
        {
            await HelpText.WriteCommandAsync(context.Out, command);
            return ExitCodes.Success;
        }

        return await command.Run(new CommandCall(context, parsed, cancellationToken));
    }

    /// <summary>
    /// 找出指令與它佔掉幾個詞。沒有第二層的指令（kumi search 文字）群組後面接的就是參數；
    /// 名稱可以是兩個詞（kumi import monday fetch），先比兩個詞再比一個詞，所以原本只有一個詞的指令不受影響。
    /// </summary>
    private static (CommandSpec? Command, int Skip) Resolve(CommandSpec[] known, IReadOnlyList<string> args)
    {
        if (known.FirstOrDefault(candidate => candidate.Name.Length == 0) is { } direct)
        {
            return (direct, 1);
        }

        if (args.Count >= 3 && known.FirstOrDefault(candidate => candidate.Name == $"{args[1]} {args[2]}") is { } twoWords)
        {
            return (twoWords, 3);
        }

        return args.Count >= 2 && known.FirstOrDefault(candidate => candidate.Name == args[1]) is { } oneWord
            ? (oneWord, 2)
            : (null, 0);
    }
}
