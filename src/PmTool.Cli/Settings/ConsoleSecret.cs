namespace PmTool.Cli.Settings;

/// <summary>在終端機讀一行不回顯的文字。</summary>
internal static class ConsoleSecret
{
    public static string? Read()
    {
        // 不是終端機時 ReadKey 會丟例外：改成告訴使用者怎麼做。
        if (Console.IsInputRedirected)
        {
            throw NotATerminal();
        }

        try
        {
            return ReadKeys();
        }
        catch (InvalidOperationException)
        {
            throw NotATerminal();
        }
    }

    private static CliException NotATerminal() =>
        CliException.Usage("沒有終端機可以輸入權杖。請加 --with-token，從標準輸入讀取（例如 echo $TOKEN | kumi auth login --with-token）。");

    private static string? ReadKeys()
    {
        var text = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return text.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0)
                {
                    text.Length--;
                }

                continue;
            }

            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                Console.Error.WriteLine();
                return null;
            }

            if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }
    }
}
