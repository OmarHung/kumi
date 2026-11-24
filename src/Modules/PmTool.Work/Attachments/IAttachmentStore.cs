namespace PmTool.Work.Attachments;

/// <param name="SizeBytes">實際寫進去的位元組數。</param>
/// <param name="Sha256">內容的 SHA-256，小寫十六進位。</param>
internal sealed record StoredFile(long SizeBytes, string Sha256);

/// <summary>
/// 附件的檔案本身放哪裡。鍵是附件的編號（<see cref="Guid"/> 的文字），不是檔名：檔名可以隨便取，不能碰到儲存空間的路徑。
/// 同一個鍵只寫一次，之後只會讀與刪。
/// </summary>
internal interface IAttachmentStore
{
    /// <summary>
    /// 從串流寫進儲存空間，邊寫邊算大小與 SHA-256。內容超過 <paramref name="maxBytes"/> 時中止、清掉寫了一半的部分，回 null。
    /// 其他失敗（儲存空間壞了、串流中斷）丟例外，同樣不留下寫了一半的檔案。
    /// </summary>
    Task<StoredFile?> WriteAsync(string key, Stream content, string contentType, long maxBytes, CancellationToken cancellationToken);

    /// <summary>打開來讀；沒有這個檔案時是 null。呼叫端負責 Dispose。</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>刪除；本來就沒有時不算錯。</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>啟動時確認儲存空間用得了；不能用就丟例外，訊息說得出缺什麼。</summary>
    Task VerifyAsync(CancellationToken cancellationToken);
}
