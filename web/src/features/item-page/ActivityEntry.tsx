import type { ReactNode } from 'react';
import type { Activity } from '../../api/types';
import { Avatar } from '../../components/Avatar/Avatar';
import {
  attachmentDescription,
  attachmentGroupDescription,
  DELETED_TEXT,
  describeChange,
  migrationLead,
  migrationParts,
  restoreDescription,
  transitionText,
  typeChangeDescription,
  type ActivityContext,
  type Segment,
} from '../../domain/activity';
import { formatInstant, formatRelativeTime } from '../../domain/time';
import { cx } from '../../lib/cx';
import type { FlashPhase } from './useCommentTarget';
import { CommentActions } from './CommentActions';
import { CommentBody } from './CommentBody';
import { EditedMark } from './CommentRevisionsDialog';
import { useCommentEntry } from './useCommentEntry';
import styles from './ActivitySection.module.css';
import commentStyles from './CommentEntry.module.css';

function Segments({ segments }: { readonly segments: readonly Segment[] }) {
  return segments.map((segment, index) =>
    segment.kind === 'value' ? (
      // 片段的順序固定，不會重排，用位置當 key 就夠。
      <span key={index} className={styles.value}>
        {segment.text}
      </span>
    ) : (
      segment.text
    ),
  );
}

function UpdatedAction({ activity, context }: EntryProps) {
  const descriptions = activity.changes.map((change) => describeChange(change, context));
  const [only] = descriptions;
  if (descriptions.length === 1 && only !== undefined) {
    return <Segments segments={only.segments} />;
  }
  return `修改了 ${String(descriptions.length)} 個欄位`;
}

function ChangeLines({ activity, context }: EntryProps) {
  if (activity.changes.length < 2) {
    return null;
  }
  return (
    <ul className={styles.changes} aria-label="修改的欄位">
      {activity.changes.map((change) => (
        <li key={change.field}>
          <Segments segments={describeChange(change, context).segments} />
        </li>
      ))}
    </ul>
  );
}

/** 套用設定變更時的搬移：只有一件事就是一句話；好幾件事時每一件一行。 */
function MigratedAction({ activity, context }: EntryProps) {
  const parts = migrationParts(activity, context);
  const [only] = parts;
  if (parts.length === 1 && only !== undefined) {
    return (
      <>
        {migrationLead(activity)}，<Segments segments={only} />
      </>
    );
  }
  return `${migrationLead(activity)}：`;
}

function MigrationLines({ activity, context }: EntryProps) {
  const parts = activity.kind === 'migrated' ? migrationParts(activity, context) : [];
  if (parts.length < 2) {
    return null;
  }
  return (
    <ul className={styles.changes} aria-label="搬移的內容">
      {parts.map((segments, index) => (
        // 每一行的順序固定（狀態在前，欄位照活動裡的順序），不會重排。
        <li key={index}>
          <Segments segments={segments} />
        </li>
      ))}
    </ul>
  );
}

function Action({ activity, context, group }: EntryProps) {
  switch (activity.kind) {
    case 'created':
      return '建立了這個項目';
    case 'transitioned':
      return transitionText(activity, context);
    case 'commented':
      return '留言';
    case 'updated':
      return <UpdatedAction activity={activity} context={context} />;
    case 'migrated':
      return <MigratedAction activity={activity} context={context} />;
    case 'type_changed':
      return <Segments segments={typeChangeDescription(activity, context).segments} />;
    case 'deleted':
      return DELETED_TEXT;
    case 'restored':
      return <Segments segments={restoreDescription(activity, context).segments} />;
    case 'attachment_added':
      // 連續附加的檔案併成一行（threads.ts）：「附加了 3 個檔案：a.png、b.png、c.png」。
      return <Segments segments={(group !== undefined && group.length > 1 ? attachmentGroupDescription(group) : attachmentDescription(activity)).segments} />;
    case 'attachment_removed':
      return <Segments segments={attachmentDescription(activity).segments} />;
  }
}

interface EntryProps {
  readonly activity: Activity;
  readonly context: ActivityContext;
  /** 併成一行的附加檔案（含 activity 自己）；其他活動不給。 */
  readonly group?: readonly Activity[] | undefined;
}

interface ActivityEntryProps extends EntryProps {
  /** 留言的編輯、刪除、復原要打的項目。 */
  readonly itemKey: string;
  /** 算相對時間用的「現在」：整條時間軸用同一個值。 */
  readonly now: number;
  /** 網址指的是這一筆（點了由留言產生的通知）：留言的卡片閃一下，a／b 交替讓動畫重來，見 ActivitySection.module.css。 */
  readonly highlight?: FlashPhase;
  /** 這一筆是某一則留言底下的回覆：小一號的間距（回覆的串在 Thread.tsx）。 */
  readonly isReply?: boolean;
  /** 回覆一則不在已載入活動裡的留言（不應該發生）：前面註明，內文照一般留言放著。 */
  readonly isDetachedReply?: boolean;
  /** 接在這一筆的內文後面：回覆的清單、「回覆」的按鈕與回覆框。 */
  readonly children?: ReactNode;
}

/**
 * 時間軸的一筆：左邊的軌道上是作者的頭像（旁邊就有名字，只是裝飾），系統事件是一行小字；留言多一塊內文，看得出是人寫的。
 */
export function ActivityEntry({ itemKey, activity, context, now, highlight, isReply = false, isDetachedReply = false, group, children }: ActivityEntryProps) {
  const isComment = activity.kind === 'commented';
  const { control, refs } = useCommentEntry(itemKey, activity);
  // 作者與時間是原本的，要看得出不是在這裡發生的；併成一行的附件只要有一筆是匯入的就標。
  const importedFrom = (group ?? [activity]).find((entry) => entry.importedFrom !== null)?.importedFrom ?? null;
  return (
    <li
      className={cx(styles.entry, isComment && styles.commentEntry, isReply && styles.replyEntry)}
      data-activity-id={activity.id}
      data-highlighted={highlight}
    >
      <span className={styles.marker}>
        <Avatar name={activity.actor.displayName} size="sm" decorative />
      </span>
      <div className={styles.body}>
        <p className={cx(styles.line, isComment && commentStyles.commentLine)}>
          <span className={styles.summary}>
            <span className={styles.actor}>{activity.actor.displayName}</span>{' '}
            <span className={styles.action}>
              <Action activity={activity} context={context} group={group} />
            </span>
          </span>{' '}
          <span className={styles.stamp}>
            <time className={styles.time} dateTime={activity.at} title={formatInstant(activity.at)}>
              {formatRelativeTime(activity.at, now)}
            </time>
            {isComment && activity.deletedAt === null && <EditedMark itemKey={itemKey} activity={activity} />}
            {importedFrom !== null && <span className={styles.time}>・從 {importedFrom} 匯入</span>}
          </span>
          {isComment && <CommentActions itemKey={itemKey} activity={activity} control={control} triggerRef={refs.triggerRef} />}
        </p>
        {activity.kind === 'updated' && <ChangeLines activity={activity} context={context} />}
        <MigrationLines activity={activity} context={context} />
        {isDetachedReply && <p className={styles.replyLead}>回覆一則較早的留言</p>}
        {isComment && <CommentBody itemKey={itemKey} activity={activity} control={control} bodyRef={refs.bodyRef} />}
        {children}
      </div>
    </li>
  );
}
