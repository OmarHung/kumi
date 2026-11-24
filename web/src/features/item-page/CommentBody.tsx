import { useEffect, useRef, type RefObject } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { restoreComment } from '../../api/activity';
import { errorMessage } from '../../api/http';
import type { Activity } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Markdown } from '../../components/Markdown/Markdown';
import { cx } from '../../lib/cx';
import { replaceActivity } from './activityCache';
import { useCommentAnnouncer } from './commentAnnouncer';
import { CommentEditForm } from './CommentEditForm';
import type { CommentEntryControl } from './useCommentEntry';
import { useMentionNames } from './useMentionNames';
import timeline from './ActivitySection.module.css';
import styles from './CommentEntry.module.css';

interface BodyProps {
  readonly itemKey: string;
  readonly activity: Activity;
  readonly control: CommentEntryControl;
}

interface CardProps extends BodyProps {
  /** 卡片（內文或佔位）：刪除、復原之後焦點放到這裡。 */
  readonly bodyRef: RefObject<HTMLDivElement | null>;
}

/** 刪除的人不是作者（管理專案的人刪的）要說是誰刪的；作者自己刪的只說已刪除。 */
export function deletedText(activity: Activity): string {
  const { deletedBy } = activity;
  if (deletedBy === null || deletedBy.personId === activity.actor.personId) return '這則留言已刪除。';
  return `這則留言已由 ${deletedBy.displayName} 刪除。`;
}

/** 復原：一按就復原、不另外確認；成功用回傳的活動換掉那一筆，焦點放到復原的留言上，失敗把伺服器的訊息放在那一行。 */
function RestoreButton({ itemKey, activity, control }: BodyProps) {
  const queryClient = useQueryClient();
  const announce = useCommentAnnouncer();
  const restore = useMutation({
    mutationFn: () => restoreComment(itemKey, activity.id),
    onSuccess: async (restored) => {
      control.requestBodyFocus();
      await replaceActivity(queryClient, itemKey, restored);
      announce('已復原留言');
    },
  });
  const name = `復原 ${activity.actor.displayName} 的留言`;
  return (
    <>
      <Button
        size="sm"
        variant="ghost"
        // 送出中停在按鈕上（aria-disabled）：停用會讓焦點掉到頁面本體。
        aria-disabled={restore.isPending || undefined}
        // 送出中名稱跟著變、也標 aria-busy：按鈕留著焦點，報讀軟體才聽得到「進行中」。
        aria-busy={restore.isPending || undefined}
        aria-label={restore.isPending ? `${name}（復原中）` : name}
        onClick={() => {
          if (!restore.isPending) restore.mutate();
        }}
      >
        {restore.isPending ? '復原中…' : '復原'}
      </Button>
      {restore.isError && (
        <span role="alert" className={styles.restoreError}>
          {errorMessage(restore.error)}
        </span>
      )}
    </>
  );
}

/** 刪除的留言留在原位：內文換成淡字的說明；有權限復原的人在同一行有「復原」。 */
function DeletedBody({ itemKey, activity, control, bodyRef }: CardProps) {
  return (
    <div ref={bodyRef} className={cx(timeline.comment, styles.deletedComment)} data-comment-card="" data-deleted="">
      <p className={styles.deletedLine}>
        <span>{deletedText(activity)}</span>
        {control.abilities.canRestore && <RestoreButton itemKey={itemKey} activity={activity} control={control} />}
      </p>
    </div>
  );
}

function ShownBody({ activity, bodyRef }: Pick<CardProps, 'activity' | 'bodyRef'>) {
  const mentionNames = useMentionNames();
  return (
    <div ref={bodyRef} className={timeline.comment} data-comment-card="">
      <Markdown text={activity.comment ?? ''} mentionNames={mentionNames} />
    </div>
  );
}

/** 儲存時發現留言已經被刪除：編輯器收起來之後焦點放到這則提示（不然掉到頁面本體）；重抓成佔位之後焦點再接到佔位。 */
function VanishedNotice({ text }: { readonly text: string }) {
  const ref = useRef<HTMLParagraphElement>(null);
  useEffect(() => {
    ref.current?.focus();
  }, []);
  return (
    <p ref={ref} role="alert" tabIndex={-1} className={styles.restoreError}>
      {text}
    </p>
  );
}

/**
 * 留言的內文：一般的卡片、編輯中的編輯器、或刪除之後的佔位。
 * 編輯的狀態在 control（以活動編號記），活動重抓換了物件也不會關掉編輯器。
 */
export function CommentBody({ itemKey, activity, control, bodyRef }: CardProps) {
  if (activity.deletedAt !== null) {
    return <DeletedBody itemKey={itemKey} activity={activity} control={control} bodyRef={bodyRef} />;
  }
  return (
    <>
      {control.mode === 'view' ? (
        <ShownBody activity={activity} bodyRef={bodyRef} />
      ) : (
        <CommentEditForm
          itemKey={itemKey}
          activity={activity}
          isRestored={control.mode === 'restored'}
          onFinish={control.finishEditing}
        />
      )}
      {control.vanishedNotice !== null && (
        <VanishedNotice text={control.vanishedNotice} />
      )}
    </>
  );
}
