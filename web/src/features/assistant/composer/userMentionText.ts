const MENTION = /@\[([^\]]*)\]\(mention:[^)]*\)/g;

/** 使用者訊息裡的 @[名稱](mention:編號) 顯示與複製成 @名稱。 */
export function plainMentions(text: string): string {
  return text.replace(MENTION, '@$1');
}
