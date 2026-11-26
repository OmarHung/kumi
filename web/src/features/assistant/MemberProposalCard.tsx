import type { MemberChange, MemberProposal } from '../../api/aiTypes';
import { useDecideMemberProposal, useMemberProposal } from '../../api/memberProposals';
import { errorMessage, isApiError } from '../../api/http';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import styles from './ProposalCard.module.css';

const CLOSED_CODE = 'ai.member_proposal_closed';
const CLOSED_TEXT = '這份建議已經處理過了。';

function failureText(error: unknown): string {
  if (isApiError(error) && error.code === CLOSED_CODE) return CLOSED_TEXT;
  return errorMessage(error);
}

/** 一筆變動的說法：加入為 X、X → Y、移出專案；角色名稱是建立建議時的，沒有就用角色的 key。 */
export function changeText(change: MemberChange): string {
  if (change.role === null) return '移出專案';
  const next = change.roleName ?? change.role;
  if (change.previousRole === null) return `加入為 ${next}`;
  return `${change.previousRoleName ?? change.previousRole} → ${next}`;
}

function ChangeList({ changes }: { readonly changes: readonly MemberChange[] }) {
  return (
    <ol className={styles.drafts}>
      {changes.map((change) => (
        <li key={change.personId} className={styles.draft}>
          <p className={styles.draftTitle}>{change.displayName}</p>
          <p className={styles.draftMeta}>{changeText(change)}</p>
        </li>
      ))}
    </ol>
  );
}

function PendingBody({ proposal }: { readonly proposal: MemberProposal }) {
  const { apply, dismiss } = useDecideMemberProposal(proposal);
  const isWorking = apply.isPending || dismiss.isPending;
  const failure: unknown = apply.error ?? dismiss.error;
  return (
    <>
      <ChangeList changes={proposal.changes} />
      <div className={styles.actions}>
        <Button
          variant="primary"
          size="sm"
          aria-disabled={isWorking || undefined}
          onClick={() => {
            if (isWorking) return;
            dismiss.reset();
            apply.mutate();
          }}
        >
          {apply.isPending ? '套用中…' : '套用'}
        </Button>
        <Button
          size="sm"
          aria-disabled={isWorking || undefined}
          onClick={() => {
            if (isWorking) return;
            apply.reset();
            dismiss.mutate();
          }}
        >
          不要
        </Button>
      </div>
      <Notice tone="danger">{failure === null || failure === undefined ? null : failureText(failure)}</Notice>
    </>
  );
}

function ClosedBody({ proposal }: { readonly proposal: MemberProposal }) {
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  if (proposal.status === 'applied') {
    return (
      <>
        <p className={styles.summary}>{`已套用${when}`}</p>
        {proposal.error !== null && <Notice tone="warning">{proposal.error}</Notice>}
      </>
    );
  }
  return <p className={styles.status}>{`已略過${when}`}</p>;
}

/**
 * 助理的 propose_member_changes：列出要加入、改角色、移出的成員，按「套用」才以自己的身分寫入
 * （internal/HANDOFF-ai-assign.md 第 9 節）。
 */
export function MemberProposalCard({ proposalId }: { readonly proposalId: string }) {
  const query = useMemberProposal(proposalId);
  // 背景重讀失敗時 data 還在、status 卻是 error：有資料就照舊顯示，只有完全沒資料才換成讀不到。
  const proposal: MemberProposal | undefined = query.data;
  if (proposal === undefined) {
    if (query.isError) return <Notice tone="warning">{`讀不到這份成員建議：${errorMessage(query.error)}`}</Notice>;
    return <p className={styles.loading}>正在載入成員建議…</p>;
  }
  return (
    <section className={styles.card} aria-label={`成員建議 ${proposal.projectKey}`}>
      <p className={styles.title}>{`建議成員變動（${proposal.projectKey}）`}</p>
      {proposal.status === 'pending' && <PendingBody proposal={proposal} />}
      <div role="status">{proposal.status !== 'pending' && <ClosedBody proposal={proposal} />}</div>
    </section>
  );
}
