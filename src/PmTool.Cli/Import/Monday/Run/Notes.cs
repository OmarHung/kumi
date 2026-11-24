namespace PmTool.Cli.Import.Monday.Run;

/// <summary>一則問題或提醒。<see cref="Count"/> 是同一句話出現幾次（同一種對不上的標籤有幾個項目）。</summary>
internal sealed record Note(string Kind, string Message, int Count);

/// <summary>
/// 依序收集問題（擋下匯入）或提醒（照樣匯入、報告裡列出）：同一句話只留一筆、數次數，
/// 一次說完所有問題，不必一個一個修再重跑才看到下一個。
/// </summary>
internal sealed class Notes
{
    private readonly Dictionary<(string Kind, string Message), int> _counts = [];
    private readonly List<(string Kind, string Message)> _order = [];

    public bool Any => _order.Count > 0;

    public void Add(string kind, string message)
    {
        var key = (kind, message);
        if (_counts.TryGetValue(key, out var count))
        {
            _counts[key] = count + 1;
            return;
        }

        _counts[key] = 1;
        _order.Add(key);
    }

    public void AddMany(string kind, string message, int times)
    {
        for (var index = 0; index < times; index++)
        {
            Add(kind, message);
        }
    }

    public IReadOnlyList<Note> ToList() => [.. _order.Select(key => new Note(key.Kind, key.Message, _counts[key]))];
}

/// <summary>問題與提醒的種類（報告的 JSON 與測試都看它）。</summary>
internal static class NoteKinds
{
    public const string Mapping = "mapping";
    public const string Project = "project";
    public const string Type = "type";
    public const string State = "state";
    public const string Field = "field";
    public const string Person = "person";
    public const string Member = "member";
    public const string Server = "server";
    public const string Priority = "priority";
    public const string Value = "value";
    public const string Title = "title";
    public const string FallbackAuthor = "fallback_author";
    public const string Truncated = "truncated";
    public const string NotMoved = "not_moved";
    public const string Skipped = "skipped";
    public const string Followers = "followers";
    public const string Stale = "stale";
    public const string TitleRule = "title_rule";
    public const string Cycle = "cycle";
}
