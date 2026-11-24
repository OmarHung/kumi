import { useMutation } from '@tanstack/react-query';
import { deleteAttachment, type Attachment } from '../../api/attachments';
import { errorMessage, isApiError } from '../../api/http';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';

const HTTP_NOT_FOUND = 404;

interface DeleteAttachmentDialogProps {
  readonly attachment: Attachment;
  readonly onClose: () => void;
  /** 刪掉了（別處先刪了也算）：呼叫端關上對話框、把焦點放到還在的地方。 */
  readonly onDeleted: (attachment: Attachment) => void;
}

/**
 * 刪除附件的確認（不能復原，8.3「危險的操作」）：失敗時對話框留著、伺服器的說明寫在按鈕上方；
 * 找不到（404）表示別處先刪了，當成成功。送出中 Esc 與「取消」都不作用，確認鈕只標 aria-disabled，焦點留在它身上。
 */
export function DeleteAttachmentDialog({ attachment, onClose, onDeleted }: DeleteAttachmentDialogProps) {
  const remove = useMutation({
    mutationFn: async () => {
      try {
        return await deleteAttachment(attachment.id);
      } catch (error) {
        if (isApiError(error) && error.status === HTTP_NOT_FOUND) return attachment;
        throw error;
      }
    },
  });
  return (
    <ConfirmDialog
      title={`刪除「${attachment.fileName}」？`}
      description="刪除之後無法復原，也沒辦法再下載。"
      confirmLabel="刪除"
      pendingLabel="刪除中…"
      tone="danger"
      isPending={remove.isPending}
      keepsFocusWhilePending
      error={remove.isError ? errorMessage(remove.error) : null}
      onConfirm={() => {
        remove.mutate(undefined, {
          onSuccess: () => {
            onDeleted(attachment);
          },
        });
      }}
      onCancel={onClose}
    />
  );
}
