import { useQuery, type QueryClient } from '@tanstack/react-query';
import type { CommentProposal } from './aiTypes';
import { apiRequest } from './http';
import { pollWhileApplying, useProposalDecision } from './proposalDecision';
import { queryKeys } from './queryKeys';

const proposalPath = (proposalId: string) => `/v1/ai/comment-proposals/${encodeURIComponent(proposalId)}`;

/** 404 ai.comment_proposal_not_found：別人的、不存在的。 */
export function getCommentProposal(proposalId: string, signal?: AbortSignal): Promise<CommentProposal> {
  return apiRequest<CommentProposal>(proposalPath(proposalId), { signal });
}

/** 409 ai.comment_proposal_closed；第一步就失敗時工作引擎的失敗原樣回來（建議維持待處理）。 */
export function applyCommentProposal(proposalId: string): Promise<CommentProposal> {
  return apiRequest<CommentProposal>(`${proposalPath(proposalId)}/apply`, { method: 'POST', body: {} });
}

/** 409 ai.comment_proposal_closed。 */
export function dismissCommentProposal(proposalId: string): Promise<CommentProposal> {
  return apiRequest<CommentProposal>(`${proposalPath(proposalId)}/dismiss`, { method: 'POST', body: {} });
}

export function useCommentProposal(proposalId: string) {
  return useQuery({
    queryKey: queryKeys.commentProposal(proposalId),
    queryFn: ({ signal }) => getCommentProposal(proposalId, signal),
    refetchInterval: pollWhileApplying,
  });
}

/** 每個項目的活動（時間軸的留言）；留言不動項目本身的欄位，其他快取不用重抓。 */
function refresh(queryClient: QueryClient, proposal: CommentProposal): void {
  new Set(proposal.changes.map((change) => change.itemKey)).forEach((itemKey) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.activity(itemKey) });
  });
}

/** 套用之後換成伺服器回的新狀態並讓寫入影響到的快取失效（失敗時伺服器可能已經改了一部分，照樣失效）。 */
export function useDecideCommentProposal(proposal: CommentProposal) {
  return useProposalDecision<CommentProposal>({
    proposal,
    queryKey: queryKeys.commentProposal(proposal.id),
    apply: applyCommentProposal,
    dismiss: dismissCommentProposal,
    refresh: (queryClient) => {
      refresh(queryClient, proposal);
    },
  });
}
