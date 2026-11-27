import { useQuery, type QueryClient } from '@tanstack/react-query';
import type { ItemRestoreProposal } from './aiTypes';
import { apiRequest } from './http';
import { invalidateItemsOfProject, pollWhileApplying, useProposalDecision } from './proposalDecision';
import { refreshAfterItemsChanged } from '../features/item-table/revisionCache';
import { queryKeys } from './queryKeys';

const proposalPath = (proposalId: string) => `/v1/ai/item-restore-proposals/${encodeURIComponent(proposalId)}`;

/** 404 ai.item_restore_proposal_not_found：別人的、不存在的。 */
export function getItemRestoreProposal(proposalId: string, signal?: AbortSignal): Promise<ItemRestoreProposal> {
  return apiRequest<ItemRestoreProposal>(proposalPath(proposalId), { signal });
}

/** 409 ai.item_restore_proposal_closed；第一步就失敗時工作引擎的失敗原樣回來（建議維持待處理）。 */
export function applyItemRestoreProposal(proposalId: string): Promise<ItemRestoreProposal> {
  return apiRequest<ItemRestoreProposal>(`${proposalPath(proposalId)}/apply`, { method: 'POST', body: {} });
}

/** 409 ai.item_restore_proposal_closed。 */
export function dismissItemRestoreProposal(proposalId: string): Promise<ItemRestoreProposal> {
  return apiRequest<ItemRestoreProposal>(`${proposalPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

export function useItemRestoreProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.itemRestoreProposal(proposalId),
    queryFn: ({ signal }) => getItemRestoreProposal(proposalId, signal),
    refetchInterval: pollWhileApplying,
  });
}

/** 復原的項目回到清單、離開「已刪除」；上層項目的下層也變了，所以專案的項目都重抓。 */
function refresh(queryClient: QueryClient, proposal: ItemRestoreProposal): void {
  refreshAfterItemsChanged(queryClient, proposal.projectKey, proposal.items.map((item) => item.itemKey));
  invalidateItemsOfProject(queryClient, proposal.projectKey);
}

/** 套用之後換成伺服器回的新狀態並讓寫入影響到的快取失效（失敗時伺服器可能已經改了一部分，照樣失效）。 */
export function useDecideItemRestoreProposal(proposal: ItemRestoreProposal) {
  return useProposalDecision<ItemRestoreProposal>({
    proposal,
    queryKey: queryKeys.itemRestoreProposal(proposal.id),
    apply: applyItemRestoreProposal,
    dismiss: dismissItemRestoreProposal,
    refresh: (queryClient) => {
      refresh(queryClient, proposal);
    },
  });
}
