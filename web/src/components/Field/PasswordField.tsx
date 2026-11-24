import { useState, type ComponentPropsWithRef, type ReactNode } from 'react';
import { cx } from '../../lib/cx';
import { preventComposingSubmit } from '../../lib/keyboard';
import { Icon } from '../Icon/Icon';
import { FieldShell, fieldControlClassName } from './Field';
import styles from './PasswordField.module.css';

type PasswordFieldProps = Omit<ComponentPropsWithRef<'input'>, 'id' | 'type' | 'size' | 'className'> & {
  readonly label: string;
  readonly hint?: ReactNode;
  readonly error?: string;
};

/**
 * 密碼欄位：長相、標籤、提示與錯誤和 TextField 同一套（FieldShell），右邊多一顆「顯示…」的切換按鈕
 * （docs/DESIGN.md 第 7 節）。按鈕的名稱帶上欄位（「顯示新密碼」）：同一頁有兩個密碼欄位時才分得出來；
 * 名稱不變，看不看得到由 aria-pressed 說，圖示跟著換。
 * 顯示出來時是一般的文字框：關掉拼字檢查、自動大寫與自動修正，密碼才不會被送到拼字的服務或被改掉。
 */
export function PasswordField({ label, hint, error, disabled, onKeyDown, ...inputProps }: PasswordFieldProps) {
  const [isShown, setIsShown] = useState(false);
  const toggleLabel = `顯示${label}`;
  return (
    <FieldShell label={label} hint={hint} error={error} describedBy={inputProps['aria-describedby']}>
      {({ id, describedBy }) => (
        <span className={styles.frame}>
          <input
            {...inputProps}
            id={id}
            type={isShown ? 'text' : 'password'}
            disabled={disabled}
            spellCheck={false}
            autoCapitalize="none"
            autoCorrect="off"
            className={cx(fieldControlClassName('input', error !== undefined), styles.input)}
            aria-invalid={error === undefined ? inputProps['aria-invalid'] : true}
            aria-describedby={describedBy}
            onKeyDown={(event) => {
              preventComposingSubmit(event);
              onKeyDown?.(event);
            }}
          />
          <button
            type="button"
            className={styles.toggle}
            aria-label={toggleLabel}
            title={toggleLabel}
            aria-pressed={isShown}
            disabled={disabled}
            onClick={() => {
              setIsShown((shown) => !shown);
            }}
          >
            <Icon name={isShown ? 'hidePassword' : 'showPassword'} />
          </button>
        </span>
      )}
    </FieldShell>
  );
}
