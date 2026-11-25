import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type RefObject, type SubmitEvent } from 'react';
import { Link } from 'react-router';
import { changeOwnPassword } from '../../api/account';
import { isApiError, tooManyAttemptsMessage } from '../../api/http';
import { queryKeys } from '../../api/queryKeys';
import type { PasswordChanged } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { CheckboxField } from '../../components/CheckboxField/CheckboxField';
import { PasswordField } from '../../components/Field/PasswordField';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Notice } from '../../components/Notice/Notice';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { COMMAND_NAME } from '../../config/product';
import { firstInvalidField, hasErrors, mapApiErrorToFields, type FieldErrors, type FieldMapping } from '../../lib/formErrors';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH, passwordLengthError } from '../people/personForm';
import styles from './AccountPage.module.css';

interface PasswordDraft {
  readonly currentPassword: string;
  readonly newPassword: string;
}

type PasswordFieldName = keyof PasswordDraft;

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
// 改密碼依人限流（連續輸錯目前的密碼也算）：說「嘗試太多次」，不用共用的「請求太頻繁」。
const EMPTY: PasswordDraft = { currentPassword: '', newPassword: '' };

function validate(draft: PasswordDraft): FieldErrors<PasswordFieldName> {
  return {
    currentPassword: draft.currentPassword === '' ? '請輸入目前的密碼。' : undefined,
    newPassword: passwordLengthError(draft.newPassword, '新密碼'),
  };
}

/** 成功的說明：沒有別的登入、沒有撤銷的權杖時只說「密碼已更新。」；撤銷了權杖就提醒要重新登入。 */
function successMessage({ signedOutSessions, revokedAccessTokens }: PasswordChanged): string {
  const sessions = signedOutSessions === 0 ? '' : `其他裝置上的登入都登出了（${String(signedOutSessions)} 個）。`;
  const tokens =
    revokedAccessTokens === 0
      ? ''
      : `存取權杖撤銷了 ${String(revokedAccessTokens)} 把，用到它們的工具要重新登入：請再執行一次 ${COMMAND_NAME} setup。`;
  return ['密碼已更新。', sessions, tokens].join('');
}

/** 改密碼會撤銷存取權杖與 AI 用戶端的授權（本機代理的權杖也是存取權杖）：清單不重抓，使用者會看到已撤銷的還顯示有效。 */
function useRefreshRevokedCredentials() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.accessTokens });
    void queryClient.invalidateQueries({ queryKey: queryKeys.oauthGrants });
    void queryClient.invalidateQueries({ queryKey: queryKeys.localAgents });
  };
}

type FieldRefs = Readonly<Record<PasswordFieldName, RefObject<HTMLInputElement | null>>>;

/**
 * 送出、錯誤的對應與成功之後清空。欄位不重新掛上（在欄位裡按 Enter 送出時焦點還在欄位裡，重新掛上焦點就掉了），
 * 顯示密碼的切換照使用者按的留著。
 */
function usePasswordForm(refs: FieldRefs) {
  const [draft, setDraft] = useState<PasswordDraft>(EMPTY);
  // 改密碼通常是因為不確定憑證還安不安全，所以預設連存取權杖一起換掉。成功之後照使用者勾的留著，不重設。
  const [revokeAccessTokens, setRevokeAccessTokens] = useState(true);
  const [errors, setErrors] = useState<FieldErrors<PasswordFieldName>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const mutation = useMutation({ mutationFn: changeOwnPassword, onSuccess: useRefreshRevokedCredentials() });

  function focusFirst(found: FieldErrors<PasswordFieldName>) {
    const field = firstInvalidField(FIELDS, found);
    if (field !== undefined) refs[field].current?.focus();
  }

  function fail(error: unknown) {
    const mapped = mapApiErrorToFields(error, MAPPING);
    const isRateLimited = isApiError(error) && error.status === HTTP_TOO_MANY_REQUESTS;
    setErrors(mapped.fieldErrors);
    setFormError(isRateLimited ? tooManyAttemptsMessage(error) : mapped.formError);
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
    mutation.mutate({ ...draft, revokeAccessTokens }, {
      onSuccess: (result) => {
        setDraft(EMPTY);
        setSuccess(successMessage(result));
      },
      onError: fail,
    });
  }

  function update(field: PasswordFieldName, value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
    setErrors((current) => ({ ...current, [field]: undefined }));
  }

  return { draft, errors, formError, success, update, handleSubmit, revokeAccessTokens, setRevokeAccessTokens, isPending: mutation.isPending };
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
          改密碼會登出其他裝置，存取權杖預設一併撤銷。沒有勾選的話，到<Link to={INTEGRATIONS_PATH}>「命令列與整合」</Link>自己管理。
        </p>
      }
    >
      {/* 表單不另外取名：外面的區塊已經是有名稱的 region「密碼」，再取一樣的名字報讀軟體會列兩個同名的地標。 */}
      <form className={styles.form} noValidate onSubmit={form.handleSubmit}>
        <div className={styles.fields}>
          <PasswordField ref={currentRef} label="目前的密碼" value={form.draft.currentPassword} maxLength={PASSWORD_MAX_LENGTH} autoComplete="current-password" error={form.errors.currentPassword} onChange={(event) => { form.update('currentPassword', event.target.value); }} />
          <PasswordField ref={nextRef} label="新密碼" value={form.draft.newPassword} maxLength={PASSWORD_MAX_LENGTH} autoComplete="new-password" hint={`至少 ${String(PASSWORD_MIN_LENGTH)} 個字元`} error={form.errors.newPassword} onChange={(event) => { form.update('newPassword', event.target.value); }} />
        </div>
        <CheckboxField
          label="同時撤銷所有存取權杖（命令列、CI、本機代理）"
          hint={`那些工具之後要重新登入（${COMMAND_NAME} setup）或重建權杖。`}
          checked={form.revokeAccessTokens}
          onChange={(event) => { form.setRevokeAccessTokens(event.target.checked); }}
        />
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
