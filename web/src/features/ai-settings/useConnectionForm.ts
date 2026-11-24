import { useEffect, useRef, useState, type RefObject } from 'react';
import type { AiConnection, AiConnectionScope, AiProviderKind } from '../../api/aiTypes';
import { useStartOpenRouterLogin } from '../../api/personalAi';
import { isApiError } from '../../api/http';
import { navigateTo } from '../../lib/navigateTo';
import { hasErrors, mapApiErrorToFields, type FieldErrors, type FieldMapping } from '../../lib/formErrors';
import { useConnectionApi } from './connectionApi';
import { isOpenRouterAuthorizeUrl } from './openRouterUrl';
import {
  draftFrom,
  emptyDraft,
  toDiscoveryInput,
  toInput,
  withCustomModel,
  withDiscovered,
  withKind,
  withModelToggled,
  withoutCustomModel,
  type ConnectionDraft,
  type ConnectionField,
} from './connectionDraft';

const HTTP_TOO_MANY_REQUESTS = 429;
const RATE_LIMITED = '請求太頻繁，請稍後再試。';

/** 基底網址只有相容端點有；其他供應商的細項放不到欄位，併到表單上方。 */
function mappingFor(kind: AiProviderKind): FieldMapping<ConnectionField> {
  const fields: readonly ConnectionField[] =
    kind === 'openai_compatible' ? ['name', 'baseUrl', 'apiKey', 'enabledModels', 'customModels'] : ['name', 'apiKey', 'enabledModels', 'customModels'];
  return { fields };
}

/** 失敗的說明：429 說太頻繁，其他用後端的訊息（沒有欄位可放時）。 */
function failureText(error: unknown, formError: string | null): string | null {
  return isApiError(error) && error.status === HTTP_TOO_MANY_REQUESTS ? RATE_LIMITED : formError;
}

export interface ConnectionForm {
  readonly draft: ConnectionDraft;
  readonly errors: FieldErrors<ConnectionField>;
  /** 對不到欄位的錯誤（包括 ai.provider_failed），顯示在按鈕附近。 */
  readonly formError: string | null;
  /** 「讀取模型」失敗的原因，顯示在那顆按鈕下面。 */
  readonly discoverError: string | null;
  /** 手動加模型的輸入與它自己的錯誤。 */
  readonly customId: string;
  readonly customError: string | null;
  readonly isSaving: boolean;
  /** 「用 OpenRouter 登入」：送出到導走之前都是 true。 */
  readonly isLoggingIn: boolean;
  readonly loginError: string | null;
  readonly isDiscovering: boolean;
  /** 每次送出後有欄位出錯就加一：畫面照它把焦點移到第一個有問題的欄位。 */
  readonly focusCount: number;
  readonly formRef: RefObject<HTMLFormElement | null>;
  readonly change: (patch: Partial<ConnectionDraft>, field?: ConnectionField) => void;
  readonly changeName: (name: string) => void;
  readonly changeKind: (kind: AiProviderKind) => void;
  readonly toggleModel: (id: string) => void;
  readonly setCustomId: (value: string) => void;
  readonly addCustom: () => void;
  readonly removeCustom: (id: string) => void;
  readonly discover: () => void;
  readonly startLogin: () => void;
  readonly save: () => void;
}

type EditActions = Pick<ConnectionForm, 'change' | 'changeName' | 'changeKind' | 'toggleModel' | 'setCustomId' | 'addCustom' | 'removeCustom'>;

/** 草稿與每個欄位的改法：改了欄位就清掉那個欄位上次的錯誤，也清掉表單上方的說明。 */
function useDraftEditing(stored: AiConnection | null) {
  const [draft, setDraft] = useState(() => (stored === null ? emptyDraft() : draftFrom(stored)));
  const [errors, setErrors] = useState<FieldErrors<ConnectionField>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [discoverError, setDiscoverError] = useState<string | null>(null);
  const [customId, setCustomIdState] = useState('');
  const [customError, setCustomError] = useState<string | null>(null);

  function edit(update: (current: ConnectionDraft) => ConnectionDraft, clear: readonly ConnectionField[]) {
    setDraft(update);
    setFormError(null);
    setErrors((current) => Object.fromEntries(Object.entries(current).filter(([field]) => !clear.includes(field as ConnectionField))));
  }

  const actions: EditActions = {
    change: (patch, field) => {
      edit((current) => ({ ...current, ...patch }), field === undefined ? [] : [field]);
    },
    changeName: (name) => {
      edit((current) => ({ ...current, name, isNameEdited: true }), ['name']);
    },
    changeKind: (kind) => {
      edit((current) => withKind(current, kind), ['baseUrl', 'apiKey', 'enabledModels', 'customModels']);
      setDiscoverError(null);
    },
    toggleModel: (id) => {
      edit((current) => withModelToggled(current, id), ['enabledModels']);
    },
    setCustomId: (value) => {
      setCustomIdState(value);
      setCustomError(null);
    },
    addCustom: () => {
      const result = withCustomModel(draft, customId);
      setCustomError(result.error);
      if (result.error !== null) return;
      edit(() => result.draft, ['enabledModels', 'customModels']);
      setCustomIdState('');
    },
    removeCustom: (id) => {
      edit((current) => withoutCustomModel(current, id), ['enabledModels', 'customModels']);
    },
  };
  return { draft, setDraft, errors, setErrors, formError, setFormError, discoverError, setDiscoverError, customId, customError, actions };
}

