import { useId, useRef } from 'react';
import { Link } from 'react-router';
import type { ItemProposal, ReplyTarget } from '../../api/aiTypes';
import { useDecideItemProposal, useItemProposal, useProposalPlanningNames } from '../../api/assistant';
import { errorMessage, isApiError } from '../../api/http';
import { useItem, useProject } from '../../api/queries';
import { Button } from '../../components/Button/Button';
import { Markdown } from '../../components/Markdown/Markdown';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { itemPagePath } from '../work-items/itemLinks';
import { changedLabels, changeRows, EMPTY_VALUE, type ChangeRow } from './proposalChanges';
import { useDescriptionSummary } from './useDescriptionSummary';
import { useOverflow } from './useOverflow';
import styles from './ProposalCard.module.css';

const NO_MENTIONS: ReadonlyMap<string, string> = new Map();
const CONFLICT_CODE = 'item.description_conflict';
const CLOSED_CODE = 'ai.item_proposal_closed';
const CONFLICT_TEXT = '這個項目在建議之後被改過，請重新請助理建議。';
const COMMENT_NOTE = '留言送出後，看得到這個項目的人都看得到。';
const CLOSED_TEXT = '這份建議已經處理過了。';

function failureText(error: unknown): string {
  if (isApiError(error) && error.code === CONFLICT_CODE) return CONFLICT_TEXT;
  if (isApiError(error) && error.code === CLOSED_CODE) return CLOSED_TEXT;
  return errorMessage(error);
}

type DescriptionChange = Extract<ChangeRow, { kind: 'description' }>;

const SCROLL_NOTE = '內容較長，捲動看完整的說明';

/**
 * 說明的一格：內容很長時有高度上限、自己捲動，「套用」不會被推到看不見。只有真的溢出時才能用鍵盤聚焦捲動
 * （沒溢出時不多一個 Tab 停駐點），並在格子下面說一聲還有更多。
 */
interface DescriptionBoxProps {
  readonly label: string;
  /** 框裡的小標題（改前、改後）；外面已經有欄位名稱時不給，免得同一個字出現兩次。 */
  readonly heading?: string;
  readonly text: string;
  /** AI 建議的內容一律列出連結的網域；「現在的說明」不是 AI 寫的，維持預設。 */
  readonly linkHosts?: 'always';
}

export function DescriptionBox({ label, heading, text, linkHosts }: DescriptionBoxProps) {
  const boxRef = useRef<HTMLElement>(null);
  const noteId = useId();
  const isOverflowing = useOverflow(boxRef, text);
  return (
    <div className={styles.cell}>
      <section
        ref={boxRef}
        className={styles.descriptionBox}
        aria-label={label}
        aria-describedby={isOverflowing ? noteId : undefined}
        tabIndex={isOverflowing ? 0 : undefined}
      >
        {heading !== undefined && (
          <h4 className={styles.boxLabel} aria-hidden="true">
            {heading}
          </h4>
        )}
        {text.trim() === '' ? <span className={styles.before}>{EMPTY_VALUE}</span> : <Markdown text={text} mentionNames={NO_MENTIONS} hasAttachments={false} linkHosts={linkHosts} />}
      </section>
      {isOverflowing && (
        <p id={noteId} className={styles.scrollNote}>
          {SCROLL_NOTE}
        </p>
      )}
    </div>
  );
}

/** 說明預設展開：使用者很可能不會再點一下才看，所以現在的與建議的並排（窄的時候上下）擺出來，新增的連結與提及先講。 */
function DescriptionRow({ row }: { readonly row: DescriptionChange }) {
  const summary = useDescriptionSummary(row.before, row.text, row.members);
  return (
    <div className={styles.row}>
      <dt className={styles.label}>{row.label}</dt>
      <dd className={styles.value}>
        {summary !== null && <p className={styles.additions}>{summary}</p>}
        {row.text !== null && (
          <div className={styles.compare}>
            {row.before !== undefined && <DescriptionBox label="現在的說明" heading="現在的" text={row.before} />}
            <DescriptionBox label="建議的說明" heading="建議的" text={row.text} linkHosts="always" />
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
        {row.note !== undefined && <p className={styles.commentNote}>{row.note}</p>}
      </dd>
    </div>
  );
}

/** 「回覆 某某：摘錄」：留言是回覆哪一則。 */
export function replyText(replyTo: ReplyTarget): string {
  return `回覆 ${replyTo.authorName}：${replyTo.excerpt}`;
}

function CommentRow({ text, replyTo }: { readonly text: string; readonly replyTo: ReplyTarget | null }) {
  return (
    <div className={styles.row}>
      <dt className={styles.label}>留言</dt>
      <dd className={styles.value}>
        {replyTo !== null && <p className={styles.commentNote}>{replyText(replyTo)}</p>}
        <blockquote className={styles.quote}>
          <Markdown text={text} mentionNames={NO_MENTIONS} hasAttachments={false} linkHosts="always" />
        </blockquote>
        <p className={styles.commentNote}>{COMMENT_NOTE}</p>
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
        {proposal.comment !== null && <CommentRow text={proposal.comment} replyTo={proposal.replyTo} />}
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
  const planning = useProposalPlanningNames(
    proposal.projectKey,
    proposal.changes.some((change) => change.field === 'cycleId' && change.value !== null),
    proposal.changes.some((change) => change.field === 'milestoneId' && change.value !== null),
  );
  const rows = changeRows(proposal.changes, project.data, item.data, planning);
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
