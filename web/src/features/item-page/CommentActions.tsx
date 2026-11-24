import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type RefObject } from 'react';
import { deleteComment } from '../../api/activity';
import { errorMessage } from '../../api/http';
import type { Activity } from '../../api/types';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { replaceActivity } from './activityCache';
import { useCommentAnnouncer } from './commentAnnouncer';
import styles from './CommentEntry.module.css';
import type { CommentEntryControl } from './useCommentEntry';

const BASE_TEXT = '刪除之後可以復原。這則留言的通知會一起刪掉，復原也不會回來；已經寄出的通知信收不回來。';
const ATTACHMENT_TEXT = '貼在留言裡的圖片與檔案是項目的附件，不會一起刪掉。';

/** 刪除確認框的內文：基本的兩句，視情況加上附件與「別人的留言」的提醒。 */
export function deleteConfirmText(activity: Activity, isAuthor: boolean): string {
  const hasAttachment = (activity.comment ?? '').includes('attachment:');
  const others = isAuthor ? '' : `這是 ${activity.actor.displayName} 的留言，刪除之後只有管理專案的人能復原。`;
  return [BASE_TEXT, hasAttachment ? ATTACHMENT_TEXT : '', others].filter((part) => part !== '').join('');
}

interface ActionsProps {
  readonly itemKey: string;
  readonly activity: Activity;
  readonly control: CommentEntryControl;
  readonly triggerRef: RefObject<HTMLButtonElement | null>;
}

function useDeleteComment(itemKey: string, activity: Activity, control: CommentEntryControl, onDone: () => void) {
  const queryClient = useQueryClient();
  const announce = useCommentAnnouncer();
  return useMutation({
    mutationFn: () => deleteComment(itemKey, activity.id),
    // 換成佔位之後焦點放到佔位上（選單與對話框都跟著不見）；先換快取，反過來會看到舊的內容。
    // 編輯中刪除：編輯狀態與草稿一起丟掉，不然復原之後編輯器會自己打開。
    onSuccess: async (deleted) => {
      control.requestBodyFocus();
      await replaceActivity(queryClient, itemKey, deleted);
      control.discardEdit();
      announce('已刪除留言');
      onDone();
    },
  });
}

function menuItems(control: CommentEntryControl, onEdit: () => void, onDelete: () => void): MenuItem[] {
  // 編輯中不再給「編輯」（已經在編輯了）；刪除照舊。
  const canEdit = control.abilities.canEdit && control.mode === 'view';
  const { canDelete } = control.abilities;
  const edit: MenuItem = { key: 'edit', label: '編輯', icon: 'edit', onSelect: onEdit };
  // 危險的刪除放最後、自成一組（docs/DESIGN.md 第 7 節的 Menu）。
  const remove: MenuItem = { key: 'delete', label: '刪除', icon: 'delete', tone: 'danger', startsGroup: true, onSelect: onDelete };
  return [...(canEdit ? [edit] : []), ...(canDelete ? [remove] : [])];
}

/**
 * 留言標頭右邊的「更多」：編輯（作者）與刪除（作者或管理專案的人）。一件都不能做時沒有這顆按鈕。
 * 選了之後選單先關上、焦點回到按鈕，才開對話框（和 ItemActions 一樣），對話框放在選單外面。
 */
export function CommentActions({ itemKey, activity, control, triggerRef }: ActionsProps) {
  const [isConfirming, setIsConfirming] = useState(false);
  const remove = useDeleteComment(itemKey, activity, control, () => {
    setIsConfirming(false);
  });
  const items = menuItems(control, control.startEditing, () => {
    remove.reset();
    setIsConfirming(true);
  });
  const hasItems = items.length > 0;
  // 確認框開著時留言被別人刪掉（沒有動作可做了）：框跟著收起來（焦點由 useCommentEntry 接到佔位）；
  // 狀態也要放掉，不然復原之後框會自己再開。
  if (!hasItems && isConfirming) setIsConfirming(false);
  if (!hasItems) return null;
  const label = `${activity.actor.displayName} 的留言的更多動作`;
  return (
    <>
      <span className={styles.menuSlot}>
        <Menu
          menuLabel={label}
          align="end"
          items={items}
          triggerRef={triggerRef}
          renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label={label} size="sm" />}
        />
      </span>
      {isConfirming && (
        <ConfirmDialog
          title="刪除這則留言？"
          description={deleteConfirmText(activity, control.abilities.isAuthor)}
          confirmLabel="刪除"
          pendingLabel="刪除中…"
          tone="danger"
          isPending={remove.isPending}
          keepsFocusWhilePending
          error={remove.isError ? errorMessage(remove.error) : null}
          onConfirm={() => {
            remove.mutate();
          }}
          onCancel={() => {
            setIsConfirming(false);
          }}
        />
      )}
    </>
  );
}