/** 用 OpenRouter 登入的開始：拿到網址就整頁導走；網址只放行 http(s)，失敗的原因留在 error。 */
function useOpenRouterLoginStart(scope: AiConnectionScope) {
  const mutation = useStartOpenRouterLogin();
  const [error, setError] = useState<string | null>(null);
  // 拿到網址後頁面就要離開了：導走之前按鈕維持忙碌，不讓人再按第二次。
  const [isRedirecting, setIsRedirecting] = useState(false);
  // 在 OpenRouter 按「上一頁」會從 bfcache 還原這一頁，React 的狀態原樣留著：不重設的話按鈕會永遠卡在「前往…」。
  useEffect(() => {
    const restore = (event: PageTransitionEvent) => {
      if (event.persisted) setIsRedirecting(false);
    };
    window.addEventListener('pageshow', restore);
    return () => {
      window.removeEventListener('pageshow', restore);
    };
  }, []);
  return {
    isLoggingIn: mutation.isPending || isRedirecting,
    error,
    start: () => {
      if (mutation.isPending || isRedirecting) return;
      setError(null);
      mutation.mutate(scope, {
        onSuccess: ({ authorizeUrl }) => {
          // 網址來自伺服器：只導到 OpenRouter 自己的 https 網址（navigateTo 另外只放行 http(s)）。
          if (!isOpenRouterAuthorizeUrl(authorizeUrl)) {
            setError('OpenRouter 的登入網址不能使用，請改貼 API 金鑰。');
            return;
          }
          setIsRedirecting(true);
          navigateTo(authorizeUrl);
        },
        onError: (failure) => {
          setError(failureText(failure, mapApiErrorToFields(failure, { fields: [] }).formError));
        },
      });
    },
  };
}

/**
 * 新增、編輯連線的對話框的狀態：草稿、儲存與讀取模型（用畫面上還沒存的值）、
 * 422 的細項照路徑放到欄位下面。stored 是 null 就是新增。onSaved 在存成功後呼叫（關對話框）。
 * scope 決定打組織的還是自己的 API（登入 OpenRouter 也帶它，回來才知道連線要放在哪）。
 */
export function useConnectionForm(scope: AiConnectionScope, stored: AiConnection | null, onSaved: () => void): ConnectionForm {
  const state = useDraftEditing(stored);
  const { draft, setDraft, setErrors, setFormError, setDiscoverError } = state;
  const [focusCount, setFocusCount] = useState(0);
  const formRef = useRef<HTMLFormElement>(null);
  const { save: saveMutation, discover: discoverMutation } = useConnectionApi(scope);
  const login = useOpenRouterLoginStart(scope);

  function showSaveFailure(error: unknown) {
    const mapped = mapApiErrorToFields(error, mappingFor(draft.kind));
    setErrors(mapped.fieldErrors);
    setFormError(failureText(error, mapped.formError));
    if (hasErrors(mapped.fieldErrors)) setFocusCount((count) => count + 1);
  }

  return {
    ...state.actions,
    draft,
    errors: state.errors,
    formError: state.formError,
    discoverError: state.discoverError,
    customId: state.customId,
    customError: state.customError,
    isSaving: saveMutation.isPending,
    isLoggingIn: login.isLoggingIn,
    loginError: login.error,
    isDiscovering: discoverMutation.isPending,
    focusCount,
    formRef,
    discover: () => {
      if (discoverMutation.isPending) return;
      setDiscoverError(null);
      discoverMutation.mutate(toDiscoveryInput(draft, stored?.id ?? null), {
        onSuccess: ({ models }) => {
          setDraft((current) => withDiscovered(current, models));
        },
        onError: (error) => {
          setDiscoverError(failureText(error, mapApiErrorToFields(error, { fields: [] }).formError));
        },
      });
    },
    startLogin: login.start,
    save: () => {
      if (saveMutation.isPending) return;
      setErrors({});
      setFormError(null);
      saveMutation.mutate({ id: stored?.id ?? null, input: toInput(draft) }, { onSuccess: onSaved, onError: showSaveFailure });
    },
  };
}
