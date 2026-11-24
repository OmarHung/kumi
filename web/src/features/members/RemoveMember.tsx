import { errorMessage } from '../../api/http';
import type { Member } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { Button } from '../../components/Button/Button';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { losesAccess, type useRemoveMember } from './memberRoles';

export const LAST_ADMIN_REASON = '最後一位管理員：要先把另一位成員設成管理員，才能換掉角色或移出專案。';

interface RemoveMemberButtonProps {
  readonly id: string;
  readonly member: Member;
  /** 最後一位管理員：停用但仍然停得到（aria-disabled），說明連到 reasonId。 */
  readonly reasonId: string | undefined;
  readonly onRemove: (member: Member) => void;
}

/**
 * 每一列的「移除」。data-grid-action（lib/rovingGrid.ts 的 GRID_ACTION_ATTRIBUTE）：表格裡它不是編輯用的按鈕，
 * F2（進入編輯）不按它；Enter、空白鍵照按鈕原本的行為。
 */
export function RemoveMemberButton({ id, member, reasonId, onRemove }: RemoveMemberButtonProps) {
  const isLocked = reasonId !== undefined;
  return (
    <Button
      id={id}
      size="sm"
      variant="ghost"
      aria-label={`移除${member.displayName}`}
      aria-disabled={isLocked || undefined}
      aria-describedby={reasonId}
      data-grid-action=""
      onClick={() => {
        if (!isLocked) onRemove(member);
      }}
    >
      移除
    </Button>
  );
}

interface RemoveMemberDialogProps {
  readonly member: Member;
  readonly mutation: ReturnType<typeof useRemoveMember>;
  readonly onConfirm: () => void;
  readonly onCancel: () => void;
}

/** 移除前確認：他負責的項目留著；移除的是自己、又不是組織管理員時，說明會看不到這個專案。 */
export function RemoveMemberDialog({ member, mutation, onConfirm, onCancel }: RemoveMemberDialogProps) {
  const user = useCurrentUser();
  const isSelf = member.personId === user.personId;
  const kept = `「${member.displayName}」負責的項目與成員欄位的值會留著，之後不能再把他指派進去。`;
  const leaving = losesAccess(member, user) ? '你會看不到這個專案，畫面會回到專案清單。' : '';
  return (
    <ConfirmDialog
      title={isSelf ? '把自己移出專案？' : `把「${member.displayName}」移出專案？`}
      description={`${kept}${leaving}`}
      confirmLabel="移除"
      pendingLabel="移除中…"
      tone="danger"
      isPending={mutation.isPending}
      error={mutation.isError ? errorMessage(mutation.error) : null}
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}
