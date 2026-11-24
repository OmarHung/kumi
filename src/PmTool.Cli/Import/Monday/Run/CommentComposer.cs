namespace PmTool.Cli.Import.Monday.Run;

/// <summary>一個檔案搬到哪裡去了：<see cref="AttachmentId"/> 是 null 就是沒有搬。</summary>
internal sealed record AssetOutcome(string Name, Guid? AttachmentId);

/// <summary>把一則 monday 留言組成送給伺服器的內文（交接第 6.4 節）：HTML 轉 Markdown、沒被引用的檔案接在後面、原作者、長度上限。</summary>
internal sealed class CommentComposer(PersonDirectory people, Notes reminders)
{
    // 和上傳時的 content-type 同一份判斷：網頁只直接顯示這幾種圖片，其他當檔案下載。
    private static bool IsImage(string fileName) =>
        PmTool.Cli.Commands.AttachmentFiles.ContentTypeOf(fileName).StartsWith("image/", StringComparison.Ordinal);

    public string Compose(FlatComment comment, ResolvedPerson author, IReadOnlyDictionary<string, AssetOutcome> outcomes, string where)
    {
        var markdown = MondayHtml.ToMarkdown(comment.Html, new HtmlContext(
            userId => people.Match(userId) is { } person ? new MentionTarget(person.Id, person.DisplayName) : null,
            assetId => outcomes.TryGetValue(assetId, out var outcome) ? new ImageTarget(outcome.Name, outcome.AttachmentId) : null));

        var files = comment.Assets.Where(asset => !markdown.ReferencedAssetIds.Contains(asset.Id))
            .Select(asset => MondayHtml.FileMarkdown(
                asset.Name,
                outcomes.TryGetValue(asset.Id, out var outcome) ? outcome.AttachmentId : null,
                IsImage(asset.Name)))
            .ToArray();
        var body = string.Join("\n\n", new[] { markdown.Text, string.Join("\n", files) }.Where(part => part.Length > 0));
        if (body.Length == 0)
        {
            body = MarkdownText.EmptyComment;
        }

        if (author.UsedFallback)
        {
            body = $"（原作者：{MarkdownText.EscapeInline(MarkdownText.StripInvisible(author.OriginalName), atLineStart: false)}）\n\n{body}";
            reminders.Add(NoteKinds.FallbackAuthor, $"{author.OriginalName} 對不上這裡的人，留言改用 fallbackPerson（{people.FallbackEmail}）");
        }

        body = MarkdownText.Truncate(MarkdownText.Tidy(body), out var truncated);
        if (truncated)
        {
            reminders.Add(NoteKinds.Truncated, $"留言 {comment.Id}（{where}）超過 {MarkdownText.MaxCommentLength:N0} 字，已截斷");
        }

        return body;
    }
}
