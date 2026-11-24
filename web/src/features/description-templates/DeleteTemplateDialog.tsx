import type { DescriptionTemplate } from '../../api/types';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';

interface DeleteTemplateDialogProps {
  readonly template: DescriptionTemplate;
  readonly isPending: boolean;
  readonly error: string | null;
  readonly onConfirm: () => void;
  readonly onCancel: () => void;
}

/** 刪掉說明格式：已經寫好的專案說明不受影響；刪掉的是預設時，說下一份預設是誰。 */
export function DeleteTemplateDialog({ template, isPending, error, onConfirm, onCancel }: DeleteTemplateDialogProps) {
  const description = template.isDefault
    ? '已經寫好的專案說明不受影響。這一份是預設，刪掉之後預設會換回內建的「網站案子」。'
    : '已經寫好的專案說明不受影響。';
  return (
    <ConfirmDialog
      title={`刪除「${template.name}」`}
      description={description}
      confirmLabel="刪除"
      pendingLabel="刪除中…"
      tone="danger"
      isPending={isPending}
      keepsFocusWhilePending
      error={error}
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}
