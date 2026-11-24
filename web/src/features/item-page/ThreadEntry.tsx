import { useEffect, useRef, useState } from 'react';
import type { Activity } from '../../api/types';
import { Button } from '../../components/Button/Button';
import type { ActivityContext } from '../../domain/activity';
import { draftKey, readDraft } from '../../lib/draftStorage';
import { ActivityEntry } from './ActivityEntry';
import { CommentForm } from './CommentForm';
import type { Thread } from './threads';
import type { CommentFlash } from './useCommentTarget';
import styles from './ActivitySection.module.css';

/** 回覆框的狀態：restored 是換頁、重新整理後因為有沒送出的草稿而開著，不搶焦點。 */
type ReplyBoxState = 'closed' | 'restored' | 'opened';

interface ReplyBoxProps {
  readonly itemKey: string;
  readonly comment: Activity;
  readonly onPosted: (activity: Activity) => void;
}

/**
 * 一串最下面的「回覆」：按了就地換成回覆框（同一個留言框，焦點進去）；取消、或框是空的時按 Esc 收起來，焦點回到「回覆」。
 * 有沒送出的草稿時一開始就是開的（草稿依「項目＋那一則」存，回覆框不開，使用者就不知道有草稿）。
 */
function ReplyBox({ itemKey, comment, onPosted }: ReplyBoxProps) {
  const [state, setState] = useState<ReplyBoxState>(() =>
    readDraft(draftKey('comment', itemKey, comment.id)) === null ? 'closed' : 'restored',
  );
  const buttonRef = useRef<HTMLButtonElement>(null);
  const shouldRefocus = useRef(false);
  useEffect(() => {
    if (state === 'closed' && shouldRefocus.current) {
      shouldRefocus.current = false;
      buttonRef.current?.focus();
    }
  }, [state]);
  if (state === 'closed') {
    return (
      <div className={styles.replyAction}>
        <Button
          ref={buttonRef}
          size="sm"
          variant="ghost"
          aria-label={`回覆 ${comment.actor.displayName} 的留言`}
          onClick={() => {
            setState('opened');
          }}
        >
          回覆
        </Button>
      </div>
    );
  }
  return (
    <CommentForm
      itemKey={itemKey}
      onPosted={onPosted}
      reply={{
        commentId: comment.id,
        authorName: comment.actor.displayName,
        autoFocus: state === 'opened',
        onClose: () => {
          shouldRefocus.current = true;
          setState('closed');
        },
      }}
    />
  );
}

interface ThreadEntryProps {
  readonly itemKey: string;
  readonly thread: Thread;
  readonly contextOf: (activity: Activity) => ActivityContext;
  readonly now: number;
  readonly flash: CommentFlash | null;
  /** 有 comment.create：才有「回覆」。 */
  readonly canComment: boolean;
  readonly onPosted: (activity: Activity) => void;
}

/** 時間軸的一筆最上層活動；是留言的話，底下依時間列出它的回覆，最下面是「回覆」。 */
export function ThreadEntry({ itemKey, thread, contextOf, now, flash, canComment, onPosted }: ThreadEntryProps) {
  const { activity, replies } = thread;
  // 刪除的留言復原之前不能回覆（伺服器也是 409 comment.deleted）。
  const canReply = canComment && activity.kind === 'commented' && activity.replyToId === null && activity.deletedAt === null;
  const phaseOf = (entry: Activity) => (entry.id === flash?.id ? flash.phase : undefined);
  return (
    <ActivityEntry
      itemKey={itemKey}
      activity={activity}
      context={contextOf(activity)}
      now={now}
      highlight={phaseOf(activity)}
      isDetachedReply={thread.isDetached}
      group={thread.group}
    >
      {replies.length > 0 && (
        <ol className={styles.replies} aria-label={`回覆 ${activity.actor.displayName} 的留言的回覆`}>
          {replies.map((reply) => (
            <ActivityEntry key={reply.id} itemKey={itemKey} activity={reply} context={contextOf(reply)} now={now} highlight={phaseOf(reply)} isReply />
          ))}
        </ol>
      )}
      {canReply && <ReplyBox itemKey={itemKey} comment={activity} onPosted={onPosted} />}
    </ActivityEntry>
  );
}
