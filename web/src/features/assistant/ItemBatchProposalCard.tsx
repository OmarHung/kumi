import { useId, useState } from 'react';
import { Link } from 'react-router';
import type { ItemBatchEntry, ItemBatchProposal, ReplyTarget } from '../../api/aiTypes';
import { useProposalPlanningNames } from '../../api/assistant';
import { useDecideItemBatchProposal, useItemBatchProposal } from '../../api/itemRevisionProposals';
import { errorMessage, isApiError } from '../../api/http';
import { useItem, useProject } from '../../api/queries';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { Markdown } from '../../components/Markdown/Markdown';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { itemPagePath } from '../work-items/itemLinks';
import { changeRows, type ChangeRow } from './proposalChanges';
import { DescriptionBox, replyText } from './ProposalCard';
import styles from './ProposalCard.module.css';

const NO_MENTIONS: ReadonlyMap<string, string> = new Map();
const CONFLICT_CODE = 'item.description_conflict';
const CLOSED_CODE = 'ai.item_batch_proposal_closed';
const CONFLICT_TEXT = '這個項目在建議之後被改過，請重新請助理建議。';
const CLOSED_TEXT = '這份建議已經處理過了。';

function failureText(error: unknown): string | null {
  if (error === null || error === undefined) return null;
  if (isApiError(error) && error.code === CONFLICT_CODE) return CONFLICT_TEXT;
  if (isApiError(error) && error.code === CLOSED_CODE) return CLOSED_TEXT;
  return errorMessage(error);
}

function EntryHead({ entry, projectKey }: { readonly entry: ItemBatchEntry; readonly projectKey: string }) {
  return (
    <p className={styles.draftTitle}>
      <Link to={itemPagePath(projectKey, entry.itemKey)} className={styles.code}>
        {entry.itemKey}
      </Link>{' '}
      {entry.itemTitle}
    </p>
  );
}

function ValueLine({ row }: { readonly row: Extract<ChangeRow, { kind: 'value' }> }) {
  return (
    <p className={styles.fieldLine}>
      <span className={styles.before}>{`${row.label}：`}</span>
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
      {row.note !== undefined && <span className={styles.before}>{`（${row.note}）`}</span>}
    </p>
  );
}

/**
 * 說明很長，批次最多 20 個項目、320px 寬的浮動對話框會太長，所以預設收起（單一項目的卡片預設展開）；展開才看現在的與建議的。
 * 建議的型別對不上（text 是 null）時沒有東西可展開，不畫按鈕。
 */
function DescriptionLine({ row, itemKey }: { readonly row: Extract<ChangeRow, { kind: 'description' }>; readonly itemKey: string }) {
  const [isOpen, setIsOpen] = useState(false);
  const boxId = useId();
  return (
    <div className={styles.fieldLine}>
      <span className={styles.before}>{`${row.label}：`}</span>
      <span className={styles.after}>改寫</span>
      {row.text !== null && (
        <Button
          variant="ghost"
          size="sm"
          aria-expanded={isOpen}
          aria-controls={isOpen ? boxId : undefined}
          onClick={() => {
            setIsOpen((open) => !open);
          }}
        >
          {isOpen ? '收起說明 ' : '展開說明 '}
          <span className="visually-hidden">{itemKey}</span>
        </Button>
      )}
      {isOpen && row.text !== null && (
        <div id={boxId} className={styles.compare}>
          {row.before !== undefined && <DescriptionBox label={`現在的說明：${itemKey}`} heading="現在的" text={row.before} />}
          <DescriptionBox label={`建議的說明：${itemKey}`} heading="建議的" text={row.text} linkHosts="always" />
        </div>
      )}
    </div>
  );
}

function CommentLine({ text, itemKey, replyTo }: { readonly text: string; readonly itemKey: string; readonly replyTo: ReplyTarget | null }) {
  const [isOpen, setIsOpen] = useState(false);
  const quoteId = useId();
  return (
    <div className={styles.fieldLine}>
      <Button
        variant="ghost"
        size="sm"
        aria-expanded={isOpen}
        aria-controls={isOpen ? quoteId : undefined}
        onClick={() => {
          setIsOpen((open) => !open);
        }}
      >
        {'加留言 '}
        <span className="visually-hidden">{itemKey}</span>
      </Button>
      {replyTo !== null && <span className={styles.before}>{replyText(replyTo)}</span>}
      {isOpen && (
        <blockquote id={quoteId} className={styles.quote}>
          <Markdown text={text} mentionNames={NO_MENTIONS} hasAttachments={false} linkHosts="always" />
        </blockquote>
      )}
    </div>
  );
}

