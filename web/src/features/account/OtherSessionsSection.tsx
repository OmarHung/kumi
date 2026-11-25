import { useMutation } from '@tanstack/react-query';
import { useId, useRef, useState, type RefObject, type SubmitEvent } from 'react';
import { signOutOtherSessions } from '../../api/account';
import { errorMessage, isApiError, tooManyAttemptsMessage } from '../../api/http';
import { Button } from '../../components/Button/Button';
import { PasswordField } from '../../components/Field/PasswordField';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Notice } from '../../components/Notice/Notice';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { mapApiErrorToFields } from '../../lib/formErrors';
import { PASSWORD_MAX_LENGTH } from '../people/personForm';
import styles from './AccountPage.module.css';

/**
 * 只動網頁的登入：存取權杖與已授權的 AI 代理不會被撤銷，說清楚，才不會以為登出之後權杖也失效了。
 * 要密碼：登出其他裝置會結束別處的登入，先確認是本人。
 */
const DESCRIPTION =
  '在別的電腦或瀏覽器登入過、忘了登出時用。登出其他裝置會結束你在別處的登入，所以要確認是你本人。只登出網頁的登入，不會撤銷存取權杖；權杖到「命令列與整合」撤銷。';

const HTTP_TOO_MANY_REQUESTS = 429;
const PASSWORD_INCORRECT = 'account.password_incorrect';

function resultMessage(signedOut: number): string {
  return signedOut === 0 ? '沒有其他登入中的裝置。' : `已登出其他 ${String(signedOut)} 個裝置。`;
}

/** 送出、錯誤的對應（密碼不對標在欄位上、其他寫在按鈕下面）與成功之後清空；沒填密碼、送出中時不送。 */
function useSignOutOthers(fieldRef: RefObject<HTMLInputElement | null>) {
  const [password, setPassword] = useState('');
  const [fieldError, setFieldError] = useState<string | undefined>(undefined);
  const mutation = useMutation({ mutationFn: signOutOtherSessions });

  function submit() {
    // 送出中、沒填密碼時按鈕只標 aria-disabled，再按（或在欄位裡按 Enter）照樣觸發 submit：這裡擋下。
    if (mutation.isPending || password === '') return;
    setFieldError(undefined);
    mutation.mutate(
      { currentPassword: password },
      {
        onSuccess: () => {
          setPassword('');
        },
        onError: (error) => {
          const mapped = mapApiErrorToFields(error, { fields: ['currentPassword'], codes: { [PASSWORD_INCORRECT]: 'currentPassword' } });
          setFieldError(mapped.fieldErrors.currentPassword);
          if (mapped.fieldErrors.currentPassword !== undefined) fieldRef.current?.focus();
        },
      },
    );
  }

  function change(value: string) {
    setPassword(value);
    setFieldError(undefined);
  }

  const error = mutation.isError && fieldError === undefined ? mutation.error : null;
  const alert = error === null ? null : isApiError(error) && error.status === HTTP_TOO_MANY_REQUESTS ? tooManyAttemptsMessage(error) : errorMessage(error);
  const result = mutation.isSuccess ? resultMessage(mutation.data.signedOutSessions) : null;
  return {
    password,
    fieldError,
    alert,
    result,
    isEmpty: password === '',
    isPending: mutation.isPending,
    tone: mutation.data?.signedOutSessions === 0 ? ('info' as const) : ('success' as const),
    submit,
    change,
  };
}

/**
 * 登出其他裝置：輸入目前的密碼之後，同一個帳號的其他登入全部作廢，這一個留著。
 * 可以回復（再登入一次就好），所以不用確認對話框。伺服器不記裝置，沒有「登入中的裝置」清單，只說登出了幾個。
 * 沒填密碼時按鈕只標 aria-disabled（焦點留得住）；密碼不對標在欄位上、打的字留著；成功後清空欄位。
 */
export function OtherSessionsSection() {
  const descriptionId = useId();
  const fieldRef = useRef<HTMLInputElement>(null);
  const form = useSignOutOthers(fieldRef);
  return (
    <SettingsSection title="其他裝置" description={DESCRIPTION} descriptionId={descriptionId}>
      <form
        className={styles.form}
        noValidate
        onSubmit={(event: SubmitEvent<HTMLFormElement>) => {
          event.preventDefault();
          form.submit();
        }}
      >
        <div className={styles.fields}>
          <PasswordField
            ref={fieldRef}
            label="目前的密碼"
            value={form.password}
            maxLength={PASSWORD_MAX_LENGTH}
            autoComplete="current-password"
            error={form.fieldError}
            onChange={(event) => {
              form.change(event.target.value);
            }}
          />
        </div>
        <div className={styles.actions}>
          {/* 送出中停用但停得到（aria-disabled）：用 disabled 的話，按下的按鈕一停用焦點就掉回頁面本體。 */}
          <Button type="submit" aria-describedby={descriptionId} aria-disabled={form.isPending || form.isEmpty || undefined}>
            {form.isPending ? '登出中…' : '登出其他裝置'}
          </Button>
        </div>
        <Notice tone={form.tone} role="status">
          {form.result}
        </Notice>
        <FormAlert message={form.alert} />
      </form>
    </SettingsSection>
  );
}
