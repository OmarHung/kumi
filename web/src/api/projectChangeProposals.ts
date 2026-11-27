import { useQuery, type QueryClient } from '@tanstack/react-query';
import type { ProjectChangeProposal } from './aiTypes';
import { apiRequest } from './http';
import { pollWhileApplying, useProposalDecision } from './proposalDecision';
import { queryKeys } from './queryKeys';

const proposalPath = (proposalId: string) => `/v1/ai/project-change-proposals/${encodeURIComponent(proposalId)}`;

/** 404 ai.project_change_proposal_not_found：別人的、不存在的。 */
export function getProjectChangeProposal(proposalId: string, signal?: AbortSignal): Promise<ProjectChangeProposal> {
  return apiRequest<ProjectChangeProposal>(proposalPath(proposalId), { signal });
}

/** 409 ai.project_change_proposal_closed；第一步就失敗時工作引擎的失敗原樣回來（建議維持待處理）。 */
export function applyProjectChangeProposal(proposalId: string): Promise<ProjectChangeProposal> {
  return apiRequest<ProjectChangeProposal>(`${proposalPath(proposalId)}/apply`, { method: 'POST', body: {} });
}

/** 409 ai.project_change_proposal_closed。 */
export function dismissProjectChangeProposal(proposalId: string): Promise<ProjectChangeProposal> {
  return apiRequest<ProjectChangeProposal>(`${proposalPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

export function useProjectChangeProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.projectChangeProposal(proposalId),
    queryFn: ({ signal }) => getProjectChangeProposal(proposalId, signal),
    refetchInterval: pollWhileApplying,
  });
}

/** 專案本身（名稱、封存）、專案清單與側欄、專案說明（資訊頁）與總覽；封存會改變看得到的東西，我的工作也重抓。 */
function refresh(queryClient: QueryClient, proposal: ProjectChangeProposal): void {
  void queryClient.invalidateQueries({ queryKey: queryKeys.project(proposal.projectKey) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.projects });
  void queryClient.invalidateQueries({ queryKey: queryKeys.overviews });
  void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
}

/** 套用之後換成伺服器回的新狀態並讓寫入影響到的快取失效（失敗時伺服器可能已經改了一部分，照樣失效）。 */
export function useDecideProjectChangeProposal(proposal: ProjectChangeProposal) {
  return useProposalDecision<ProjectChangeProposal>({
    proposal,
    queryKey: queryKeys.projectChangeProposal(proposal.id),
    apply: applyProjectChangeProposal,
    dismiss: dismissProjectChangeProposal,
    refresh: (queryClient) => {
      refresh(queryClient, proposal);
    },
  });
}
