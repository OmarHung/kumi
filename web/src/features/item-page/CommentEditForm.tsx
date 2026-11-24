import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type SubmitEvent } from 'react';
import { editComment } from '../../api/activity';
import { isApiError } from '../../api/http';
import { queryKeys } from '../../api/queryKeys';
import type { Activity } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { draftKey, removeDraft } from '../../lib/draftStorage';
import { useDraft } from '../../lib/useDraft';
import { UploadAwareSubmit } from '../attachments/EditorUploadControls';
import { cellErrorMessage } from '../item-table/cells/EditableCell';
import { replaceActivity } from './activityCache';
import { useCommentAnnouncer } from './commentAnnouncer';
import { CommentEditorField, SUBMIT_SHORTCUT, useCommentEditorParts } from './CommentEditorField';
import { isEditDraftWorthKeeping } from './editDraft';
import { RestoredDraftNotice } from './RestoredDraftNotice';
import type { EditFinish } from './useCommentEntry';
import styles from './CommentEntry.module.css';

const EDIT_ROWS = 3;
const COMMENT_DELETED_CODE = 'comment.deleted';

interface CommentEditFormProps {
  readonly itemKey: string;
  readonly activity: Activity;
  /** 重新整理、換頁回來還原了草稿：不搶焦點，框上面說一聲。 */
  readonly isRestored: boolean;
  readonly onFinish: (reason: EditFinish) => void;
}

function editErrorMessage(error: unknown): string {
  return cellErrorMessage(error, ['body']);
}

/**
 * 儲存：沒改過時送出去的要是原文（編輯器的保證：打開不改就交出載入時的原文），伺服器才認得出「沒改」、不多存一個版本；
 * 改過才去掉前後的空白。草稿只在和原本不同時存。成功用回傳的活動換掉兩份快取裡的那一筆（自己做的事即時更新不會推回來）。
 * 儲存時留言已經被刪除了（409 comment.deleted）：關掉編輯器、重抓，畫面變成佔位；草稿留著。
 */
function useEditSave(itemKey: string, activity: Activity, original: string, onFinish: (reason: EditFinish) => void) {
  const queryClient = useQueryClient();
  const announce = useCommentAnnouncer();
  const key = draftKey('comment-edit', itemKey, activity.id);
  const mutation = useMutation({
    mutationFn: (body: string) => editComment(itemKey, activity.id, body),
    // 先換掉快取裡的那一筆再收起編輯器：反過來的話，收起的那一瞬間會看到舊的內容。
    onSuccess: async (saved) => {
      removeDraft(key);
      await replaceActivity(queryClient, itemKey, saved);
      onFinish('saved');
      announce('已儲存留言');
    },
    onError: (error) => {
      if (!isApiError(error) || error.code !== COMMENT_DELETED_CODE) return;
      // 草稿留著：沒儲存的字不丟，之後有人復原、再按「編輯」時還原。
      onFinish('deleted');
      void queryClient.invalidateQueries({ queryKey: queryKeys.activity(itemKey) });
    },
  });
  const stored = useDraft(key, { isWorthKeeping: (value) => isEditDraftWorthKeeping(original, value) });
  return { mutation, stored };
}

/** 草稿、儲存、取消、捨棄還原的草稿：編輯器之外的所有動作。 */
function useEditState(props: CommentEditFormProps) {
  const { itemKey, activity, onFinish } = props;
  const original = activity.comment ?? '';
  const { mutation, stored } = useEditSave(itemKey, activity, original, onFinish);
  const [draft, setDraft] = useState(stored.restored ?? original);
  const change = (next: string) => {
    setDraft(next);
    stored.save(next);
    if (mutation.isError) mutation.reset();
  };
  const editorParts = useCommentEditorParts({ itemKey, value: draft, onChange: change, isBusy: mutation.isPending });
  const { editorRef, uploads } = editorParts;
  const canSave = draft.trim() !== '' && !mutation.isPending;
  const save = () => {
    if (uploads.isUploading) uploads.announceUploading();
    else if (canSave) mutation.mutate(draft === original ? draft : draft.trim());
  };
  return {
    ...editorParts,
    draft,
    change,
    canSave,
    save,
    isPending: mutation.isPending,
    isRestored: stored.isRestored,
    error: mutation.isError ? editErrorMessage(mutation.error) : null,
    cancel: () => {
      if (mutation.isPending) return;
      stored.clear();
      onFinish('cancelled');
    },
    discardRestored: () => {
      editorRef.current?.focus();
      setDraft(original);
      stored.clear();
    },
    // 從「儲存」送出時先把焦點放回編輯器：儲存中按鈕停用，有焦點的按鈕一停用，焦點就掉到頁面本體（和留言框一樣）。
    handleSubmit: (event: SubmitEvent<HTMLFormElement>) => {
      event.preventDefault();
      editorRef.current?.focus();
      save();
    },
  };
}

/** 編輯既有的留言：就地換成編輯器（同一個留言編輯器，貼圖、提及、字數、快速鍵都一樣），內容是現在的 Markdown。 */
export function CommentEditForm(props: CommentEditFormProps) {
  const edit = useEditState(props);
  // 一串留言裡可能有好幾個編輯器（回覆），名稱帶作者才分得開。
  const label = `編輯 ${props.activity.actor.displayName} 的留言`;
  const { editorRef, candidates, uploads } = edit;
  return (
    <form className={styles.editForm} aria-label={label} aria-busy={edit.isPending} onSubmit={edit.handleSubmit}>
      {edit.isRestored && <RestoredDraftNotice target="沒儲存的編輯" onDiscard={edit.discardRestored} />}
      <CommentEditorField
        editorRef={editorRef}
        candidates={candidates}
        uploads={uploads}
        label={label}
        isLabelHidden
        value={edit.draft}
        onChange={edit.change}
        rows={EDIT_ROWS}
        autoFocus={props.isRestored ? false : 'end'}
        onCancel={edit.cancel}
        hint={`${SUBMIT_SHORTCUT} 儲存，Esc 取消`}
        onSubmit={edit.save}
        isBusy={edit.isPending}
        error={edit.error}
      />
      <p className={styles.editNote}>儲存後，舊的內容會留在版本紀錄裡，看得到這個項目的人都看得到。</p>
      <div className={styles.editActions}>
        <Button size="sm" disabled={edit.isPending} onClick={edit.cancel}>
          取消
        </Button>
        <UploadAwareSubmit isUploading={uploads.isUploading} disabled={!edit.canSave}>
          {edit.isPending ? '儲存中…' : '儲存'}
        </UploadAwareSubmit>
      </div>
    </form>
  );
}
