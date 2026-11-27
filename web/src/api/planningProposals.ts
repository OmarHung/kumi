import { useQuery, type QueryClient } from '@tanstack/react-query';
import type { PlanningProposal } from './aiTypes';
import { apiRequest } from './http';
import { invalidateItemsOfProject, pollWhileApplying, useProposalDecision } from './proposalDecision';
import { refreshAfterItemsChanged } from '../features/item-table/revisionCache';
import { queryKeys } from './queryKeys';

const proposalPath = (proposalId: string) => `/v1/ai/planning-proposals/${encodeURIComponent(proposalId)}`;

/** 404 ai.planning_proposal_not_found：別人的、不存在的。 */
export function getPlanningProposal(proposalId: string, signal?: AbortSignal): Promise<PlanningProposal> {
  return apiRequest<PlanningProposal>(proposalPath(proposalId), { signal });
}

/** 409 ai.planning_proposal_closed；第一步就失敗時工作引擎的失敗原樣回來（建議維持待處理）。 */
export function applyPlanningProposal(proposalId: string): Promise<PlanningProposal> {
  return apiRequest<PlanningProposal>(`${proposalPath(proposalId)}/apply`, { method: 'POST', body: {} });
}

/** 409 ai.planning_proposal_closed。 */
export function dismissPlanningProposal(proposalId: string): Promise<PlanningProposal> {
  return apiRequest<PlanningProposal>(`${proposalPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

export function usePlanningProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.planningProposal(proposalId),
    queryFn: ({ signal }) => getPlanningProposal(proposalId, signal),
    refetchInterval: pollWhileApplying,
  });
}

/**
 * 週期與里程碑本身、專案的項目清單（項目的週期與里程碑欄位）、每個項目的內容、我的工作與總覽。
 * 刪除週期或里程碑會把裡面的項目變成沒有，所以項目也要重抓。
 */
function refresh(queryClient: QueryClient, proposal: PlanningProposal): void {
  refreshAfterItemsChanged(queryClient, proposal.projectKey, []);
  invalidateItemsOfProject(queryClient, proposal.projectKey);
}

/** 套用之後換成伺服器回的新狀態並讓寫入影響到的快取失效（失敗時伺服器可能已經改了一部分，照樣失效）。 */
export function useDecidePlanningProposal(proposal: PlanningProposal) {
  return useProposalDecision<PlanningProposal>({
    proposal,
    queryKey: queryKeys.planningProposal(proposal.id),
    apply: applyPlanningProposal,
    dismiss: dismissPlanningProposal,
    refresh: (queryClient) => {
      refresh(queryClient, proposal);
    },
  });
}
