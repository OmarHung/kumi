import type { PlanningChange, PlanningProposal } from '../../api/aiTypes';
import { useDecidePlanningProposal, usePlanningProposal } from '../../api/planningProposals';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { planningHeading, planningLine } from './planningRows';
import { ClosedByOtherNotice, ProposalActions } from './ProposalActions';
import { ProposalShell } from './ProposalShell';
import styles from './ProposalCard.module.css';

function ChangeList({ changes }: { readonly changes: readonly PlanningChange[] }) {
  return (
    <ol className={styles.drafts}>
      {changes.map((change, index) => {
        const line = planningLine(change);
        return (
          // 同一份建議裡可以有好幾筆新增，沒有別的穩定的鍵；清單不會重排。
          <li key={`${String(index)}:${change.target}`} className={styles.draft}>
            <p className={styles.draftTitle}>{planningHeading(change)}</p>
            <p className={styles.draftMeta}>
              {line.before !== null && (
                <>
                  <span className={styles.before}>{line.before}</span>
                  <span className="visually-hidden"> 改成 </span>
                  <span aria-hidden="true" className={styles.arrow}>
                    {' → '}
                  </span>
                </>
              )}
              <span className={styles.after}>{line.after}</span>
            </p>
            {line.note !== null && <p className={styles.draftMeta}>{line.note}</p>}
            {change.error !== null && <Notice tone="warning">{change.error}</Notice>}
          </li>
        );
      })}
    </ol>
  );
}

function PendingBody({ proposal }: { readonly proposal: PlanningProposal }) {
  const { apply, dismiss } = useDecidePlanningProposal(proposal);
  return (
    <>
      {proposal.reason !== null && <p className={styles.draftMeta}>{proposal.reason}</p>}
      <ChangeList changes={proposal.changes} />
      <ProposalActions apply={apply} dismiss={dismiss} applyLabel="套用" busyLabel="套用中…" closedCode="ai.planning_proposal_closed" />
    </>
  );
}

function ClosedBody({ proposal }: { readonly proposal: PlanningProposal }) {
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  if (proposal.status === 'dismissed') return <p className={styles.status}>{`已略過${when}`}</p>;
  return (
    <>
      <p className={styles.summary}>{`已套用${when}`}</p>
      <ChangeList changes={proposal.changes} />
      {proposal.error !== null && <Notice tone="warning">{proposal.error}</Notice>}
    </>
  );
}

function LoadedCard({ proposal }: { readonly proposal: PlanningProposal }) {
  return (
    <section className={styles.card} aria-label={`週期與里程碑建議 ${proposal.projectKey}`}>
      <p className={styles.title}>{`建議調整週期與里程碑（${proposal.projectKey}）`}</p>
      {proposal.status === 'pending' && <PendingBody proposal={proposal} />}
      <div role="status">
        {proposal.status !== 'pending' && <ClosedByOtherNotice proposalId={proposal.id} />}
        {proposal.status === 'applying' && <p className={styles.status}>套用中…</p>}
        {(proposal.status === 'applied' || proposal.status === 'dismissed') && <ClosedBody proposal={proposal} />}
      </div>
    </section>
  );
}

/**
 * 助理的 propose_planning_changes：列出週期與里程碑的新增、修改、刪除，按「套用」才依序以自己的身分寫入
 * （internal/HANDOFF-ai-coverage.md 第 5.3 節）。新增不能重做，所以另一個分頁按了之後是「套用中」。
 */
export function PlanningProposalCard({ proposalId }: { readonly proposalId: string }) {
  const query = usePlanningProposal(proposalId);
  return (
    <ProposalShell query={query} noun="週期與里程碑建議">
      {(proposal) => <LoadedCard proposal={proposal} />}
    </ProposalShell>
  );
}
