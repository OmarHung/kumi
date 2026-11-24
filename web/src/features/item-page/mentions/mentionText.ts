import type { Member } from '../../../api/types';

/** 游標前面正在打的提及：start 是 @ 的位置，query 是 @ 後面打的字。 */
export interface MentionTrigger {
  readonly start: number;
  readonly query: string;
}

/** 提及的搜尋字最長這麼多字；再長多半不是在找人（也避免每打一個字都掃整段文字）。 */
export const MAX_MENTION_QUERY = 30;

// @ 前面是開頭、空白、標點或中文都算（「請@小明」）；前面是英數字、點、底線的是 Email，不是提及。
// @ 後面到游標之間不能有換行、@、方括號與圓括號（已經是 @[名稱](mention:…) 的連結了）；空白最多一個，而且前面要有字
// （「John Smith」找得到人，「@ 範」不是在找人）。
const WORD = `[^\\s@\\[\\]()]`;
const MAX = String(MAX_MENTION_QUERY);
const TRIGGER = new RegExp(`(?:^|[^A-Za-z0-9_.@])@(${WORD}{1,${MAX}} ${WORD}{0,${MAX}}|${WORD}{0,${MAX}})$`);

/** 游標前面是不是正在打提及；只看游標前面的字。 */
export function findMentionTrigger(value: string, caret: number): MentionTrigger | null {
  const before = value.slice(0, caret);
  const match = TRIGGER.exec(before);
  if (match === null) return null;
  const query = match[1] ?? '';
  return { start: caret - query.length - 1, query };
}

/** 名稱包含打的字的成員，不分大小寫；開頭符合的排在前面，同一組維持原本的順序。 */
export function filterMembers(members: readonly Member[], query: string): readonly Member[] {
  const needle = query.toLowerCase();
  if (needle === '') return members;
  const names = members.map((member) => ({ member, name: member.displayName.toLowerCase() }));
  const matching = names.filter((entry) => entry.name.includes(needle));
  return [...matching.filter((entry) => entry.name.startsWith(needle)), ...matching.filter((entry) => !entry.name.startsWith(needle))].map(
    (entry) => entry.member,
  );
}

const BREAKS_LINK = /[[\]\\\r\n`*_<]/g;

/** 名稱放進連結文字：方括號、反斜線、換行，以及會開出行內程式碼、粗斜體、HTML 的 ` * _ <，都會弄壞後面的連結寫法，拿掉。 */
function linkText(displayName: string): string {
  const text = displayName.replace(BREAKS_LINK, '').trim();
  return text === '' ? '成員' : text;
}

export interface InsertedMention {
  readonly value: string;
  /** 插入之後游標的位置（提及後面的空白之後）。 */
  readonly caret: number;
}

/** 把「@ 加打的字」換成 @[名稱](mention:編號) 與一個空白；後面接著的字不動。 */
export function insertMention(value: string, trigger: MentionTrigger, member: Member): InsertedMention {
  const end = trigger.start + 1 + trigger.query.length;
  const mention = `@[${linkText(member.displayName)}](mention:${member.personId}) `;
  return { value: `${value.slice(0, trigger.start)}${mention}${value.slice(end)}`, caret: trigger.start + mention.length };
}
