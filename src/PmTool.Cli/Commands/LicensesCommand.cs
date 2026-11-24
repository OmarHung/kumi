namespace PmTool.Cli.Commands;

/// <summary>
/// kumi licenses：印出 Kumi 的授權、NOTICE 與第三方聲明。
/// 單一執行檔下載時只有一個檔案，沒辦法另外附文件，而裡面的套件（MIT、Apache-2.0 這類）都要求散布時附上條文與版權聲明，所以內嵌在執行檔裡。
/// </summary>
internal static class LicensesCommand
{
    // 順序就是印出來的順序。名稱和 PmTool.Cli.csproj 裡的 LogicalName 是同一組。
    private static readonly IReadOnlyList<string> Documents = ["Kumi.LICENSE", "Kumi.NOTICE", "Kumi.THIRD-PARTY-NOTICES"];

    private static readonly string Separator = new('=', 72);

    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("licenses", string.Empty, "顯示 Kumi 的授權，以及用到的第三方套件與它們的授權", "kumi licenses", [], 0, 0, RunAsync),
    ];

    private static async Task<int> RunAsync(CommandCall call)
    {
        foreach (var name in Documents)
        {
            var text = await ReadAsync(name, call.CancellationToken);
            await call.Out.WriteLineAsync(text.TrimEnd());
            await call.Out.WriteLineAsync();
            await call.Out.WriteLineAsync(Separator);
            await call.Out.WriteLineAsync();
        }

        return ExitCodes.Success;
    }

    private static async Task<string> ReadAsync(string name, CancellationToken cancellationToken)
    {
        await using var stream = typeof(LicensesCommand).Assembly.GetManifestResourceStream(name)
            ?? throw new CliException(ExitCodes.Error, $"這份 kumi 沒有內含 {name}：建置時少了檔案。授權的全文在 Kumi 的原始碼裡。");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
