import { Link } from 'react-router';
import type { CommentChange, CommentProposal } from '../../api/aiTypes';
import { useCommentProposal, useDecideCommentProposal } from '../../api/commentProposals';
import { Icon } from '../../components/Icon/Icon';
import { Markdown } from '../../components/Markdown/Markdown';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { itemPagePath } from '../work-items/itemLinks';
import { ClosedByOtherNotice, ProposalActions } from './ProposalActions';
import { ProposalShell } from './ProposalShell';
import styles from './ProposalCard.module.css';

const NO_MENTIONS: ReadonlyMap<string, string> = new Map();
const ACTION_LABELS = { edit: '編輯留言', delete: '刪除留言', restore: '復原留言' } as const;

function ChangeList({ projectKey, changes, showResult = false }: { readonly projectKey: string; readonly changes: readonly CommentChange[]; readonly showResult?: boolean }) {
  return (
    <ol className={styles.drafts}>
      {changes.map((change) => (
        <li key={`${change.itemKey}:${change.commentId}`} className={styles.draft}>
          <p className={styles.draftTitle}>
            {`${ACTION_LABELS[change.action]} · `}
            <Link to={itemPagePath(projectKey, change.itemKey)} className={styles.code}>
              {change.itemKey}
            </Link>
          </p>
          <p className={styles.draftMeta}>{`${change.authorName} · ${formatInstant(change.commentedAt)}`}</p>
          {change.excerpt === null ? (
            <p className={styles.draftMeta}>（已刪除，看不到內容）</p>
          ) : (
            <blockquote className={styles.quote}>{change.excerpt}</blockquote>
          )}
          {change.action === 'edit' && change.body !== null && (
            <>
              <p className={styles.draftMeta}>改成</p>
              <blockquote className={styles.quote}>
                <Markdown text={change.body} mentionNames={NO_MENTIONS} hasAttachments={false} linkHosts="always" />
              </blockquote>
            </>
          )}
          {change.error !== null && <Notice tone="warning">{change.error}</Notice>}
          {showResult && change.error === null && (
            <p className={styles.entryResult}>
              <Icon name="check" />
              <span>已套用</span>
            </p>
          )}
        </li>
      ))}
    </ol>
  );
}

function PendingBody({ proposal }: { readonly proposal: CommentProposal }) {
  const { apply, dismiss } = useDecideCommentProposal(proposal);
  return (
    <>
      <ChangeList projectKey={proposal.projectKey} changes={proposal.changes} />
      <ProposalActions apply={apply} dismiss={dismiss} applyLabel="套用" busyLabel="套用中…" closedCode="ai.comment_proposal_closed" />
    </>
  );
}

function ClosedBody({ proposal }: { readonly proposal: CommentProposal }) {
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  if (proposal.status === 'dismissed') return <p className={styles.status}>{`已略過${when}`}</p>;
  return (
    <>
      <p className={styles.summary}>{`已套用${when}`}</p>
      <ChangeList projectKey={proposal.projectKey} changes={proposal.changes} showResult />
      {proposal.error !== null && <Notice tone="warning">{proposal.error}</Notice>}
    </>
  );
}

function LoadedCard({ proposal }: { readonly proposal: CommentProposal }) {
  return (
    <section className={styles.card} aria-label={`留言建議 ${proposal.projectKey}`}>
      <p className={styles.title}>{`建議處理 ${String(proposal.changes.length)} 則留言（${proposal.projectKey}）`}</p>
      {proposal.status === 'pending' && <PendingBody proposal={proposal} />}
      <div role="status">
        {proposal.status !== 'pending' && <ClosedByOtherNotice proposalId={proposal.id} />}
        {proposal.status === 'applying' && <p className={styles.status}>套用中…</p>}
        {(proposal.status === 'applied' || proposal.status === 'dismissed') && <ClosedBody proposal={proposal} />}
      </div>
    </section>
  );
}

/** 助理的 propose_comment_changes：編輯、刪除、復原留言，按「套用」才以自己的身分寫入（internal/HANDOFF-ai-coverage.md 第 5.5 節）。 */
export function CommentProposalCard({ proposalId }: { readonly proposalId: string }) {
  const query = useCommentProposal(proposalId);
  return (
    <ProposalShell query={query} noun="留言建議">
      {(proposal) => <LoadedCard proposal={proposal} />}
    </ProposalShell>
  );
}
