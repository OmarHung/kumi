import { useState } from 'react';
import { Link } from 'react-router';
import type { ItemProposal } from '../../api/aiTypes';
import { useDecideItemProposal, useItemProposal } from '../../api/assistant';
import { errorMessage, isApiError } from '../../api/http';
import { useItem, useProject } from '../../api/queries';
import { Button } from '../../components/Button/Button';
import { Markdown } from '../../components/Markdown/Markdown';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { itemPagePath } from '../work-items/itemLinks';
import { changedLabels, changeRows, type ChangeRow } from './proposalChanges';
import styles from './ProposalCard.module.css';

const NO_MENTIONS: ReadonlyMap<string, string> = new Map();
const CONFLICT_CODE = 'item.description_conflict';
const CLOSED_CODE = 'ai.item_proposal_closed';
const CONFLICT_TEXT = '這個項目在建議之後被改過，請重新請助理建議。';
const CLOSED_TEXT = '這份建議已經處理過了。';

function failureText(error: unknown): string {
  if (isApiError(error) && error.code === CONFLICT_CODE) return CONFLICT_TEXT;
  if (isApiError(error) && error.code === CLOSED_CODE) return CLOSED_TEXT;
  return errorMessage(error);
}

function DescriptionRow({ row }: { readonly row: Extract<ChangeRow, { kind: 'description' }> }) {
  const [isOpen, setIsOpen] = useState(false);
  return (
    <div className={styles.row}>
      <dt className={styles.label}>{row.label}</dt>
      <dd className={styles.value}>
        <span>改成新的內容</span>{' '}
        {row.text !== null && (
          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              setIsOpen((open) => !open);
            }}
          >
            {isOpen ? '收起' : '展開'}
          </Button>
        )}
        {isOpen && row.text !== null && (
          <div className={styles.fullText}>
            <Markdown text={row.text} mentionNames={NO_MENTIONS} hasAttachments={false} />
          </div>
        )}
      </dd>
    </div>
  );
}

function ValueRow({ row }: { readonly row: Extract<ChangeRow, { kind: 'value' }> }) {
  return (
    <div className={styles.row}>
      <dt className={styles.label}>{row.label}</dt>
      <dd className={styles.value}>
        {row.before !== null && (
          <>
            <span className={styles.before}>{row.before}</span>
            <span className="visually-hidden"> 改成 </span>
            <span aria-hidden="true" className={styles.arrow}>
              →
            </span>
          </>
        )}
        <span className={styles.after}>{row.after}</span>
      </dd>
    </div>
  );
}

function CommentRow({ text }: { readonly text: string }) {
  return (
    <div className={styles.row}>
      <dt className={styles.label}>留言</dt>
      <dd className={styles.value}>
        <blockquote className={styles.quote}>
          <Markdown text={text} mentionNames={NO_MENTIONS} hasAttachments={false} />
        </blockquote>
      </dd>
    </div>
  );
}

function Header({ proposal }: { readonly proposal: ItemProposal }) {
  return (
    <p className={styles.title}>
      建議修改 <Link to={itemPagePath(proposal.projectKey, proposal.itemKey)}>{proposal.itemKey}</Link> {proposal.itemTitle}
    </p>
  );
}

interface PendingProps {
  readonly proposal: ItemProposal;
  readonly rows: readonly ChangeRow[];
}

function PendingBody({ proposal, rows }: PendingProps) {
  const { apply, dismiss } = useDecideItemProposal(proposal);
  const isWorking = apply.isPending || dismiss.isPending;
  const failure: unknown = apply.error ?? dismiss.error;
  return (
    <>
      <dl className={styles.rows}>
        {rows.map((row) => (row.kind === 'description' ? <DescriptionRow key={row.field} row={row} /> : <ValueRow key={row.field} row={row} />))}
        {proposal.comment !== null && <CommentRow text={proposal.comment} />}
      </dl>
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

function ClosedBody({ proposal, summary }: { readonly proposal: ItemProposal; readonly summary: string }) {
  const isApplied = proposal.status === 'applied';
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  return (
    <>
      <p className={styles.status}>
        {isApplied ? '已套用' : '已略過'}
        {when}
      </p>
      {isApplied && <p className={styles.summary}>{summary}</p>}
      {isApplied && proposal.error !== null && <Notice tone="warning">{proposal.error}</Notice>}
    </>
  );
}

function LoadedCard({ proposal }: { readonly proposal: ItemProposal }) {
  const project = useProject(proposal.projectKey);
  const item = useItem(proposal.itemKey, proposal.status === 'pending');
  const rows = changeRows(proposal.changes, project.data, item.data);
  const summary = changedLabels(proposal.changes, project.data, proposal.comment !== null);
  return (
    <section className={styles.card} aria-label={`修改建議 ${proposal.itemKey}`}>
      <Header proposal={proposal} />
      {proposal.status === 'pending' ? <PendingBody proposal={proposal} rows={rows} /> : <ClosedBody proposal={proposal} summary={summary} />}
    </section>
  );
}

/** 助理的 propose_item_changes：讀建議與項目現在的資料，列出「現在 → 建議」，按「套用」才寫入（internal/HANDOFF-ai-items.md 第 5 節）。 */
export function ProposalCard({ proposalId }: { readonly proposalId: string }) {
  const proposal = useItemProposal(proposalId);
  if (proposal.isPending) return <p className={styles.loading}>正在載入修改建議…</p>;
  if (proposal.isError) return <Notice tone="warning">{`讀不到這份修改建議：${errorMessage(proposal.error)}`}</Notice>;
  return <LoadedCard proposal={proposal.data} />;
}
