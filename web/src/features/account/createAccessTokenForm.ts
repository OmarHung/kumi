import { useRef, useState, type SubmitEvent } from 'react';
import type { AccessTokenScope, CreateAccessTokenRequest } from '../../api/types';
import { firstInvalidField, hasErrors, mapApiErrorToFields, type FieldErrors, type FieldMapping } from '../../lib/formErrors';
import { useCreateAccessToken } from './accessTokenMutations';
import { AGENT_EXPIRY_MAX_DAYS, DEFAULT_EXPIRY, DEFAULT_SCOPES, SCOPE_OPTIONS, expiryDays, isExpiryAllowed } from './accessTokenText';

export type TokenFieldName = 'name' | 'scopes' | 'expiresInDays';

const FIELDS: readonly TokenFieldName[] = ['name', 'scopes', 'expiresInDays'];
// 名稱的長度上限和後端一樣（AccessTokenRules.MaxNameLength）。
const NAME_MAX_LENGTH = 100;

const MAPPING: FieldMapping<TokenFieldName> = {
  fields: FIELDS,
  codes: {
    'access_token.name_required': 'name',
    'access_token.name_too_long': 'name',
    'access_token.scopes_required': 'scopes',
    'access_token.expiry_invalid': 'expiresInDays',
  },
};

export interface TokenDraft {
  readonly name: string;
  readonly scopes: readonly AccessTokenScope[];
  readonly expiry: string;
}

const INITIAL: TokenDraft = {
  name: '',
  scopes: DEFAULT_SCOPES,
  expiry: DEFAULT_EXPIRY,
};

function validate(draft: TokenDraft): FieldErrors<TokenFieldName> {
  const name = draft.name.trim();
  let nameError: string | undefined;
  if (name === '') nameError = '請輸入名稱。';
  else if (name.length > NAME_MAX_LENGTH) nameError = `名稱不能超過 ${String(NAME_MAX_LENGTH)} 個字元。`;
  return {
    name: nameError,
    scopes: draft.scopes.length === 0 ? '至少選一個範圍。' : undefined,
  };
}

/**
 * 範圍與期限互相牽動：本機代理的權杖最長 90 天，勾了它而原本選得更長就換成 90 天並說明（不讓人送出會被退回的組合）；
 * 使用者自己改期限或取消本機代理，說明就收起來。
 */
function useScopeAndExpiry(draft: TokenDraft, change: (next: Partial<TokenDraft>, field: TokenFieldName) => void) {
  const [expiryNote, setExpiryNote] = useState<string | null>(null);

  function toggleScope(scope: AccessTokenScope, isChecked: boolean) {
    const kept = draft.scopes.filter((candidate) => candidate !== scope);
    const scopes = isChecked ? SCOPE_OPTIONS.map((option) => option.scope).filter((s) => s === scope || kept.includes(s)) : kept;
    const isCapped = !isExpiryAllowed(draft.expiry, scopes);
    change(isCapped ? { scopes, expiry: DEFAULT_EXPIRY } : { scopes }, 'scopes');
    setExpiryNote(isCapped ? `本機代理的權杖最長 ${String(AGENT_EXPIRY_MAX_DAYS)} 天，期限已改成 ${String(AGENT_EXPIRY_MAX_DAYS)} 天。` : null);
  }

  function changeExpiry(expiry: string) {
    setExpiryNote(null);
    change({ expiry }, 'expiresInDays');
  }

  return { expiryNote, toggleScope, changeExpiry };
}

/**
 * 建立對話框的表單：先在本機檢查名稱與範圍，伺服器的 422 依細項路徑（name、scopes、expiresInDays）標在欄位上，
 * 對不到欄位的（409 已達上限、網路）寫在表單上方。成功時明文交給 onSecret，這裡不留。
 */
export function useCreateTokenForm(onSecret: (secret: string) => void) {
  // 欄位用 callback ref 登記自己（畫的時候不碰 ref.current），送出有問題時才把焦點放過去。
  const nodes = useRef<Partial<Record<TokenFieldName, HTMLElement | null>>>({});
  const [draft, setDraft] = useState<TokenDraft>(INITIAL);
  const [errors, setErrors] = useState<FieldErrors<TokenFieldName>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const create = useCreateAccessToken(onSecret);

  function focusFirst(found: FieldErrors<TokenFieldName>) {
    const field = firstInvalidField(FIELDS, found);
    if (field !== undefined) nodes.current[field]?.focus();
  }

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    // 送出中按鈕只標 aria-disabled（焦點留在上面），再按一次照樣觸發 submit：這裡擋下重送。
    if (create.isPending) return;
    const found = validate(draft);
    setFormError(null);
    setErrors(found);
    if (hasErrors(found)) {
      focusFirst(found);
      return;
    }
    const request: CreateAccessTokenRequest = { name: draft.name.trim(), scopes: draft.scopes, expiresInDays: expiryDays(draft.expiry) };
    create.mutate(request, {
      onError: (error) => {
        const mapped = mapApiErrorToFields(error, MAPPING);
        setErrors(mapped.fieldErrors);
        setFormError(mapped.formError);
        focusFirst(mapped.fieldErrors);
      },
    });
  }

  function register(field: TokenFieldName) {
    return (element: HTMLElement | null) => {
      nodes.current[field] = element;
    };
  }

  function change(next: Partial<TokenDraft>, field: TokenFieldName) {
    setDraft((current) => ({ ...current, ...next }));
    setErrors((current) => ({ ...current, [field]: undefined }));
  }

  const { expiryNote, toggleScope, changeExpiry } = useScopeAndExpiry(draft, change);

  return { register, draft, errors, formError, expiryNote, change, changeExpiry, toggleScope, handleSubmit, isPending: create.isPending };
}

export type CreateTokenForm = ReturnType<typeof useCreateTokenForm>;