/** 這一筆有排週期、里程碑時才讀名稱（同一個專案的各筆共用同一份快取）。 */
function useEntryPlanningNames(entry: ItemBatchEntry, projectKey: string) {
  return useProposalPlanningNames(
    projectKey,
    entry.changes.some((change) => change.field === 'cycleId' && change.value !== null),
    entry.changes.some((change) => change.field === 'milestoneId' && change.value !== null),
  );
}

/** 一個項目一個查詢（最多 20 個）：各讀各的，才知道每一個「現在」的值。 */
function PendingEntry({ entry, projectKey }: { readonly entry: ItemBatchEntry; readonly projectKey: string }) {
  const project = useProject(projectKey);
  const item = useItem(entry.itemKey);
  const planning = useEntryPlanningNames(entry, projectKey);
  const rows = changeRows(entry.changes, project.data, item.data, planning);
  return (
    <li className={styles.entry}>
      <EntryHead entry={entry} projectKey={projectKey} />
      {rows.map((row) =>
        row.kind === 'description' ? <DescriptionLine key={row.field} row={row} itemKey={entry.itemKey} /> : <ValueLine key={row.field} row={row} />,
      )}
      {entry.comment !== null && <CommentLine text={entry.comment} itemKey={entry.itemKey} replyTo={entry.replyTo} />}
    </li>
  );
}

function PendingBody({ proposal }: { readonly proposal: ItemBatchProposal }) {
  const { apply, dismiss } = useDecideItemBatchProposal(proposal);
  const isWorking = apply.isPending || dismiss.isPending;
  return (
    <>
      <ol className={styles.drafts}>
        {proposal.items.map((entry) => (
          <PendingEntry key={entry.itemKey} entry={entry} projectKey={proposal.projectKey} />
        ))}
      </ol>
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
          {apply.isPending ? '套用中…' : proposal.items.length === 1 ? '套用' : '全部套用'}
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

/** 套用之後留著每個項目改了哪些欄位（建議的值）；說明很長只寫「改寫」。 */
function AppliedEntry({ entry, projectKey }: { readonly entry: ItemBatchEntry; readonly projectKey: string }) {
  const project = useProject(projectKey);
  const planning = useEntryPlanningNames(entry, projectKey);
  const rows = changeRows(entry.changes, project.data, undefined, planning);
  return (
    <li className={styles.entry}>
      <EntryHead entry={entry} projectKey={projectKey} />
      {rows.map((row) =>
        row.kind === 'description' ? (
          <p key={row.field} className={styles.fieldLine}>
            <span className={styles.before}>{`${row.label}：`}</span>
            <span className={styles.after}>改寫</span>
          </p>
        ) : (
          <ValueLine key={row.field} row={row} />
        ),
      )}
      {entry.comment !== null && <p className={styles.fieldLine}>加了一則留言</p>}
      {entry.error === null ? (
        <p className={styles.entryResult}>
          <Icon name="check" />
          <span>已套用</span>
        </p>
      ) : (
        <Notice tone="warning">{`其餘已套用；沒套用的：${entry.error}`}</Notice>
      )}
    </li>
  );
}

function ClosedBody({ proposal }: { readonly proposal: ItemBatchProposal }) {
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  if (proposal.status !== 'applied') return <p className={styles.status}>{`已略過${when}`}</p>;
  return (
    <>
      <ol className={styles.drafts}>
        {proposal.items.map((entry) => (
          <AppliedEntry key={entry.itemKey} entry={entry} projectKey={proposal.projectKey} />
        ))}
      </ol>
      {proposal.error !== null && <Notice tone="warning">{proposal.error}</Notice>}
    </>
  );
}

/**
 * 助理的 propose_bulk_item_changes：每個項目各自的欄位修改與留言，按一次「全部套用」才以自己的身分照順序寫入
 * （internal/HANDOFF-ai-revise.md 第 7 節）。
 */
export function ItemBatchProposalCard({ proposalId }: { readonly proposalId: string }) {
  const query = useItemBatchProposal(proposalId);
  const proposal: ItemBatchProposal | undefined = query.data;
  if (proposal === undefined) {
    if (query.isError) return <Notice tone="warning">{`讀不到這份批次修改建議：${errorMessage(query.error)}`}</Notice>;
    return <p className={styles.loading}>正在載入批次修改建議…</p>;
  }
  return (
    <section className={styles.card} aria-label={`批次修改建議 ${proposal.projectKey}`}>
      <p className={styles.title}>{`建議修改 ${String(proposal.items.length)} 個項目（${proposal.projectKey}）`}</p>
      {proposal.status === 'pending' && <PendingBody proposal={proposal} />}
      <div role="status">{proposal.status !== 'pending' && <ClosedBody proposal={proposal} />}</div>
    </section>
  );
}
