/**
 * 指向時間軸上某一則留言的網址 hash（#comment-<活動編號>）：由留言產生的通知帶著它，
 * 項目頁與面板的時間軸讀它捲過去並閃一下（docs/design/08-interaction.md 收件匣）。
 */
const COMMENT_HASH_PREFIX = '#comment-';

/** 標出目標那一筆多久（毫秒）；和 CSS 的 --duration-target 同一個數字。 */
export const HIGHLIGHT_MS = 2400;

export function commentHash(commentId: string): string {
  return `${COMMENT_HASH_PREFIX}${commentId}`;
}

/** 從 location.hash 取出留言的編號；其他格式（或空的編號）是 null。 */
export function commentIdOfHash(hash: string): string | null {
  if (!hash.startsWith(COMMENT_HASH_PREFIX)) return null;
  const id = hash.slice(COMMENT_HASH_PREFIX.length);
  return id === '' ? null : id;
}

/**
 * 這個時間軸該不該回應 hash：面板裡的看網址的 item 參數是不是自己的項目，整頁的在面板沒開時才回應。
 * 網址是人打的，大小寫可能不同（smk-7）；項目編號不分大小寫，兩邊轉大寫再比。
 */
export function ownsCommentTarget(isInPanel: boolean, panelKey: string | null, itemKey: string): boolean {
  return isInPanel ? panelKey?.toUpperCase() === itemKey.toUpperCase() : panelKey === null;
}
