import type { Member } from '../../../api/types';
import { linkText, type InsertedMention, type MentionTrigger } from '../../item-page/mentions/mentionText';

/** 這次在輸入框選過的人（名稱 → 編號）；送出時照選的順序對應文字裡的 @名稱。 */
export interface MentionPick {
  readonly personId: string;
  readonly displayName: string;
}

/** 輸入框只寫 @名稱 加一個空白（原始語法太長，聊天的輸入框不適合）。 */
export function plainMentionInsert(value: string, trigger: MentionTrigger, member: Member): InsertedMention {
  const end = trigger.start + 1 + trigger.query.length;
  const mention = `@${member.displayName} `;
  return { value: `${value.slice(0, trigger.start)}${mention}${value.slice(end)}`, caret: trigger.start + mention.length };
}

// @ 前面是英數字、點、底線的是 Email，不是提及（和 findMentionTrigger 同一條規矩）。
const EMAIL_PREFIX = /[A-Za-z0-9_.@]/;

function findMention(text: string, name: string, from: number): number {
  let index = text.indexOf(`@${name}`, from);
  while (index > 0 && EMAIL_PREFIX.test(text.charAt(index - 1))) index = text.indexOf(`@${name}`, index + 1);
  return index;
}

/**
 * 送出前把文字裡還在的 @名稱 換成 @[名稱](mention:編號)：依選的順序一個一個往後找，
 * 使用者刪掉或改掉的找不到就略過（不換），同名的兩個人照順序對應。
 */
export function applyMentions(text: string, picks: readonly MentionPick[]): string {
  let result = '';
  let cursor = 0;
  for (const pick of picks) {
    const index = findMention(text, pick.displayName, cursor);
    if (index < 0) continue;
    result += `${text.slice(cursor, index)}@[${linkText(pick.displayName)}](mention:${pick.personId})`;
    cursor = index + 1 + pick.displayName.length;
  }
  return result + text.slice(cursor);
}
