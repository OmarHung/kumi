namespace PmTool.Ai.Attachments;

/// <summary>附件種類在資料庫與 transcript 裡的固定文字，以及各種類的上限。改名會讓舊資料讀不出來。</summary>
internal static class AttachmentKinds
{
    public const string Image = "image";

    public const string Pdf = "pdf";

    public const string Text = "text";

    public static string NameOf(AssistantAttachmentKind kind) => kind switch
    {
        AssistantAttachmentKind.Image => Image,
        AssistantAttachmentKind.Pdf => Pdf,
        AssistantAttachmentKind.Text => Text,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不認得的附件種類。"),
    };

    public static AssistantAttachmentKind Parse(string name) => name switch
    {
        Image => AssistantAttachmentKind.Image,
        Pdf => AssistantAttachmentKind.Pdf,
        Text => AssistantAttachmentKind.Text,
        _ => throw new FormatException($"不認得的附件種類：{name}"),
    };

    public static long MaxBytesOf(AssistantAttachmentKind kind) => kind switch
    {
        AssistantAttachmentKind.Image => AssistantAttachmentRules.MaxImageBytes,
        AssistantAttachmentKind.Pdf => AssistantAttachmentRules.MaxPdfBytes,
        _ => AssistantAttachmentRules.MaxTextBytes,
    };

    /// <summary>給使用者看的種類名稱，放在「圖片不能超過…」這類句子裡。</summary>
    public static string LabelOf(AssistantAttachmentKind kind) => kind switch
    {
        AssistantAttachmentKind.Image => "圖片",
        AssistantAttachmentKind.Pdf => "PDF",
        _ => "文字檔",
    };
}
