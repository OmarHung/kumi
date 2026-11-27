import { useQuery, type QueryClient } from '@tanstack/react-query';
import type { ActionKind, ActionProposal } from './aiTypes';
import { apiRequest } from './http';
import { pollWhileApplying, useProposalDecision } from './proposalDecision';
import { queryKeys } from './queryKeys';

const proposalPath = (proposalId: string) => `/v1/ai/action-proposals/${encodeURIComponent(proposalId)}`;

/** 卡片輸入的值：key 對應 ActionInput.key。只在按下套用的那一刻送出，不進快取。 */
export type ActionInputValues = Readonly<Record<string, string>>;

/** 404 ai.action_proposal_not_found：別人的、不存在的。 */
export function getActionProposal(proposalId: string, signal?: AbortSignal): Promise<ActionProposal> {
  return apiRequest<ActionProposal>(proposalPath(proposalId), { signal });
}

/** 409 ai.action_proposal_closed；第一步就被拒絕時錯誤原樣回來（建議維持待處理）。 */
export function applyActionProposal(proposalId: string, inputs: ActionInputValues): Promise<ActionProposal> {
  return apiRequest<ActionProposal>(`${proposalPath(proposalId)}/apply`, { method: 'POST', body: { inputs } });
}

/** 409 ai.action_proposal_closed。 */
export function dismissActionProposal(proposalId: string): Promise<ActionProposal> {
  return apiRequest<ActionProposal>(`${proposalPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

export function useActionProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.actionProposal(proposalId),
    queryFn: ({ signal }) => getActionProposal(proposalId, signal),
    refetchInterval: pollWhileApplying,
  });
}

function invalidateWhere(queryClient: QueryClient, predicate: (key: readonly unknown[]) => boolean): void {
  void queryClient.invalidateQueries({ predicate: ({ queryKey }) => predicate(queryKey) });
}

/** 步驟只有文字，不知道是哪個項目或頁面：附件與活動的快取（項目底下的 attachments、activity）整批失效。 */
const isItemAttachmentOrActivity = (key: readonly unknown[]) => key[0] === 'item' && (key[2] === 'attachments' || key[2] === 'activity');
/** 項目詳情（['item', 編號]）：附件清單與數量跟著項目走。 */
const isItemDetail = (key: readonly unknown[]) => key[0] === 'item' && key.length === 2;
/** 專案詳情（['project', 代號]，含成員）；不連專案底下的清單一起重抓。 */
const isProjectDetail = (key: readonly unknown[]) => key[0] === 'project' && key.length === 2;

/** 套用成功或失敗之後依種類讓寫入影響到的快取失效（失敗時伺服器可能已經做了一部分）。 */
export function invalidateForActionKind(queryClient: QueryClient, kind: ActionKind): void {
  switch (kind) {
    case 'knowledge':
      invalidateWhere(queryClient, (key) => key[0] === 'knowledge');
      return;
    case 'people':
      void queryClient.invalidateQueries({ queryKey: queryKeys.people });
      // 改到自己的角色或停用時，目前的登入資料跟著變。
      void queryClient.invalidateQueries({ queryKey: queryKeys.session });
      void queryClient.invalidateQueries({ queryKey: queryKeys.auditEvents });
      invalidateWhere(queryClient, isProjectDetail);
      return;
    case 'attachments':
      invalidateWhere(queryClient, isItemAttachmentOrActivity);
      invalidateWhere(queryClient, isItemDetail);
      invalidateWhere(queryClient, (key) => key[0] === 'knowledge');
      return;
    case 'saved_views':
      invalidateWhere(queryClient, (key) => key[0] === 'project' && key[2] === 'saved-views');
      void queryClient.invalidateQueries({ queryKey: queryKeys.myWorkViews });
      return;
    case 'description_templates':
      void queryClient.invalidateQueries({ queryKey: queryKeys.descriptionTemplates });
      return;
  }
}

/**
 * 套用與不要。卡片輸入（初始密碼）不當成 mutation 的變數：變數會留在 mutation 快取裡，
 * 所以用 getInputs 在送出的那一刻才讀；onApplySettled 在套用成功或失敗之後呼叫（卡片拿來清掉輸入）。
 */
export function useDecideActionProposal(proposal: ActionProposal, getInputs: () => ActionInputValues, onApplySettled?: () => void) {
  return useProposalDecision<ActionProposal>({
    proposal,
    queryKey: queryKeys.actionProposal(proposal.id),
    apply: (proposalId) => applyActionProposal(proposalId, getInputs()),
    dismiss: dismissActionProposal,
    refresh: (queryClient) => {
      invalidateForActionKind(queryClient, proposal.kind);
      onApplySettled?.();
    },
  });
}
