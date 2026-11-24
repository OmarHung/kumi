import type { Notification } from '../../api/types';

/** 一則通知的一句話（前面接做的人的名字）：「指派給你」「提及你」「留言了」「把狀態從「待辦」改成「審查中」」。 */
export function notificationSentence(notification: Notification): string {
  switch (notification.kind) {
    case 'assigned':
      return '指派給你';
    case 'mentioned':
      // 說明或留言裡的提及是同一種（合約的 kind 只有 mentioned），所以不說「在留言裡」。
      return '提及你';
    case 'commented':
      return '留言了';
    case 'replied':
      return '回覆了你的留言';
    case 'state_changed':
      return stateSentence(notification.fromStateName, notification.toStateName);
  }
}

function stateSentence(from: string | null, to: string | null): string {
  if (from !== null && to !== null) return `把狀態從「${from}」改成「${to}」`;
  return to === null ? '改了狀態' : `把狀態改成「${to}」`;
}

// 後端截斷摘要時可能剛好切在連結中間：「@[王小」「[設計稿](https://exa」。沒寫完的部分去掉，不要露出半截的符號。
const UNFINISHED_LINK = /@?\[[^\]]*(?:\]\([^)]*)?$/;
const MENTION_LINK = /@?\[([^\]]*)\]\(mention:[^)]*\)/g;
const ANY_LINK = /\[([^\]]*)\]\([^)]*\)/g;

/**
 * 摘要是說明或留言的開頭，寫的是 Markdown：收件匣只想看一行字，所以提及換成「@名稱」、連結只留文字、換行縮成空白
 * （其他的符號照原樣，不嘗試把整段 Markdown 排好）。沒有內容回 null。
 */
export function plainExcerpt(excerpt: string | null): string | null {
  if (excerpt === null) return null;
  const text = excerpt
    .replace(UNFINISHED_LINK, '')
    .replace(MENTION_LINK, (_whole, name: string) => `@${name}`)
    .replace(ANY_LINK, '$1')
    .replace(/\s+/g, ' ')
    .trim();
  return text === '' ? null : text;
}
