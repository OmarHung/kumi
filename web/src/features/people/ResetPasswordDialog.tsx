import { useRef, useState, type SubmitEvent } from 'react';
import type { Person } from '../../api/types';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { PasswordField } from '../../components/Field/PasswordField';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { mapApiErrorToFields, type FieldMapping } from '../../lib/formErrors';
import { useResetPersonPassword } from './personMutations';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH, passwordLengthError } from './personForm';
import styles from './PeoplePage.module.css';

const MAPPING: FieldMapping<'newPassword'> = {
  fields: ['newPassword'],
  codes: { 'person.password_too_short': 'newPassword', 'person.password_too_long': 'newPassword' },
};

const HINT = `至少 ${String(PASSWORD_MIN_LENGTH)} 個字元。重設之後，他在每個裝置上的登入都會登出，請親自把新密碼交給他。`;

interface ResetPasswordDialogProps {
  readonly person: Person;
  readonly onClose: () => void;
  /** 重設好了：呼叫端關上對話框、在頁面上方說明結果。 */
  readonly onReset: (person: Person) => void;
}

/** 送出前先檢查長度；伺服器的長度錯誤（細項路徑 newPassword）也標在欄位上，其他的（403、404、409）寫在表單上方。 */
function useResetForm(person: Person, onReset: (person: Person) => void) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [password, setPassword] = useState('');
  const [fieldError, setFieldError] = useState<string | undefined>();
  const [formError, setFormError] = useState<string | null>(null);
  const reset = useResetPersonPassword();

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    // 送出中按鈕只標 aria-disabled（焦點留在上面），再按一次照樣觸發 submit：這裡擋下重送。
    if (reset.isPending) return;
    const problem = passwordLengthError(password, '新密碼');
    setFormError(null);
    setFieldError(problem);
    if (problem !== undefined) {
      inputRef.current?.focus();
      return;
    }
    reset.mutate(
      { personId: person.personId, newPassword: password },
      {
        onSuccess: onReset,
        onError: (error) => {
          const mapped = mapApiErrorToFields(error, MAPPING);
          setFieldError(mapped.fieldErrors.newPassword);
          setFormError(mapped.formError);
          if (mapped.fieldErrors.newPassword !== undefined) inputRef.current?.focus();
        },
      },
    );
  }

  function change(value: string) {
    setPassword(value);
    setFieldError(undefined);
  }

  return { inputRef, password, change, fieldError, formError, handleSubmit, isPending: reset.isPending };
}

/**
 * 「重設密碼…」：替別人設一組新密碼（忘記密碼的人沒有寄信的服務可以救）。欄位可以切換顯示密碼（PasswordField），
 * 管理員打完可以確認一次再親自交給他。自己的密碼不在這裡改（那一列沒有這個動作），要到「我的帳號」。
 */
export function ResetPasswordDialog({ person, onClose, onReset }: ResetPasswordDialogProps) {
  const { inputRef, password, change, fieldError, formError, handleSubmit, isPending } = useResetForm(person, onReset);
  // 送出中不讓 Esc 或「取消」關掉，結果才不會沒人看到。
  const close = isPending ? () => undefined : onClose;
  return (
    <Dialog title={`重設「${person.displayName}」的密碼`} onClose={close}>
      <form className={styles.form} noValidate onSubmit={handleSubmit}>
        <FormAlert message={formError} />
        <PasswordField
          ref={inputRef}
          label="新密碼"
          value={password}
          maxLength={PASSWORD_MAX_LENGTH}
          autoComplete="new-password"
          hint={HINT}
          error={fieldError}
          onChange={(event) => {
            change(event.target.value);
          }}
        />
        <DialogFormActions submitLabel="重設密碼" pendingLabel="重設中…" isPending={isPending} keepsFocusWhilePending onCancel={close} />
      </form>
    </Dialog>
  );
}
