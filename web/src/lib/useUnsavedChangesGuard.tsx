import { useRef, type ReactNode } from 'react';
import { useBlocker } from 'react-router';
import { ConfirmDialog } from '../components/Dialog/ConfirmDialog';
import { useBeforeUnloadWarning } from './useBeforeUnloadWarning';

/** 離開前確認的對話框用字；預設是「沒儲存的修改」。不是修改而是「做到一半的事」（AI 助理的回覆）時換掉。 */
export interface GuardLabels {
  readonly title?: string;
  readonly confirmLabel?: string;
  readonly cancelLabel?: string;
  /** 確認鈕的樣子；預設 danger（捨棄修改）。停下一件還能再做的事（停止回覆）不是不可回復，用 primary。 */
  readonly tone?: 'primary' | 'danger';
}

/**
 * 有改過卻要離開時先問：站內的連結與上一頁用 React Router 的 blocker 擋下來問一次；
 * 重新整理或關掉分頁用瀏覽器自己的提醒。儲存成功後要離開的那一次不問：呼叫 allowLeave() 之後再換頁。
 * description 是對話框裡說明修改會怎樣的那一句（各頁自己寫，例如「離開這一頁的話，名稱與內容的修改會不見。」）。回傳要畫出來的對話框。
 */
export function useUnsavedChangesGuard(
  hasChanges: boolean,
  description: string,
  labels: GuardLabels = {},
): { readonly dialog: ReactNode; readonly allowLeave: () => void } {
  const isAllowedRef = useRef(false);
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) => hasChanges && !isAllowedRef.current && currentLocation.pathname !== nextLocation.pathname,
  );
  useBeforeUnloadWarning(hasChanges);

  const dialog =
    blocker.state === 'blocked' ? (
      <ConfirmDialog
        title={labels.title ?? '還有沒儲存的修改'}
        description={description}
        confirmLabel={labels.confirmLabel ?? '捨棄修改並離開'}
        cancelLabel={labels.cancelLabel ?? '留下來'}
        tone={labels.tone ?? 'danger'}
        onConfirm={() => {
          blocker.proceed();
        }}
        onCancel={() => {
          blocker.reset();
        }}
      />
    ) : null;
  return {
    dialog,
    allowLeave: () => {
      isAllowedRef.current = true;
    },
  };
}
