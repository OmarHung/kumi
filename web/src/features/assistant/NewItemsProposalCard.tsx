import { useId, useState } from 'react';
import { Link } from 'react-router';
import type { NewItemProposal } from '../../api/aiTypes';
import { useDecideNewItemProposal, useNewItemProposal } from '../../api/assistant';
import { errorMessage, isApiError } from '../../api/http';
import { useProject } from '../../api/queries';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { itemPagePath } from '../work-items/itemLinks';
import { newItemRows, type NewItemRow, type ProjectLoadState } from './newItemRows';
import { DescriptionBox } from './ProposalCard';
import { cx } from '../../lib/cx';
import styles from './ProposalCard.module.css';

const DEPTH_CLASSES = [undefined, styles.depth1, styles.depth2, styles.depth3] as const;

const CLOSED_CODE = 'ai.new_item_proposal_closed';
const CLOSED_TEXT = '這份建議已經處理過了。';

function failureText(error: unknown): string {
  if (isApiError(error) && error.code === CLOSED_CODE) return CLOSED_TEXT;
  return errorMessage(error);
}

function ParentNote({ row, projectKey }: { readonly row: NewItemRow; readonly projectKey: string }) {
  if (row.parentKey !== null) {
    return (
      <>
        在 <Link to={itemPagePath(projectKey, row.parentKey)}>{row.parentKey}</Link> 底下
      </>
    );
  }
  return row.parentTitle === null ? null : <>{`在「${row.parentTitle}」底下`}</>;
}

function ItemRow({ row, projectKey }: { readonly row: NewItemRow; readonly projectKey: string }) {
  const [isOpen, setIsOpen] = useState(false);
  const descriptionId = useId();
  const hasParent = row.parentKey !== null || row.parentTitle !== null;
  return (
    <li className={cx(styles.draft, DEPTH_CLASSES[row.depth])}>
      <p className={styles.draftTitle}>
        <span className={styles.draftType}>{row.typeName}</span> {row.title}
      </p>
      {(hasParent || row.details.length > 0) && (
        <p className={styles.draftMeta}>
          {hasParent && <ParentNote row={row} projectKey={projectKey} />}
          {hasParent && row.details.length > 0 && ' · '}
          {row.details.join(' · ')}
        </p>
      )}
      {row.description !== null && (
        <>
          <Button
            variant="ghost"
            size="sm"
            className={styles.draftToggle}
            aria-label={`說明：${row.title}`}
            aria-expanded={isOpen}
            aria-controls={isOpen ? descriptionId : undefined}
            onClick={() => {
              setIsOpen((open) => !open);
            }}
          >
            {isOpen ? '收起說明' : '展開說明'}
          </Button>
          {isOpen && (
            <div id={descriptionId}>
              <DescriptionBox label={`說明內容：${row.title}`} text={row.description} linkHosts="always" />
            </div>
          )}
        </>
      )}
    </li>
  );
}

function PendingBody({
  proposal,
  rows,
  projectState,
}: {
  readonly proposal: NewItemProposal;
  readonly rows: readonly NewItemRow[];
  readonly projectState: ProjectLoadState;
}) {
  const { apply, dismiss } = useDecideNewItemProposal(proposal);
  const isWorking = apply.isPending || dismiss.isPending;
  // 專案（成員、藍圖）還沒讀到時畫面上的負責人、類型都不可靠，不讓人照它按建立。
  const isBlocked = isWorking || projectState === 'loading';
  const failure: unknown = apply.error ?? dismiss.error;
  return (
    <>
      <ol className={styles.drafts}>
        {rows.map((row, index) => (
          <ItemRow key={index} row={row} projectKey={proposal.projectKey} />
        ))}
      </ol>
      {projectState === 'loading' && <p className={styles.loading}>正在載入專案的成員與欄位…</p>}
      <div className={styles.actions}>
        <Button
          variant="primary"
          size="sm"
          aria-disabled={isBlocked || undefined}
          onClick={() => {
            if (isBlocked) return;
            dismiss.reset();
            apply.mutate();
          }}
        >
          {apply.isPending ? '建立中…' : rows.length === 1 ? '建立' : '全部建立'}
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

function CreatedKeys({ proposal }: { readonly proposal: NewItemProposal }) {
  const keys = proposal.createdKeys.filter((key): key is string => key !== null);
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  if (keys.length === 0) return <p className={styles.summary}>{`沒有建立任何項目${when}`}</p>;
  return (
    <p className={styles.summary}>
      已建立{' '}
      {keys.map((key, index) => (
        <span key={key}>
          {index > 0 && '、'}
          <Link to={itemPagePath(proposal.projectKey, key)} className={styles.code}>
            {key}
          </Link>
        </span>
      ))}
      {when}
    </p>
  );
}

function ClosedBody({ proposal }: { readonly proposal: NewItemProposal }) {
  if (proposal.status === 'applied') {
    return (
      <>
        <CreatedKeys proposal={proposal} />
        {proposal.error !== null && <Notice tone="warning">{proposal.error}</Notice>}
      </>
    );
  }
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  return <p className={styles.status}>{`已略過${when}`}</p>;
}

function LoadedCard({ proposal }: { readonly proposal: NewItemProposal }) {
  const project = useProject(proposal.projectKey);
  const projectState: ProjectLoadState = project.data !== undefined ? 'ready' : project.isError ? 'error' : 'loading';
  const rows = newItemRows(proposal.items, project.data, projectState);
  return (
    <section className={styles.card} aria-label={`新增項目建議 ${proposal.projectKey}`}>
      <p className={styles.title}>{`建議新增 ${proposal.items.length} 個項目（${proposal.projectKey}）`}</p>
      {proposal.status === 'pending' && <PendingBody proposal={proposal} rows={rows} projectState={projectState} />}
      <div role="status">
        {proposal.status === 'applying' && <p className={styles.status}>建立中…</p>}
        {(proposal.status === 'applied' || proposal.status === 'dismissed') && <ClosedBody proposal={proposal} />}
      </div>
    </section>
  );
}

/**
 * 助理的 propose_new_items：列出要新增的項目（類型、標題、上層、欄位），按「全部建立」才依序以自己的身分建立
 * （internal/HANDOFF-ai-new-items.md 第 6 節）。不能只勾其中幾個；不要的話請助理重提。
 */
export function NewItemsProposalCard({ proposalId }: { readonly proposalId: string }) {
  const query = useNewItemProposal(proposalId);
  // 背景重讀失敗時 data 還在、status 卻是 error：有資料就照舊顯示，只有完全沒資料才換成讀不到。
  const data: NewItemProposal | undefined = query.data;
  if (data !== undefined) return <LoadedCard proposal={data} />;
  if (query.isError) return <Notice tone="warning">{`讀不到這份新增項目建議：${errorMessage(query.error)}`}</Notice>;
  return <p className={styles.loading}>正在載入新增項目建議…</p>;
}
