import { errorMessage } from '../../api/http';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';

interface DeletePlanningDialogProps {
  readonly name: string;
  /** 「週期」或「里程碑」：說明後果時用。 */
  readonly kind: '週期' | '里程碑';
  /** 這個週期（里程碑）現在有幾個項目（進度的 total）：刪除前讓人知道影響多大。 */
  readonly itemCount: number;
  readonly isPending: boolean;
  readonly error: unknown;
  readonly onConfirm: () => void;
  readonly onClose: () => void;
}

/** 刪除前確認：說明項目不會被刪除，只是變成沒有週期（里程碑）；失敗的原因留在對話框裡。 */
export function DeletePlanningDialog({ name, kind, itemCount, isPending, error, onConfirm, onClose }: DeletePlanningDialogProps) {
  return (
    <ConfirmDialog
      title={`刪除「${name}」？`}
      description={`這個${kind}有 ${String(itemCount)} 個項目。項目不會被刪除，只會變成沒有${kind}。`}
      confirmLabel="刪除"
      pendingLabel="刪除中…"
      tone="danger"
      isPending={isPending}
      keepsFocusWhilePending
      error={error === null || error === undefined ? null : errorMessage(error)}
      onConfirm={onConfirm}
      onCancel={onClose}
    />
  );
}
