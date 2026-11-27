import type { AssistantConversationListing } from '../../api/aiTypes';

/** 選單最多列幾則（所有專案合計）；更舊的到整頁版的對話清單找。 */
export const MAX_LISTED_CONVERSATIONS = 50;

/** 目前的專案最多先保留幾則：別的專案最近很熱鬧時，目前這一則（打勾的那一則）也不會被擠出選單。 */
export const MAX_CURRENT_CONVERSATIONS = 20;

export interface ConversationGroup {
  /** null 是不選專案的組織對話。 */
  readonly projectKey: string | null;
  readonly conversations: readonly AssistantConversationListing[];
}

/**
 * 依專案分組：目前的專案（或不選專案）在最前面，其他的照各組最近一則的時間（伺服器的清單已經是新的在前）。
 * 先保留目前的專案最多 MAX_CURRENT_CONVERSATIONS 則，剩下的名額照時間給別的專案，合計最多 MAX_LISTED_CONVERSATIONS 則。
 */
export function groupConversations(all: readonly AssistantConversationListing[], current: string | null): readonly ConversationGroup[] {
  const mine = all.filter((conversation) => conversation.projectKey === current).slice(0, MAX_CURRENT_CONVERSATIONS);
  const others = all.filter((conversation) => conversation.projectKey !== current).slice(0, MAX_LISTED_CONVERSATIONS - mine.length);
  const order: (string | null)[] = [];
  const byScope = new Map<string | null, AssistantConversationListing[]>();
  for (const conversation of [...mine, ...others]) {
    if (!order.includes(conversation.projectKey)) order.push(conversation.projectKey);
    byScope.set(conversation.projectKey, [...(byScope.get(conversation.projectKey) ?? []), conversation]);
  }
  return order.map((projectKey) => ({ projectKey, conversations: byScope.get(projectKey) ?? [] }));
}
