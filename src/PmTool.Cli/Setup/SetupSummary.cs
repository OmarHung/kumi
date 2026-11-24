namespace PmTool.Cli.Setup;

/// <summary>精靈最後的總結：每一項一行，失敗的寫怎麼補。</summary>
internal static class SetupSummary
{
    public static async Task WriteAsync(TextWriter output, IReadOnlyList<SetupResult> results)
    {
        await output.WriteLineAsync();
        await output.WriteLineAsync("完成：");
        foreach (var result in results)
        {
            var mark = result.Status switch
            {
                SetupStatus.Done => "✓",
                SetupStatus.Failed => "✗",
                _ => "–",
            };
            await output.WriteLineAsync($"  {mark} {result.Name}　{result.Detail}");
        }

        await output.WriteLineAsync("回到網頁的「設定精靈」可以看到打勾的結果。");
    }
}
