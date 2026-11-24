import { useState } from 'react';
import { useCommentRevisions } from '../../api/queries';
import type { Activity } from '../../api/types';
import { Badge } from '../../components/Badge/Badge';
import { Button } from '../../components/Button/Button';
import { Dialog, DialogActions } from '../../components/Dialog/Dialog';
import { Markdown } from '../../components/Markdown/Markdown';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import { formatInstant } from '../../domain/time';
import { useMentionNames } from './useMentionNames';
import timeline from './ActivitySection.module.css';
import styles from './CommentEntry.module.css';

interface RevisionsProps {
  readonly itemKey: string;
  readonly activity: Activity;
}

function RevisionList({ itemKey, activity }: RevisionsProps) {
  const query = useCommentRevisions(itemKey, activity.id);
  const mentionNames = useMentionNames();
  if (query.isPending) {
    return <Skeleton label="正在載入版本…" variant="text" />;
  }
  if (query.data === undefined) {
    return (
      <ErrorState
        error={query.error}
        title="版本載入失敗"
        onRetry={() => {
          void query.refetch();
        }}
      />
    );
  }
  return (
    <ol className={styles.revisions} aria-label="版本">
      {query.data.items.map((revision) => (
        <li key={revision.number} className={styles.revision}>
          <p className={styles.revisionHead}>
            <span className={styles.revisionNumber}>第 {revision.number} 版</span>
            <time dateTime={revision.at}>{formatInstant(revision.at)}</time>
            {revision.isCurrent && <Badge tone="accent">目前</Badge>}
          </p>
          <div className={timeline.comment}>
            <Markdown text={revision.body} mentionNames={mentionNames} />
          </div>
        </li>
      ))}
    </ol>
  );
}

/** 「留言的版本」：新的在前，第一筆是現在的內容；沒有「還原到這個版本」（HANDOFF-comment-edit 第 2 節第 14 點）。 */
function RevisionsDialog({ itemKey, activity, onClose }: RevisionsProps & { readonly onClose: () => void }) {
  return (
    <Dialog title="留言的版本" description={`${activity.actor.displayName} 的留言，新的在前。`} onClose={onClose} size="lg">
      <RevisionList itemKey={itemKey} activity={activity} />
      <DialogActions>
        <Button onClick={onClose}>關閉</Button>
      </DialogActions>
    </Dialog>
  );
}

/** 時間後面的「已編輯」：小的文字按鈕，滑過看編輯的時間，按了開版本的對話框。刪除的留言不顯示（呼叫端擋）。 */
export function EditedMark({ itemKey, activity }: RevisionsProps) {
  const [isOpen, setIsOpen] = useState(false);
  const { editedAt } = activity;
  if (editedAt === null) return null;
  return (
    <>
      <span className={styles.editedSeparator} aria-hidden="true">
        ・
      </span>
      <button
        type="button"
        className={styles.editedButton}
        title={`編輯於 ${formatInstant(editedAt)}`}
        // 名稱以看得見的字開頭（label in name），後面補是哪一則：一串留言裡有好幾顆。
        aria-label={`已編輯（查看 ${activity.actor.displayName} 的留言的版本）`}
        onClick={() => {
          setIsOpen(true);
        }}
      >
        已編輯
      </button>
      {isOpen && (
        <RevisionsDialog
          itemKey={itemKey}
          activity={activity}
          onClose={() => {
            setIsOpen(false);
          }}
        />
      )}
    </>
  );
}
