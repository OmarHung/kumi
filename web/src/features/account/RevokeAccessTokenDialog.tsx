import { errorMessage } from '../../api/http';
import type { AccessToken } from '../../api/types';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { prefixText } from './accessTokenText';
import { useRevokeAccessToken } from './accessTokenMutations';

interface RevokeAccessTokenDialogProps {
  readonly token: AccessToken;
  readonly onClose: () => void;
  /** 撤銷好了：呼叫端關上對話框、把焦點放到還在的地方（那一列已經不在了）。 */
  /** wasAlreadyGone：伺服器說已經不在了（別處先撤了），也當成功。 */
  readonly onRevoked: (wasAlreadyGone: boolean) => void;
}

/**
 * 「撤銷」的確認：寫出後果（用它的 CLI 與腳本立刻失效、不能復原）。失敗時對話框留著（404 不算失敗：已經撤銷了），伺服器的說明寫在按鈕上方。
 * 送出中 Esc 與「取消」都不作用；確認鈕只標 aria-disabled，焦點留在它身上（keepsFocusWhilePending）。
 */
export function RevokeAccessTokenDialog({ token, onClose, onRevoked }: RevokeAccessTokenDialogProps) {
  const revoke = useRevokeAccessToken();
  return (
    <ConfirmDialog
      title={`撤銷「${token.name}」？`}
      description={`開頭 ${prefixText(token)}。撤銷之後，用這把權杖的 CLI 與腳本會立刻失效，無法復原。需要的話再建立一把新的。`}
      confirmLabel="撤銷權杖"
      pendingLabel="撤銷中…"
      tone="danger"
      isPending={revoke.isPending}
      keepsFocusWhilePending
      error={revoke.isError ? errorMessage(revoke.error) : null}
      onConfirm={() => {
        revoke.mutate(token.tokenId, {
          onSuccess: (result) => {
            onRevoked(result.wasAlreadyGone);
          },
        });
      }}
      onCancel={onClose}
    />
  );
}
