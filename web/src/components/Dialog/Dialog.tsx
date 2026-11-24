import { useId, type ReactNode } from 'react';
import { Button } from '../Button/Button';
import styles from './Dialog.module.css';
import { ModalFrame } from './ModalFrame';

interface DialogProps {
  readonly title: string;
  readonly description?: ReactNode;
  /** 按 Esc 或呼叫端的「取消」時呼叫。送出中不想被關掉時，呼叫端自己忽略。 */
  readonly onClose: () => void;
  /** lg：裡面有表格（例如成員）的對話框，寬 --dialog-width-lg。 */
  readonly size?: 'md' | 'lg';
  readonly children: ReactNode;
}

/** 互動式的對話框。掛上就是打開、卸下就是關閉，由呼叫端用條件渲染控制。外殼（焦點鎖、背景）在 ModalFrame。 */
export function Dialog({ title, description, onClose, size = 'md', children }: DialogProps) {
  const titleId = useId();
  const descriptionId = useId();
  return (
    <ModalFrame
      labelledBy={titleId}
      describedBy={description ? descriptionId : undefined}
      size={size}
      onEscape={onClose}
    >
      <header className={styles.header}>
        <h2 id={titleId} className={styles.title}>
          {title}
        </h2>
        {description && (
          <p id={descriptionId} className={styles.description}>
            {description}
          </p>
        )}
      </header>
      {children}
    </ModalFrame>
  );
}

/** 對話框底部的按鈕列，主要動作放最右邊。 */
export function DialogActions({ children }: { readonly children: ReactNode }) {
  return <div className={styles.actions}>{children}</div>;
}

interface DialogFormActionsProps {
  readonly submitLabel: string;
  /** 送出中的按鈕文字，例如「建立中…」。 */
  readonly pendingLabel: string;
  readonly isPending: boolean;
  /** 資料還沒準備好（例如選項還在載入）時設成 false。 */
  readonly canSubmit?: boolean;
  /**
   * 送不出去、而且要說明原因時（例如會被伺服器擋下）：送出鈕停用但仍然停得到（aria-disabled），
   * aria-describedby 連到這些 id。按了也會觸發表單的 submit，呼叫端自己不送。
   */
  readonly blockedReasonIds?: readonly string[];
  /**
   * 送出中主要按鈕不停用、改標 aria-disabled（按了照樣觸發 submit，呼叫端自己不重送）：焦點常常就在這顆按鈕上
   * （按 Enter、點它），停用會讓焦點掉到頁面本體，報讀軟體也聽不到「送出中…」。送出要等一陣子、送完還留在對話框的用它。
   */
  readonly keepsFocusWhilePending?: boolean;
  readonly onCancel: () => void;
}

/** 表單對話框的「取消」與送出按鈕。送出中兩個都停用（或主要按鈕改標 aria-disabled），避免重複送出或送到一半被關掉。 */
export function DialogFormActions({
  submitLabel,
  pendingLabel,
  isPending,
  canSubmit = true,
  blockedReasonIds = [],
  keepsFocusWhilePending = false,
  onCancel,
}: DialogFormActionsProps) {
  const isBlocked = blockedReasonIds.length > 0;
  const isPendingDisabled = isPending && !keepsFocusWhilePending;
  return (
    <DialogActions>
      <Button onClick={onCancel} disabled={isPending}>
        取消
      </Button>
      <Button
        type="submit"
        variant="primary"
        disabled={isPendingDisabled || !canSubmit}
        aria-disabled={isBlocked || (isPending && keepsFocusWhilePending) || undefined}
        aria-describedby={isBlocked ? blockedReasonIds.join(' ') : undefined}
      >
        {isPending ? pendingLabel : submitLabel}
      </Button>
    </DialogActions>
  );
}
