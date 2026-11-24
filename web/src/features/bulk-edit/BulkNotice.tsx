import { useEffect, useRef, type ReactNode, type Ref } from 'react';
import { Link } from 'react-router';
import type { BulkItemFailure } from '../../api/itemRecovery';
import { Button } from '../../components/Button/Button';
import { Notice, type NoticeTone } from '../../components/Notice/Notice';
import { useItemPanelLink } from '../item-page/ItemPanelContext';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import type { BulkEdit, BulkNotice as BulkNoticeValue } from './useBulkEdit';
import styles from './BulkEdit.module.css';

/** 一種結果的字：成功的數量、沒做成的數量、動詞（改、刪、復原）。 */
interface Wording {
  readonly done: (count: number) => string;
  readonly notDone: string;
}

const WORDING: Readonly<Record<'updated' | 'deleted' | 'restored', Wording>> = {
  updated: { done: (count) => `改好 ${String(count)} 個`, notDone: '沒改' },
  deleted: { done: (count) => `已刪除 ${String(count)} 個項目`, notDone: '沒刪' },
  restored: { done: (count) => `已復原 ${String(count)} 個項目`, notDone: '沒復原' },
};

/** 全部成功是句點；有失敗時接「；M 個沒改：」，一個都沒成功是「M 個都沒改：」。 */
function summaryOf(kind: keyof typeof WORDING, doneCount: number, failedCount: number): string {
  const { done, notDone } = WORDING[kind];
  if (failedCount === 0) return `${done(doneCount)}。`;
  if (doneCount === 0) return `${String(failedCount)} 個都${notDone}：`;
  return `${done(doneCount)}；${String(failedCount)} 個${notDone}：`;
}

function toneOf(doneCount: number, failedCount: number): NoticeTone {
  if (failedCount === 0) return 'success';
  return doneCount === 0 ? 'danger' : 'warning';
}

/** 一筆沒做成：編號連到項目（在旁邊開面板，和表格的編號一樣），後面是伺服器的說明。 */
function FailureLine({ projectKey, failure }: { readonly projectKey: string; readonly failure: BulkItemFailure }) {
  const entry = useItemPageEntry();
  const panelLink = useItemPanelLink(failure.key);
  return (
    <li>
      <Link to={itemPagePath(projectKey, failure.key)} state={entry} className={styles.failureKey} {...panelLink}>
        {failure.key}
      </Link>
      ：{failure.error.message}
    </li>
  );
}

function Failures({ projectKey, failed }: { readonly projectKey: string; readonly failed: readonly BulkItemFailure[] }) {
  return (
    <ul className={styles.failures} aria-label="沒做成的項目">
      {failed.map((failure) => (
        <FailureLine key={failure.key} projectKey={projectKey} failure={failure} />
      ))}
    </ul>
  );
}

interface ResultProps {
  readonly projectKey: string;
  readonly kind: keyof typeof WORDING;
  readonly doneCount: number;
  readonly failed: readonly BulkItemFailure[];
  readonly onDismiss: () => void;
  readonly actions?: ReactNode;
  /** 動作失敗的原因：接在結果下面（alert）。 */
  readonly error?: string;
  readonly noticeRef?: Ref<HTMLDivElement>;
}

function Result({ projectKey, kind, doneCount, failed, onDismiss, actions, error, noticeRef }: ResultProps) {
  return (
    <Notice
      tone={toneOf(doneCount, failed.length)}
      actions={actions}
      onDismiss={onDismiss}
      className={styles.notice}
      ref={noticeRef}
      tabIndex={noticeRef === undefined ? undefined : -1}
    >
      <>
        {summaryOf(kind, doneCount, failed.length)}
        {failed.length > 0 && <Failures projectKey={projectKey} failed={failed} />}
        {error !== undefined && <p role="alert">{error}</p>}
      </>
    </Notice>
  );
}

interface BulkNoticeProps {
  readonly projectKey: string;
  readonly bulk: BulkEdit;
}

/**
 * 表格上方的批次結果（docs/DESIGN.md 8.3「批次操作」）：全部成功的修改不提示，其他都在這裡。
 * 有沒做成的：逐筆列出編號與原因，編號連到項目；全部失敗是錯誤、部分失敗是警告。
 * 刪除之後有「復原」：按了之後同一個位置換成「已復原 N 個項目。」。
 */
export function BulkNotice({ projectKey, bulk }: BulkNoticeProps) {
  const { notice } = bulk;
  const noticeRef = useRef<HTMLDivElement>(null);
  const isRestored = notice?.kind === 'restored';
  // 「復原」成功之後按鈕跟著不見：焦點移到結果的提示本身（不然掉到頁面本體）。
  useEffect(() => {
    if (isRestored) noticeRef.current?.focus();
  }, [isRestored]);
  if (notice === null) return null;
  if (notice.kind === 'error') {
    return (
      <Notice tone="danger" onDismiss={bulk.dismissNotice} className={styles.notice}>
        {notice.message}
      </Notice>
    );
  }
  const common = { projectKey, onDismiss: bulk.dismissNotice };
  if (notice.kind === 'updated') {
    return <Result {...common} kind="updated" doneCount={notice.updatedCount} failed={notice.failed} />;
  }
  if (notice.kind === 'restored') {
    return <Result {...common} kind="restored" doneCount={notice.count} failed={notice.failed} noticeRef={noticeRef} />;
  }
  return <DeletedResult {...common} notice={notice} onUndo={bulk.undoRemove} />;
}

function DeletedResult({
  projectKey,
  notice,
  onUndo,
  onDismiss,
}: {
  readonly projectKey: string;
  readonly notice: Extract<BulkNoticeValue, { readonly kind: 'deleted' }>;
  readonly onUndo: () => Promise<void>;
  readonly onDismiss: () => void;
}) {
  const undo =
    notice.keys.length === 0 ? undefined : (
      <Button
        size="sm"
        aria-disabled={notice.isRestoring || undefined}
        onClick={() => {
          if (!notice.isRestoring) void onUndo();
        }}
      >
        {notice.isRestoring ? '復原中…' : '復原'}
      </Button>
    );
  return (
    <Result projectKey={projectKey} kind="deleted" doneCount={notice.keys.length} failed={notice.failed} actions={undo} onDismiss={onDismiss} error={notice.restoreError} />
  );
}
