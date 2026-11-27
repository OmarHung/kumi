import type { QueryClient, QueryKey } from '@tanstack/react-query';

/** 串流事件裡看得到的那一點：工具的名稱與狀態。 */
interface ToolEventLike {
  readonly type: string;
  readonly tool?: { readonly name: string; readonly status: string };
}

/** 助理在這一輪撤回了某份建議（withdraw_proposal 成功）。 */
export function withdrewProposal(event: ToolEventLike): boolean {
  return event.type === 'tool' && event.tool?.name === 'withdraw_proposal' && event.tool.status === 'done';
}

// 各種建議卡片的查詢鍵都是 ['ai', '<種類>-proposals', 編號]（queryKeys.itemProposal 這一類）。
function isProposalQuery(key: QueryKey): boolean {
  return key[0] === 'ai' && typeof key[1] === 'string' && key[1].endsWith('-proposals');
}

/**
 * 撤回之後讓畫面上所有建議卡片重讀：工具段落沒有帶被撤回的是哪一份，不重讀的話前面那張卡片還留著「套用」按鈕，
 * 要重新整理頁面才會變成「已略過」。卡片只有幾張、各一個查詢，全部重讀也不貴。
 */
export function refreshAfterWithdraw(queryClient: QueryClient): Promise<void> {
  return queryClient.invalidateQueries({ predicate: (query) => isProposalQuery(query.queryKey) });
}
