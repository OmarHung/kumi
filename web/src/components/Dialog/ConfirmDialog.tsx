import { useId, type ReactNode } from 'react';
import { Button } from '../Button/Button';
import { FormAlert } from '../FormAlert/FormAlert';
import { Dialog, DialogActions } from './Dialog';
import styles from './Dialog.module.css';

interface ConfirmDialogProps {
  readonly title: string;
  readonly description?: ReactNode;
  readonly children?: ReactNode;
  readonly confirmLabel: string;
  /** 送出中的按鈕文字，例如「套用中…」；不會送請求的確認（例如捨棄草稿）不必給。 */
  readonly pendingLabel?: string;
  readonly isPending?: boolean;
  readonly cancelLabel?: string;
  /** 會刪掉或丟掉東西的動作用 danger。 */
  readonly tone?: 'primary' | 'danger';
  /** 送出失敗的訊息，顯示在按鈕上方。 */
  readonly error?: string | null;
  /**
   * 暫時還不能確認的原因（例如還在讀資料）：確認鈕停用但仍然停得到（aria-disabled），
   * 原因顯示在按鈕上方、用 aria-describedby 連過去；按了不會呼叫 onConfirm。
   */
  readonly blockedReason?: string | null;
  /**
   * 送出要等一陣子、失敗時對話框留著的（和 DialogFormActions 的同名屬性一樣）：送出中確認鈕不停用、改標 aria-disabled，
   * 焦點留在它身上（按下的按鈕一停用，焦點就掉到頁面本體，失敗之後 Esc 關不掉、Tab 跑到對話框後面）；
   * 送出中再按不會再呼叫 onConfirm。「取消」照樣停用。
   */
  readonly keepsFocusWhilePending?: boolean;
  readonly onConfirm: () => void;
  readonly onCancel: () => void;
}

/**
 * 「確定要這樣做嗎」的對話框：說明後果，取消在左、確認在右。送出中兩個按鈕都停用，Esc 也不會關掉，
 * 結果才不會沒人看到。焦點的處理沿用 Dialog（打開時到第一個按鈕，關上後回到打開它的元素）。
 */
export function ConfirmDialog({
  title,
  description,
  children,
  confirmLabel,
  pendingLabel = confirmLabel,
  isPending = false,
  cancelLabel = '取消',
  tone = 'primary',
  error = null,
  blockedReason = null,
  keepsFocusWhilePending = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const reasonId = useId();
  const close = isPending ? () => undefined : onCancel;
  const isBlocked = blockedReason !== null;
  const isAriaDisabled = isBlocked || (isPending && keepsFocusWhilePending);
  return (
    <Dialog title={title} description={description} onClose={close}>
      {children}
      <FormAlert message={error} />
      {isBlocked && (
        <p id={reasonId} className={styles.note}>
          {blockedReason}
        </p>
      )}
      <DialogActions>
        <Button onClick={close} disabled={isPending}>
          {cancelLabel}
        </Button>
        <Button
          variant={tone}
          onClick={() => {
            if (!isBlocked && !isPending) onConfirm();
          }}
          disabled={isPending && !keepsFocusWhilePending}
          aria-disabled={isAriaDisabled || undefined}
          aria-describedby={isBlocked ? reasonId : undefined}
        >
          {isPending ? pendingLabel : confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
