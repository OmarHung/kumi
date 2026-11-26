import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { MemberProposal } from './aiTypes';
import { apiRequest } from './http';
import { queryKeys } from './queryKeys';

const memberProposalPath = (proposalId: string) => `/v1/ai/member-proposals/${encodeURIComponent(proposalId)}`;

/** 404 ai.member_proposal_not_found：別人的、不存在的。 */
export function getMemberProposal(proposalId: string, signal?: AbortSignal): Promise<MemberProposal> {
  return apiRequest<MemberProposal>(memberProposalPath(proposalId), { signal });
}

/** 409 ai.member_proposal_closed；第一筆就失敗時工作引擎的失敗原樣回來（建議維持待處理）。 */
export function applyMemberProposal(proposalId: string): Promise<MemberProposal> {
  return apiRequest<MemberProposal>(`${memberProposalPath(proposalId)}/apply`, { method: 'POST', body: {} });
}

/** 409 ai.member_proposal_closed。 */
export function dismissMemberProposal(proposalId: string): Promise<MemberProposal> {
  return apiRequest<MemberProposal>(`${memberProposalPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

export function useMemberProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.memberProposal(proposalId),
    queryFn: ({ signal }) => getMemberProposal(proposalId, signal),
  });
}

/**
 * 套用之後：換成伺服器回的新狀態，並讓這個專案的詳細資料（成員在裡面）、人員清單與專案清單（成員數）失效。
 * 套用失敗（5xx、逾時、網路）時伺服器可能已經改了一部分，也失效同一組。
 */
export function useDecideMemberProposal(proposal: MemberProposal) {
  const queryClient = useQueryClient();
  const settle = (decided: MemberProposal) => {
    queryClient.setQueryData(queryKeys.memberProposal(proposal.id), decided);
  };
  const invalidateMembers = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.project(proposal.projectKey), exact: true });
    void queryClient.invalidateQueries({ queryKey: queryKeys.people });
    void queryClient.invalidateQueries({ queryKey: queryKeys.projects });
    // 自己被加入或移出時，「我的工作」的範圍跟著變。
    void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
  };
  const apply = useMutation({
    mutationFn: () => applyMemberProposal(proposal.id),
    onSuccess: (decided) => {
      settle(decided);
      invalidateMembers();
    },
    onError: () => {
      // 已經處理過（別的分頁按了）：以伺服器現在的狀態為準；第一筆就失敗的維持待處理，重讀也不會變。
      void queryClient.invalidateQueries({ queryKey: queryKeys.memberProposal(proposal.id) });
      invalidateMembers();
    },
  });
  const dismiss = useMutation({
    mutationFn: () => dismissMemberProposal(proposal.id),
    onSuccess: settle,
    // 409（別的分頁已處理）：重讀，卡片才會從待處理變成實際的狀態。
    onError: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.memberProposal(proposal.id) });
    },
  });
  return { apply, dismiss };
}
