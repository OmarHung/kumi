import { errorMessage } from '../../api/http';
import type { Person } from '../../api/types';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { useSetPersonDisabled } from './personMutations';

interface DisablePersonDialogProps {
  readonly person: Person;
  readonly onClose: () => void;
  /** 停用好了：呼叫端關上對話框、在頁面上方說明結果。 */
  readonly onDisabled: (person: Person) => void;
}

/**
 * 「停用帳號…」的確認：寫出後果（立刻被登出、不能再登入；項目與活動留著、可以重新啟用）。
 * 失敗時對話框留著，伺服器的說明寫在按鈕上方。送出中 Esc 與「取消」都不作用；確認鈕只標 aria-disabled，
 * 焦點留在它身上，失敗之後 Esc 與 Tab 照常在對話框裡（keepsFocusWhilePending）。
 */
export function DisablePersonDialog({ person, onClose, onDisabled }: DisablePersonDialogProps) {
  const disable = useSetPersonDisabled();
  return (
    <ConfirmDialog
      title={`停用「${person.displayName}」的帳號？`}
      description="停用之後，他會立刻被登出，也不能再登入。他負責的項目、留言與活動都會留著，之後可以重新啟用。"
      confirmLabel="停用帳號"
      pendingLabel="停用中…"
      tone="danger"
      isPending={disable.isPending}
      keepsFocusWhilePending
      error={disable.isError ? errorMessage(disable.error) : null}
      onConfirm={() => {
        disable.mutate({ personId: person.personId, disabled: true }, { onSuccess: onDisabled });
      }}
      onCancel={onClose}
    />
  );
}
