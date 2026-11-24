using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>
/// 項目的活動紀錄與留言。看得到項目（item.read）就看得到它的活動；留言要 comment.create。
/// 看不到的項目一律回 NotFound。
/// </summary>
public interface IItemActivityService
{
    Task<Result<ItemActivityPage>> ListAsync(Actor actor, string itemKey, ActivityQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 留言。內容去掉頭尾空白後不能是空的，長度上限見 <see cref="WorkRules.MaxCommentLength"/>，
    /// 可以有換行與 Tab，不能有其他控制字元。回傳的是這則留言的活動。
    /// <para>
    /// <paramref name="replyTo"/> 是要回覆的留言（活動的編號）：一定要是同一個項目的留言，否則是
    /// <see cref="WorkErrorCodes.CommentNotFound"/>（不存在、別的項目的、不是留言的活動都一樣，不透露別的項目有什麼）。
    /// 回覆只有一層：回覆一則回覆，等於回覆它的那一則最上層留言（<see cref="ItemActivity.ReplyToId"/> 一律是最上層的）。
    /// 那一則最上層留言已經刪除時是 <see cref="WorkErrorCodes.CommentDeleted"/>：復原之前這一串不能再回覆。
    /// </para>
    /// </summary>
    Task<Result<ItemActivity>> AddCommentAsync(
        Actor actor, string itemKey, string body, Guid? replyTo, CancellationToken cancellationToken);

    /// <summary>
    /// 編輯一則留言（含回覆），回傳編輯之後的樣子。只有作者本人能改（不是作者是 <see cref="WorkErrorCodes.CommentNotAuthor"/>），
    /// 而且現在還要有 comment.create（封存的專案沒有，回 project.archived）。內容的規則和 <see cref="AddCommentAsync"/> 一樣。
    /// <para>
    /// 正規化之後和現在的內容一樣時什麼都不改（不留版本、不寫事件），回傳現在的樣子。
    /// 改了的話，舊的內容留成一個版本（<see cref="ListCommentRevisionsAsync"/>），已經有 <see cref="WorkRules.MaxCommentRevisions"/>
    /// 個版本時是 <see cref="WorkErrorCodes.CommentTooManyEdits"/>。這一版新加的提及照留言的規則通知，原本就提及的不重發。
    /// 不動項目的 updatedAt，時間軸上的位置也不變（<see cref="ItemActivity.At"/> 是第一次留言的時間）。
    /// </para>
    /// 看不到項目是項目的 NotFound；留言不存在、不是這個項目的、不是留言是 <see cref="WorkErrorCodes.CommentNotFound"/>；
    /// 已經刪除是 <see cref="WorkErrorCodes.CommentDeleted"/>（先復原才能改）。
    /// </summary>
    Task<Result<ItemActivity>> EditCommentAsync(
        Actor actor, string itemKey, Guid commentId, string body, CancellationToken cancellationToken);

    /// <summary>
    /// 刪除一則留言（含回覆），回傳刪除之後的樣子（<see cref="ItemActivity.DeletedAt"/> 有值、<see cref="ItemActivity.Comment"/> 是 null）。
    /// 自己的留言要有 comment.create；別人的留言要有 project.manage（沒有是 <see cref="WorkErrorCodes.CommentNotAuthor"/>）。
    /// 封存的專案一律 project.archived。
    /// <para>
    /// 留言留在原位、顯示「已刪除」，內容與版本留在資料庫但不給任何人讀，<see cref="RestoreCommentAsync"/> 可以復原。
    /// 這則留言的通知一起刪掉（還沒寄的信不寄），復原也不會回來。底下的回覆留著；復原之前這一串不能再回覆。
    /// 留言裡貼的圖片與檔案是項目的附件，不會一起刪掉。
    /// </para>
    /// 找不到的規則和 <see cref="EditCommentAsync"/> 一樣。已經刪除的什麼都不做（不寫事件），回傳現在的樣子：重送不會失敗。
    /// </summary>
    Task<Result<ItemActivity>> DeleteCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken);

    /// <summary>
    /// 復原刪除的留言，回傳復原之後的樣子（內容、版本、「已編輯」都回來，位置不變）。
    /// 作者自己刪的：作者本人（要有 comment.create）或有 project.manage 的人能復原；別人刪的（管理的人刪的）只有 project.manage 能復原，
    /// 作者不能自己把它找回來。沒有權限是 <see cref="WorkErrorCodes.CommentNotAuthor"/>，封存的專案一律 project.archived。
    /// 刪除時清掉的通知不會回來，也不重新通知任何人。找不到的規則和 <see cref="EditCommentAsync"/> 一樣；
    /// 沒有刪除的什麼都不做（不寫事件），回傳現在的樣子。
    /// </summary>
    Task<Result<ItemActivity>> RestoreCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken);

    /// <summary>
    /// 一則留言的所有版本，新的在前（第一筆是現在的內容）；沒編輯過的只有一個版本。
    /// 看得到這則留言就看得到它的版本（和活動一樣，只看自己的角色也看得到自己看得到的項目的）。
    /// 找不到的規則和 <see cref="EditCommentAsync"/> 一樣；刪除的留言是 <see cref="WorkErrorCodes.CommentDeleted"/>（復原之前不給讀）。
    /// </summary>
    Task<Result<IReadOnlyList<CommentRevision>>> ListCommentRevisionsAsync(
        Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken);
}
