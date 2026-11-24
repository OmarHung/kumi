namespace PmTool.Cli.Commands;

/// <summary>
/// 自己寫的指令列解析：支援 --名稱 值、--名稱=值、-x 值、重複的選項、-- 之後都是位置參數。
/// 指令只有兩層（群組與動作），規則固定，不值得為它引進一個套件。
/// </summary>
internal static class ArgumentParser
{
    public static ParsedArguments Parse(CommandSpec command, IReadOnlyList<string> tokens)
    {
        var known = command.Options.Concat(GlobalOptions.All).ToArray();
        var positionals = new List<string>();
        var values = new Dictionary<string, List<string>>();
        var onlyPositionals = false;

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (onlyPositionals || !IsOption(token))
            {
                positionals.Add(token);
                continue;
            }

            if (token == "--")
            {
                onlyPositionals = true;
                continue;
            }

            var (option, inlineValue) = Find(known, token, command);
            string? value = null;
            if (option.TakesValue)
            {
                value = inlineValue ?? (index + 1 < tokens.Count
                    ? tokens[++index]
                    : throw CliException.Usage($"--{option.Name} 後面要接{option.Value}。用法：{command.Usage}"));
            }
            else if (inlineValue is not null)
            {
                throw CliException.Usage($"--{option.Name} 不接值。");
            }

            if (!values.TryGetValue(option.Name, out var list))
            {
                values[option.Name] = list = [];
            }
            else if (!option.Repeatable && option.TakesValue)
            {
                throw CliException.Usage($"--{option.Name} 只能給一次。");
            }

            list.Add(value ?? string.Empty);
        }

        if (!values.ContainsKey(GlobalOptions.Help.Name)
            && (positionals.Count < command.MinArguments || positionals.Count > command.MaxArguments))
        {
            throw CliException.Usage($"參數的數量不對。用法：{command.Usage}");
        }

        return new ParsedArguments(positionals, values);
    }

    /// <summary>單獨的 - 是標準輸入，不是選項；負數不會出現在位置參數，所以以 - 開頭的都當選項。</summary>
    private static bool IsOption(string token) => token.Length > 1 && token[0] == '-';

    private static (OptionSpec Option, string? InlineValue) Find(IEnumerable<OptionSpec> known, string token, CommandSpec command)
    {
        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var body = token[2..];
            var equals = body.IndexOf('=');
            var name = equals < 0 ? body : body[..equals];
            var match = known.FirstOrDefault(option => option.Name == name)
                ?? throw CliException.Usage($"不認得的選項 --{name}。用法：{command.Usage}");
            return (match, equals < 0 ? null : body[(equals + 1)..]);
        }

        var shortName = token[1];
        var shortMatch = known.FirstOrDefault(option => option.Short == shortName)
            ?? throw CliException.Usage($"不認得的選項 -{shortName}。用法：{command.Usage}");
        return (shortMatch, token.Length > 2 ? token[2..] : null);
    }
}
