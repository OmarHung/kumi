import { Link } from 'react-router';
import type { ItemRestoreProposal } from '../../api/aiTypes';
import { useDecideItemRestoreProposal, useItemRestoreProposal } from '../../api/itemRestoreProposals';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { itemPagePath } from '../work-items/itemLinks';
import { ClosedByOtherNotice, ProposalActions } from './ProposalActions';
import { ProposalShell } from './ProposalShell';
import styles from './ProposalCard.module.css';

function PendingBody({ proposal }: { readonly proposal: ItemRestoreProposal }) {
  const { apply, dismiss } = useDecideItemRestoreProposal(proposal);
  return (
    <>
      {proposal.reason !== null && <p className={styles.draftMeta}>{proposal.reason}</p>}
      <ol className={styles.drafts}>
        {proposal.items.map((item) => (
          <li key={item.itemKey} className={styles.draft}>
            <p className={styles.draftTitle}>
              <span className={styles.code}>{item.itemKey}</span> {item.title}
            </p>
            <p className={styles.draftMeta}>{`刪除於 ${formatInstant(item.deletedAt)}`}</p>
          </li>
        ))}
      </ol>
      <ProposalActions apply={apply} dismiss={dismiss} applyLabel="復原" busyLabel="復原中…" closedCode="ai.item_restore_proposal_closed" />
    </>
  );
}

function ClosedBody({ proposal }: { readonly proposal: ItemRestoreProposal }) {
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  if (proposal.status === 'dismissed') return <p className={styles.status}>{`已略過${when}`}</p>;
  return (
    <>
      {proposal.restoredKeys.length === 0 ? (
        <p className={styles.summary}>{`沒有復原任何項目${when}`}</p>
      ) : (
        <p className={styles.summary}>
          已復原{' '}
          {proposal.restoredKeys.map((key, index) => (
            <span key={key}>
              {index > 0 && '、'}
              <Link to={itemPagePath(proposal.projectKey, key)}>{key}</Link>
            </span>
          ))}
          {when}
        </p>
      )}
      {proposal.error !== null && <Notice tone="warning">{proposal.error}</Notice>}
    </>
  );
}

function LoadedCard({ proposal }: { readonly proposal: ItemRestoreProposal }) {
  return (
    <section className={styles.card} aria-label={`復原建議 ${proposal.projectKey}`}>
      <p className={styles.title}>{`建議復原 ${String(proposal.items.length)} 個已刪除的項目（${proposal.projectKey}）`}</p>
      {proposal.status === 'pending' && <PendingBody proposal={proposal} />}
      <div role="status">
        {proposal.status !== 'pending' && <ClosedByOtherNotice proposalId={proposal.id} />}
        {proposal.status === 'applying' && <p className={styles.status}>復原中…</p>}
        {(proposal.status === 'applied' || proposal.status === 'dismissed') && <ClosedBody proposal={proposal} />}
      </div>
    </section>
  );
}

/** 助理的 propose_item_restores：列出要從「已刪除」復原的項目，按「復原」才以自己的身分復原（internal/HANDOFF-ai-coverage.md 第 5.6 節）。 */
export function ItemRestoreProposalCard({ proposalId }: { readonly proposalId: string }) {
  const query = useItemRestoreProposal(proposalId);
  return (
    <ProposalShell query={query} noun="復原建議">
      {(proposal) => <LoadedCard proposal={proposal} />}
    </ProposalShell>
  );
}
