import type { Member } from '../../../api/types';
import { linkText, type InsertedMention, type MentionTrigger } from '../../item-page/mentions/mentionText';

/** 這次在輸入框選過的人（名稱 → 編號）；送出時對應文字裡的 @名稱。 */
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

// 名稱後面接著文字或數字就是另一個名稱的延伸（@Ivan 不是 @Ivani），不算這個人。
const NAME_CONTINUATION = /[\p{L}\p{N}_]/u;

interface Replacement {
  readonly start: number;
  readonly end: number;
  readonly pick: MentionPick;
}

function overlaps(taken: readonly Replacement[], start: number, end: number): boolean {
  return taken.some((each) => start < each.end && each.start < end);
}

/** 文字裡這個名稱的每一處提及（由左到右），排除前面是 Email 的、後面是名稱延伸的、已被別的名稱佔走的區間。 */
function findMentions(text: string, name: string, taken: readonly Replacement[]): readonly number[] {
  const needle = `@${name}`;
  const found: number[] = [];
  let index = text.indexOf(needle);
  while (index >= 0) {
    const end = index + needle.length;
    const isEmailLike = index > 0 && EMAIL_PREFIX.test(text.charAt(index - 1));
    const isExtended = end < text.length && NAME_CONTINUATION.test(text.charAt(end));
    if (!isEmailLike && !isExtended && !overlaps(taken, index, end)) found.push(index);
    index = text.indexOf(needle, index + 1);
  }
  return found;
}

/**
 * 送出前把文字裡還在的 @名稱 換成 @[名稱](mention:編號)。
 * 較長的名稱先找（@Ivani 不會被 @Ivan 搶走）；每個名稱各自從頭找，選人的順序和文字順序無關；
 * 同名的幾個人照文字裡出現的順序對應，同一個人（編號相同）提及幾次就換幾處；使用者刪掉或改掉的找不到就略過。
 */
export function applyMentions(text: string, picks: readonly MentionPick[]): string {
  const names = [...new Set(picks.map((pick) => pick.displayName))].sort((a, b) => b.length - a.length);
  const taken: Replacement[] = [];
  for (const name of names) {
    const group = picks.filter((pick) => pick.displayName === name);
    const isOnePerson = group.every((pick) => pick.personId === group[0]?.personId);
    findMentions(text, name, taken).forEach((start, order) => {
      const pick = group[order] ?? (isOnePerson ? group[0] : undefined);
      if (pick !== undefined) taken.push({ start, end: start + 1 + name.length, pick });
    });
  }
  let result = '';
  let cursor = 0;
  for (const each of [...taken].sort((a, b) => a.start - b.start)) {
    result += `${text.slice(cursor, each.start)}@[${linkText(each.pick.displayName)}](mention:${each.pick.personId})`;
    cursor = each.end;
  }
  return result + text.slice(cursor);
}
