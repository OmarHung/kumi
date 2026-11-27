import { Link } from 'react-router';
import type { ItemDeletionProposal } from '../../api/aiTypes';
import { useDecideItemDeletionProposal, useItemDeletionProposal } from '../../api/itemRevisionProposals';
import { errorMessage, isApiError } from '../../api/http';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { itemPagePath, projectPagePath } from '../work-items/itemLinks';
import styles from './ProposalCard.module.css';

const CLOSED_CODE = 'ai.item_deletion_proposal_closed';
const CLOSED_TEXT = '這份建議已經處理過了。';

function failureText(error: unknown): string | null {
  if (error === null || error === undefined) return null;
  if (isApiError(error) && error.code === CLOSED_CODE) return CLOSED_TEXT;
  return errorMessage(error);
}

function deleteLabel(count: number): string {
  return count === 1 ? '刪除' : `刪除 ${String(count)} 個`;
}

/** 看得見的字是「刪除」「刪除 3 個」，名稱再說清楚刪什麼（包含看得見的字）。 */
function deleteAccessibleName(keys: readonly string[]): string {
  return keys.length === 1 ? `刪除 ${keys[0] ?? ''}` : `刪除 ${String(keys.length)} 個項目：${keys.join('、')}`;
}

function PendingBody({ proposal }: { readonly proposal: ItemDeletionProposal }) {
  const { apply, dismiss } = useDecideItemDeletionProposal(proposal);
  const isWorking = apply.isPending || dismiss.isPending;
  return (
    <>
      {proposal.reason !== null && <p className={styles.draftMeta}>{proposal.reason}</p>}
      <ol className={styles.drafts}>
        {proposal.items.map((item) => (
          <li key={item.itemKey} className={styles.draft}>
            <p className={styles.draftTitle}>
              <Link to={itemPagePath(proposal.projectKey, item.itemKey)} className={styles.code}>
                {item.itemKey}
              </Link>{' '}
              {item.title}
            </p>
          </li>
        ))}
      </ol>
      <div className={styles.actions}>
        <Button
          variant="danger"
          size="sm"
          aria-label={apply.isPending ? undefined : deleteAccessibleName(proposal.items.map((item) => item.itemKey))}
          aria-disabled={isWorking || undefined}
          onClick={() => {
            if (isWorking) return;
            dismiss.reset();
            apply.mutate();
          }}
        >
          {apply.isPending ? '刪除中…' : deleteLabel(proposal.items.length)}
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
      <Notice tone="danger">{failureText(apply.error ?? dismiss.error)}</Notice>
    </>
  );
}

function DeletedSummary({ proposal }: { readonly proposal: ItemDeletionProposal }) {
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  if (proposal.deletedKeys.length === 0) return <p className={styles.summary}>{`沒有刪除任何項目${when}`}</p>;
  return (
    <p className={styles.summary}>
      已刪除{' '}
      {proposal.deletedKeys.map((key, index) => (
        <span key={key}>
          {index > 0 && '、'}
          {key}
        </span>
      ))}
      ，可以在<Link to={`${projectPagePath(proposal.projectKey)}/deleted`}>「已刪除」</Link>復原{when}
    </p>
  );
}

function ClosedBody({ proposal }: { readonly proposal: ItemDeletionProposal }) {
  if (proposal.status === 'applied') {
    return (
      <>
        <DeletedSummary proposal={proposal} />
        {proposal.error !== null && <Notice tone="warning">{proposal.error}</Notice>}
      </>
    );
  }
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  return <p className={styles.status}>{`已略過${when}`}</p>;
}

/**
 * 助理的 propose_item_deletions：列出要刪的項目，按「刪除」才以自己的身分批次刪除（軟刪除，能在「已刪除」復原；
 * internal/HANDOFF-ai-revise.md 第 7 節）。
 */
export function ItemDeletionProposalCard({ proposalId }: { readonly proposalId: string }) {
  const query = useItemDeletionProposal(proposalId);
  // 背景重讀失敗時 data 還在、status 卻是 error：有資料就照舊顯示，只有完全沒資料才換成讀不到。
  const proposal: ItemDeletionProposal | undefined = query.data;
  if (proposal === undefined) {
    if (query.isError) return <Notice tone="warning">{`讀不到這份刪除建議：${errorMessage(query.error)}`}</Notice>;
    return <p className={styles.loading}>正在載入刪除建議…</p>;
  }
  return (
    <section className={styles.card} aria-label={`刪除建議 ${proposal.projectKey}`}>
      <p className={styles.title}>{`建議刪除 ${String(proposal.items.length)} 個項目（${proposal.projectKey}）`}</p>
      {proposal.status === 'pending' && <PendingBody proposal={proposal} />}
      <div role="status">{proposal.status !== 'pending' && <ClosedBody proposal={proposal} />}</div>
    </section>
  );
}
