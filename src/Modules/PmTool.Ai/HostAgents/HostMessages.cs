namespace PmTool.Ai.HostAgents;

/// <summary>主機來源的回覆失敗時給使用者看的說明（繁體中文）。</summary>
internal static class HostMessages
{
    public const string QueueTimeout = "排隊超過 3 分鐘還沒輪到，請稍後再試。";

    public const string OutcomeUnknown =
        "主機代理中斷了回報，這則的結果不明：它可能已經提出了變更集或修改建議，請到變更集與項目確認；不會自動重跑。";

    public const string Timeout = "超過 5 分鐘，回覆中斷了。";

    public const string NotAMember = "發問的人已經不是組織的有效成員，這則沒有執行。";

    public const string Unreported = "主機上的引擎回報失敗，沒有說明原因。";

    public const string Vanished = "這則排隊的工作不見了（來源被關掉或代理被移除），沒有執行。";

    /// <summary>領走之後 worker 一次都沒回報（沒收到工作、剛領走就當掉）：引擎沒有跑過，所以不是「結果不明」，用量也退還。</summary>
    public const string NeverStarted = "主機代理沒有開始執行這則（可能沒收到），請再送一次；不會自動重跑。";

    private const string NotRunSuffix = "這則沒有執行。";

    private const string InterruptedSuffix = "這則回覆中斷了。";

    /// <summary>擁有者的動作停下的是執行中的工作時，它已經跑了一部分，不能說「沒有執行」。</summary>
    public static string Interrupted(string notRunMessage) =>
        notRunMessage.EndsWith(NotRunSuffix, StringComparison.Ordinal)
            ? notRunMessage[..^NotRunSuffix.Length] + InterruptedSuffix
            : notRunMessage;
}
