const MENTION = /@\[([^\]]*)\]\(mention:[^)]*\)/g;

/** 使用者訊息裡的 @[名稱](mention:編號) 顯示與複製成 @名稱。 */
export function plainMentions(text: string): string {
  return text.replace(MENTION, '@$1');
}

// 標題結尾停在提及中間（名稱已經完整、編號被切掉）。
const CUT_MENTION = /@\[([^\]]*)\]\(mention:[^)]*$/;

/**
 * 對話標題的顯示：照 plainMentions，另外處理結尾被截斷的提及。早期的標題是第一則訊息的前 40 個字，
 * 常常切在編號中間；伺服器之後才改成存 @名稱，舊的標題只能在顯示時整理。
 */
export function plainTitle(title: string): string {
  return plainMentions(title).replace(CUT_MENTION, '@$1');
}
