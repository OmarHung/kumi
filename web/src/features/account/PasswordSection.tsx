import { useMutation } from '@tanstack/react-query';
import { useRef, useState, type RefObject, type SubmitEvent } from 'react';
import { Link } from 'react-router';
import { changeOwnPassword } from '../../api/account';
import { isApiError } from '../../api/http';
import type { ChangePasswordRequest } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { PasswordField } from '../../components/Field/PasswordField';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Notice } from '../../components/Notice/Notice';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { firstInvalidField, hasErrors, mapApiErrorToFields, type FieldErrors, type FieldMapping } from '../../lib/formErrors';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH, passwordLengthError } from '../people/personForm';
import styles from './AccountPage.module.css';

type PasswordFieldName = keyof ChangePasswordRequest;

const FIELDS: readonly PasswordFieldName[] = ['currentPassword', 'newPassword'];

const MAPPING: FieldMapping<PasswordFieldName> = {
  fields: FIELDS,
  codes: {
    'account.password_incorrect': 'currentPassword',
    'person.password_too_short': 'newPassword',
    'person.password_too_long': 'newPassword',
  },
};

// 存取權杖在設定中心的「命令列與整合」那一頁。
const INTEGRATIONS_PATH = '/account/integrations';
const HTTP_TOO_MANY_REQUESTS = 429;
// 和登入共用依來源 IP 的額度（猜目前的密碼的人也被擋在這裡）：說「嘗試太多次」，不用共用的「請求太頻繁」。
const RATE_LIMITED = '嘗試太多次，請稍後再試。';
const EMPTY: ChangePasswordRequest = { currentPassword: '', newPassword: '' };

function validate(draft: ChangePasswordRequest): FieldErrors<PasswordFieldName> {
  return {
    currentPassword: draft.currentPassword === '' ? '請輸入目前的密碼。' : undefined,
    newPassword: passwordLengthError(draft.newPassword, '新密碼'),
  };
}

/** 成功的說明：沒有別的登入時不寫括號那一句。 */
function successMessage(signedOut: number): string {
  return signedOut === 0 ? '密碼已更新。' : `密碼已更新。其他裝置上的登入都登出了（${String(signedOut)} 個）。`;
}

type FieldRefs = Readonly<Record<PasswordFieldName, RefObject<HTMLInputElement | null>>>;

/**
 * 送出、錯誤的對應與成功之後清空。欄位不重新掛上（在欄位裡按 Enter 送出時焦點還在欄位裡，重新掛上焦點就掉了），
 * 顯示密碼的切換照使用者按的留著。
 */
function usePasswordForm(refs: FieldRefs) {
  const [draft, setDraft] = useState<ChangePasswordRequest>(EMPTY);
  const [errors, setErrors] = useState<FieldErrors<PasswordFieldName>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const mutation = useMutation({ mutationFn: changeOwnPassword });

  function focusFirst(found: FieldErrors<PasswordFieldName>) {
    const field = firstInvalidField(FIELDS, found);
    if (field !== undefined) refs[field].current?.focus();
  }

  function fail(error: unknown) {
    const mapped = mapApiErrorToFields(error, MAPPING);
    const isRateLimited = isApiError(error) && error.status === HTTP_TOO_MANY_REQUESTS;
    setErrors(mapped.fieldErrors);
    setFormError(isRateLimited ? RATE_LIMITED : mapped.formError);
    focusFirst(mapped.fieldErrors);
  }

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    // 送出中按鈕只標 aria-disabled（焦點留在上面），再按一次照樣觸發 submit：這裡擋下重送。
    if (mutation.isPending) return;
    const found = validate(draft);
    setSuccess(null);
    setFormError(null);
    setErrors(found);
    if (hasErrors(found)) {
      focusFirst(found);
      return;
    }
    mutation.mutate(draft, {
      onSuccess: ({ signedOutSessions }) => {
        setDraft(EMPTY);
        setSuccess(successMessage(signedOutSessions));
      },
      onError: fail,
    });
  }

  function update(field: PasswordFieldName, value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
    setErrors((current) => ({ ...current, [field]: undefined }));
  }

  return { draft, errors, formError, success, update, handleSubmit, isPending: mutation.isPending };
}

/**
 * 改自己的密碼：先輸入目前的密碼。成功後清空欄位，並說明其他裝置上的登入都登出了（自己這一個留著）。
 * 錯誤依細項路徑標在欄位上；對不到欄位的（429、網路）寫在表單下面、按鈕旁邊，和成功的說明同一個位置。
 */
export function PasswordSection() {
  const currentRef = useRef<HTMLInputElement>(null);
  const nextRef = useRef<HTMLInputElement>(null);
  const form = usePasswordForm({ currentPassword: currentRef, newPassword: nextRef });
  return (
    <SettingsSection
      title="密碼"
      description={
        <p>
          改密碼不會撤銷存取權杖。懷疑權杖外流的話，到<Link to={INTEGRATIONS_PATH}>「命令列與整合」</Link>撤銷。
        </p>
      }
    >
      {/* 表單不另外取名：外面的區塊已經是有名稱的 region「密碼」，再取一樣的名字報讀軟體會列兩個同名的地標。 */}
      <form className={styles.form} noValidate onSubmit={form.handleSubmit}>
        <div className={styles.fields}>
          <PasswordField ref={currentRef} label="目前的密碼" value={form.draft.currentPassword} maxLength={PASSWORD_MAX_LENGTH} autoComplete="current-password" error={form.errors.currentPassword} onChange={(event) => { form.update('currentPassword', event.target.value); }} />
          <PasswordField ref={nextRef} label="新密碼" value={form.draft.newPassword} maxLength={PASSWORD_MAX_LENGTH} autoComplete="new-password" hint={`至少 ${String(PASSWORD_MIN_LENGTH)} 個字元`} error={form.errors.newPassword} onChange={(event) => { form.update('newPassword', event.target.value); }} />
        </div>
        <div className={styles.actions}>
          <Button type="submit" variant="primary" aria-disabled={form.isPending || undefined}>
            {form.isPending ? '更新中…' : '更新密碼'}
          </Button>
        </div>
        {/* 一直掛著（沒有訊息時看不見、不佔位置）：成功之後才填進來的說明，報讀軟體才唸得到。 */}
        <Notice tone="success" role="status">
          {form.success}
        </Notice>
        <FormAlert message={form.formError} />
      </form>
    </SettingsSection>
  );
}
