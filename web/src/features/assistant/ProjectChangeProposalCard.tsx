import type { ProjectChangeProposal } from '../../api/aiTypes';
import { useDecideProjectChangeProposal, useProjectChangeProposal } from '../../api/projectChangeProposals';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { CLEARED_VALUE } from './proposalChanges';
import { DescriptionBox } from './ProposalCard';
import { ClosedByOtherNotice, ProposalActions } from './ProposalActions';
import { ProposalShell } from './ProposalShell';
import styles from './ProposalCard.module.css';

function ValueRow({ label, before, after }: { readonly label: string; readonly before: string; readonly after: string }) {
  return (
    <div className={styles.row}>
      <dt className={styles.label}>{label}</dt>
      <dd className={styles.value}>
        <span className={styles.before}>{before}</span>
        <span className="visually-hidden"> 改成 </span>
        <span aria-hidden="true" className={styles.arrow}>
          →
        </span>
        <span className={styles.after}>{after}</span>
      </dd>
    </div>
  );
}

function DescriptionRow({ proposal }: { readonly proposal: ProjectChangeProposal }) {
  if (proposal.description === null || proposal.description.trim() === '') {
    return (
      <div className={styles.row}>
        <dt className={styles.label}>專案說明</dt>
        <dd className={styles.value}>
          <span className={styles.after}>{CLEARED_VALUE}</span>
        </dd>
      </div>
    );
  }
  return (
    <div className={styles.row}>
      <dt className={styles.label}>專案說明</dt>
      <dd className={styles.value}>
        <div className={styles.compare}>
          <DescriptionBox label="現在的專案說明" heading="現在的" text={proposal.before.description ?? ''} />
          <DescriptionBox label="建議的專案說明" heading="建議的" text={proposal.description} linkHosts="always" />
        </div>
      </dd>
    </div>
  );
}

const archivedText = (isArchived: boolean) => (isArchived ? '已封存' : '使用中');

function ChangeRows({ proposal }: { readonly proposal: ProjectChangeProposal }) {
  const { before } = proposal;
  if (proposal.name === null && proposal.archived === null && !proposal.changesDescription) return null;
  return (
    <dl className={styles.rows}>
      {proposal.name !== null && <ValueRow label="名稱" before={before.name} after={proposal.name} />}
      {proposal.archived !== null && (
        <ValueRow label="狀態" before={archivedText(before.archived)} after={archivedText(proposal.archived)} />
      )}
      {proposal.changesDescription && <DescriptionRow proposal={proposal} />}
    </dl>
  );
}

function PendingBody({ proposal }: { readonly proposal: ProjectChangeProposal }) {
  const { apply, dismiss } = useDecideProjectChangeProposal(proposal);
  return (
    <>
      {proposal.reason !== null && <p className={styles.draftMeta}>{proposal.reason}</p>}
      <ChangeRows proposal={proposal} />
      <ProposalActions apply={apply} dismiss={dismiss} applyLabel="套用" busyLabel="套用中…" closedCode="ai.project_change_proposal_closed" />
    </>
  );
}

function ClosedBody({ proposal }: { readonly proposal: ProjectChangeProposal }) {
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  if (proposal.status === 'dismissed') return <p className={styles.status}>{`已略過${when}`}</p>;
  return (
    <>
      <p className={styles.summary}>{`已套用${when}`}</p>
      {proposal.error !== null && <Notice tone="warning">{proposal.error}</Notice>}
    </>
  );
}

function LoadedCard({ proposal }: { readonly proposal: ProjectChangeProposal }) {
  return (
    <section className={styles.card} aria-label={`專案變更建議 ${proposal.projectKey}`}>
      <p className={styles.title}>{`建議修改專案（${proposal.projectKey}）`}</p>
      {proposal.status === 'pending' && <PendingBody proposal={proposal} />}
      <div role="status">
        {proposal.status !== 'pending' && <ClosedByOtherNotice proposalId={proposal.id} />}
        {proposal.status === 'applying' && <p className={styles.status}>套用中…</p>}
        {(proposal.status === 'applied' || proposal.status === 'dismissed') && <ClosedBody proposal={proposal} />}
      </div>
    </section>
  );
}

/** 助理的 propose_project_changes：名稱、封存、專案說明（資訊頁），按「套用」才以自己的身分寫入（internal/HANDOFF-ai-coverage.md 第 5.4 節）。 */
export function ProjectChangeProposalCard({ proposalId }: { readonly proposalId: string }) {
  const query = useProjectChangeProposal(proposalId);
  return (
    <ProposalShell query={query} noun="專案變更建議">
      {(proposal) => <LoadedCard proposal={proposal} />}
    </ProposalShell>
  );
}
