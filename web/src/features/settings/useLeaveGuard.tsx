import type { ReactNode } from 'react';
import { useBlocker, useLocation, type Blocker } from 'react-router';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { useBeforeUnloadWarning } from '../../lib/useBeforeUnloadWarning';
import { isSameSettingsPage } from '../work-items/itemLinks';

export interface LeaveGuardOptions {
  /** 草稿有還沒送出的修改。 */
  readonly hasChanges: boolean;
  /** 有對話框開著：裡面打到一半的內容還沒進草稿。 */
  readonly hasOpenDialog: boolean;
  /** 確認要換到同一頁的別的區塊時（對話框會跟著關上），給換過去之後放焦點用。 */
  readonly onSwitchSection: (pathname: string) => void;
}

type BlockedBlocker = Extract<Blocker, { state: 'blocked' }>;

function SwitchSectionDialog({ blocker, onSwitchSection }: { readonly blocker: BlockedBlocker; readonly onSwitchSection: (pathname: string) => void }) {
  return (
    <ConfirmDialog
      title="對話框裡的內容還沒加入"
      description="換到別的區塊的話，對話框裡打到一半的內容會不見；已經加進來的修改還在。"
      confirmLabel="捨棄並換過去"
      cancelLabel="留下來"
      tone="danger"
      onConfirm={() => {
        onSwitchSection(blocker.location.pathname);
        blocker.proceed();
      }}
      onCancel={() => {
        blocker.reset();
      }}
    />
  );
}

function LeavePageDialog({ blocker }: { readonly blocker: BlockedBlocker }) {
  return (
    <ConfirmDialog
      title="還有沒送出的修改"
      description="離開這一頁的話，設定頁上還沒送出的修改會不見。"
      confirmLabel="捨棄修改並離開"
      cancelLabel="留下來"
      tone="danger"
      onConfirm={() => {
        blocker.proceed();
      }}
      onCancel={() => {
        blocker.reset();
      }}
    />
  );
}

/**
 * 離開設定頁、或對話框開著時換區塊，要先問：站內的連結與上一頁用 React Router 的 blocker 擋下來、問一次；
 * 重新整理或關掉分頁用瀏覽器自己的提醒（beforeunload，內容由瀏覽器決定）。回傳要畫出來的對話框。
 * 草稿在外層的版面路由，沒有對話框開著時切換區塊（包含轉到預設的區塊）不問；
 * 對話框開著時區塊一換，對話框跟著卸下、打到一半的內容就不見，所以連切換區塊也擋。
 */
export function useLeaveGuard({ hasChanges, hasOpenDialog, onSwitchSection }: LeaveGuardOptions): ReactNode {
  const location = useLocation();
  const blocker = useBlocker(({ currentLocation, nextLocation }) => {
    if (currentLocation.pathname === nextLocation.pathname) return false;
    return hasOpenDialog || (hasChanges && !isSameSettingsPage(currentLocation.pathname, nextLocation.pathname));
  });
  useBeforeUnloadWarning(hasChanges || hasOpenDialog);

  if (blocker.state !== 'blocked') {
    return null;
  }
  return isSameSettingsPage(location.pathname, blocker.location.pathname) ? (
    <SwitchSectionDialog blocker={blocker} onSwitchSection={onSwitchSection} />
  ) : (
    <LeavePageDialog blocker={blocker} />
  );
}
