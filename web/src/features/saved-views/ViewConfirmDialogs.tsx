import { errorMessage } from '../../api/http';
import type { SavedView } from '../../api/types';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import type { SavedViewMutations } from './useSavedViewMutations';

interface DeleteViewDialogProps {
  readonly view: SavedView;
  readonly mutations: SavedViewMutations;
  readonly onDelete: (view: SavedView) => void;
  readonly onClose: () => void;
}

/** 刪除前確認（送出中確認鈕只標 aria-disabled，焦點留在它身上，見 ConfirmDialog）：說明後果（只刪檢視，項目與目前的條件不受影響）；失敗的原因留在對話框裡。 */
export function DeleteViewDialog({ view, mutations, onDelete, onClose }: DeleteViewDialogProps) {
  return (
    <ConfirmDialog
      title={`刪除檢視「${view.name}」？`}
      description="只會刪掉這個檢視；項目與目前的條件都不受影響。刪除之後不能復原。"
      confirmLabel="刪除"
      pendingLabel="刪除中…"
      tone="danger"
      isPending={mutations.remove.isPending}
      keepsFocusWhilePending
      error={mutations.remove.isError ? errorMessage(mutations.remove.error) : null}
      onConfirm={() => {
        onDelete(view);
      }}
      onCancel={onClose}
    />
  );
}

interface UpdateFailedDialogProps {
  readonly view: SavedView;
  /** 失敗的原因：先存下來，再試時 mutation 的錯誤會被清掉。 */
  readonly message: string;
  readonly isPending: boolean;
  readonly onRetry: (view: SavedView) => void;
  readonly onClose: () => void;
}

/** 「更新成目前的條件」失敗之後：說明原因，可以再試一次。 */
export function UpdateFailedDialog({ view, message, isPending, onRetry, onClose }: UpdateFailedDialogProps) {
  return (
    <ConfirmDialog
      title={`無法更新檢視「${view.name}」`}
      description={message}
      confirmLabel="再試一次"
      pendingLabel="更新中…"
      isPending={isPending}
      keepsFocusWhilePending
      onConfirm={() => {
        onRetry(view);
      }}
      onCancel={onClose}
    />
  );
}
