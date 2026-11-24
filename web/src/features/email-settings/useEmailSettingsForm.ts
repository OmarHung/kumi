import { useRef, useState, type RefObject } from 'react';
import { useSaveEmailSettings, useSendTestEmail, type EmailProvider, type EmailSettings, type SmtpSecurity } from '../../api/emailSettings';
import { isApiError } from '../../api/http';
import { hasErrors, mapApiErrorToFields, type FieldErrors, type FieldMapping } from '../../lib/formErrors';
import { useUnsavedChangesGuard } from '../../lib/useUnsavedChangesGuard';
import {
  hasDraftChanges,
  toDraft,
  toInput,
  visibleFields,
  withSecurity,
  type DraftContext,
  type EmailDraft,
  type EmailField,
} from './emailSettingsDraft';

const HTTP_TOO_MANY_REQUESTS = 429;
const SAVE_RATE_LIMITED = '請求太頻繁，請稍後再試。';
const TEST_RATE_LIMITED = '測試信寄得太頻繁，請稍後再試。';

function mappingFor(provider: EmailDraft['provider']): FieldMapping<EmailField> {
  return {
    fields: visibleFields(provider),
    codes: { 'email.port_not_allowed': 'port', 'email.insecure_credentials': 'security' },
  };
}

export interface EmailSettingsFormState {
  readonly draft: EmailDraft;
  readonly errors: FieldErrors<EmailField>;
  /** 對不到欄位的錯誤（或寄測試信失敗的原因），顯示在按鈕附近。 */
  readonly formError: string | null;
  /** 儲存成功、測試信寄出的說明。 */
  readonly message: string | null;
  readonly isBusy: boolean;
  readonly isSaving: boolean;
  readonly isSendingTest: boolean;
  /** 有問題的欄位加了 aria-invalid 之後，焦點要去的外框；focusCount 每次加一，同一個欄位連續兩次有問題也會再移一次。 */
  readonly focusCount: number;
  readonly formRef: RefObject<HTMLFormElement | null>;
  readonly guardDialog: ReturnType<typeof useUnsavedChangesGuard>['dialog'];
  readonly change: (patch: Partial<EmailDraft>, field?: EmailField) => void;
  readonly changeProvider: (provider: EmailProvider) => void;
  readonly changeSecurity: (security: SmtpSecurity) => void;
  readonly save: () => void;
  readonly sendTest: () => void;
}

/** 草稿、欄位的錯誤、說明與焦點要去的欄位（畫面上看得到的狀態）；寄出與回應的處理在 useEmailSettingsForm。 */
function useEmailDraftState(settings: EmailSettings, context: DraftContext) {
  const [baseline, setBaseline] = useState(() => toDraft(settings, context));
  const [draft, setDraft] = useState(baseline);
  const [errors, setErrors] = useState<FieldErrors<EmailField>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [focusCount, setFocusCount] = useState(0);
  return {
    draft,
    errors,
    formError,
    message,
    focusCount,
    hasChanges: hasDraftChanges(draft, baseline),
    /** 儲存成功：以回來的設定為新的起點（密碼欄清空）。 */
    reset: (saved: EmailSettings) => {
      const next = toDraft(saved, context);
      setBaseline(next);
      setDraft(next);
    },
    show: (nextErrors: FieldErrors<EmailField>, nextFormError: string | null, nextMessage: string | null) => {
      setErrors(nextErrors);
      setFormError(nextFormError);
      setMessage(nextMessage);
      if (hasErrors(nextErrors)) setFocusCount((count) => count + 1);
    },
    change: (patch: Partial<EmailDraft>, field?: EmailField) => {
      setDraft((current) => ({ ...current, ...patch }));
      // 改了任何欄位，上一次「已儲存」「已經寄到…」的結果就不是現在的畫面了。
      setMessage(null);
      if (field !== undefined) setErrors((current) => ({ ...current, [field]: undefined }));
    },
    /** 換寄信服務：另一邊的欄位錯誤、表單上方的錯誤與結果訊息都不再適用。 */
    changeProvider: (provider: EmailProvider) => {
      setDraft((current) => ({ ...current, provider }));
      setErrors({});
      setFormError(null);
      setMessage(null);
    },
    changeSecurity: (security: SmtpSecurity) => {
      setDraft((current) => withSecurity(current, security));
      setMessage(null);
      setErrors((current) => ({ ...current, security: undefined, port: undefined }));
    },
  };
}

/**
 * 寄信設定表單的狀態：草稿（起點是存著的設定加預先填的值）、儲存與寄測試信（用畫面上還沒存的設定）、
 * 422 的細項照路徑放到欄位下面、有沒存的修改離開時問一次。
 */
export function useEmailSettingsForm(settings: EmailSettings, context: DraftContext): EmailSettingsFormState {
  const state = useEmailDraftState(settings, context);
  const formRef = useRef<HTMLFormElement>(null);
  const saveMutation = useSaveEmailSettings();
  const testMutation = useSendTestEmail();
  const isBusy = saveMutation.isPending || testMutation.isPending;
  const guard = useUnsavedChangesGuard(state.hasChanges, '離開這一頁的話，寄信設定的修改會不見。');

  function fail(error: unknown, rateLimitedMessage: string) {
    const mapped = mapApiErrorToFields(error, mappingFor(state.draft.provider));
    const isRateLimited = isApiError(error) && error.status === HTTP_TOO_MANY_REQUESTS;
    state.show(mapped.fieldErrors, isRateLimited ? rateLimitedMessage : mapped.formError, null);
  }

  return {
    ...state,
    isBusy,
    isSaving: saveMutation.isPending,
    isSendingTest: testMutation.isPending,
    formRef,
    guardDialog: guard.dialog,
    save: () => {
      if (isBusy) return;
      state.show({}, null, null);
      saveMutation.mutate(toInput(state.draft), {
        onSuccess: (saved) => {
          state.reset(saved);
          state.show({}, null, '已儲存。');
        },
        onError: (error) => {
          fail(error, SAVE_RATE_LIMITED);
        },
      });
    },
    sendTest: () => {
      if (isBusy) return;
      state.show({}, null, null);
      testMutation.mutate(toInput(state.draft), {
        onSuccess: ({ sentTo }) => {
          state.show({}, null, `已經寄到 ${sentTo}，請到信箱確認。`);
        },
        onError: (error) => {
          fail(error, TEST_RATE_LIMITED);
        },
      });
    },
  };
}
