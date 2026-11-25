import { useState } from 'react';
import { useSaveAiSettings } from '../../api/aiSettings';
import type { AiEffort, AiSelection, AiSettings, AiSettingsInput } from '../../api/aiTypes';
import { isApiError } from '../../api/http';
import { hasErrors, mapApiErrorToFields, stripPathPrefix, type FieldErrors } from '../../lib/formErrors';
import { useUnsavedChangesGuard } from '../../lib/useUnsavedChangesGuard';
import { draftFromLimits, LIMIT_FIELDS, limitsFromDraft, sameLimits, validateLimits, type LimitField, type LimitsDraft } from './usageLimits';
import { effectiveSelection, findChoice, modelChoices, sameSelection, selectionFor, type ModelChoice } from './aiSelection';

const HTTP_TOO_MANY_REQUESTS = 429;
const RATE_LIMITED = '請求太頻繁，請稍後再試。';

export interface AiSettingsFormState {
  readonly enabled: boolean;
  readonly allowPersonal: boolean;
  /** 畫面上有效的預設：選的連線或模型已經被刪掉、關掉時是 null。 */
  readonly selection: AiSelection | null;
  readonly choices: readonly ModelChoice[];
  readonly choice: ModelChoice | undefined;
  /** 用量上限三個欄位（照輸入的字，空字串是不限）與各自的錯誤。 */
  readonly limits: LimitsDraft;
  readonly limitErrors: FieldErrors<LimitField>;
  /** 每次送出後有欄位出錯就加一：畫面照它把焦點移到第一個有問題的欄位。 */
  readonly focusCount: number;
  readonly formError: string | null;
  readonly message: string | null;
  readonly isSaving: boolean;
  readonly hasChanges: boolean;
  readonly guardDialog: ReturnType<typeof useUnsavedChangesGuard>['dialog'];
  readonly setEnabled: (enabled: boolean) => void;
  readonly setAllowPersonal: (allowed: boolean) => void;
  readonly chooseModel: (value: string) => void;
  readonly chooseEffort: (effort: AiEffort | null) => void;
  readonly setLimit: (field: LimitField, value: string) => void;
  readonly save: () => void;
}

/** 用量上限欄位的錯誤，以及每次出錯後加一的計數（畫面照它把焦點移到第一個有問題的欄位）。 */
function useLimitFeedback() {
  const [errors, setErrors] = useState<FieldErrors<LimitField>>({});
  const [focusCount, setFocusCount] = useState(0);
  return {
    errors,
    focusCount,
    clear: (field: LimitField) => {
      setErrors((current) => Object.fromEntries(Object.entries(current).filter(([key]) => key !== field)));
    },
    reset: () => {
      setErrors({});
    },
    show: (next: FieldErrors<LimitField>) => {
      setErrors(next);
      if (hasErrors(next)) setFocusCount((count) => count + 1);
    },
  };
}

/** 儲存與它的結果說明（「已儲存。」或失敗的原因）；改了任何東西就清掉上一次的結果。 */
function useSettingsSave() {
  const [formError, setFormError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const limitFeedback = useLimitFeedback();
  const mutation = useSaveAiSettings();
  return {
    formError,
    message,
    limitErrors: limitFeedback.errors,
    focusCount: limitFeedback.focusCount,
    clearLimitError: limitFeedback.clear,
    isSaving: mutation.isPending,
    touch: () => {
      setMessage(null);
      setFormError(null);
    },
    run: (input: AiSettingsInput, limits: LimitsDraft, limitsChanged: boolean, onSaved: () => void) => {
      if (mutation.isPending) return;
      setMessage(null);
      setFormError(null);
      limitFeedback.reset();
      // 上限沒改就不帶：伺服器沿用存的，兩位管理員同時開著頁面時不會把別人改的上限蓋回去。
      const found = limitsChanged ? validateLimits(limits) : {};
      if (hasErrors(found)) {
        limitFeedback.show(found);
        return;
      }
      mutation.mutate(limitsChanged ? { ...input, limits: limitsFromDraft(limits) } : input, {
        onSuccess: () => {
          onSaved();
          setMessage('已儲存。');
        },
        onError: (error) => {
          const isRateLimited = isApiError(error) && error.status === HTTP_TOO_MANY_REQUESTS;
          const mapped = mapApiErrorToFields(stripPathPrefix(error, 'limits.'), { fields: LIMIT_FIELDS });
          limitFeedback.show(mapped.fieldErrors);
          setFormError(isRateLimited ? RATE_LIMITED : mapped.formError);
        },
      });
    },
  };
}

function hasUnsavedChanges(
  settings: AiSettings,
  choices: readonly ModelChoice[],
  draft: { readonly enabled: boolean; readonly allowPersonal: boolean; readonly selection: AiSelection | null; readonly limits: LimitsDraft },
): boolean {
  return (
    !sameLimits(draft.limits, draftFromLimits(settings.limits)) ||
    draft.enabled !== settings.enabled ||
    draft.allowPersonal !== settings.allowPersonalConnections ||
    !sameSelection(draft.selection, effectiveSelection(choices, settings.default))
  );
}

/** 用量上限的草稿：沒改的欄位照存著的設定。 */
function useLimitsDraft(settings: AiSettings, save: Pick<ReturnType<typeof useSettingsSave>, 'touch' | 'clearLimitError'>) {
  const [draft, setDraft] = useState<Partial<LimitsDraft>>({});
  const limits: LimitsDraft = { ...draftFromLimits(settings.limits), ...draft };
  return {
    limits,
    setLimit: (field: LimitField, value: string) => {
      save.touch();
      save.clearLimitError(field);
      setDraft((current) => ({ ...current, [field]: value }));
    },
    reset: () => {
      setDraft({});
    },
  };
}

/** 兩個勾選的草稿：沒動過的照存著的設定。 */
function useFlagDrafts(settings: AiSettings, touch: () => void) {
  const [draft, setDraft] = useState<{ readonly enabled?: boolean; readonly allowPersonal?: boolean }>({});
  return {
    enabled: draft.enabled ?? settings.enabled,
    allowPersonal: draft.allowPersonal ?? settings.allowPersonalConnections,
    setEnabled: (next: boolean) => {
      touch();
      setDraft((current) => ({ ...current, enabled: next }));
    },
    setAllowPersonal: (next: boolean) => {
      touch();
      setDraft((current) => ({ ...current, allowPersonal: next }));
    },
    reset: () => {
      setDraft({});
    },
  };
}

/**
 * 「啟用」、「允許個人」與「預設」的草稿。連線的增減不經過這裡（對話框自己存），所以起點永遠是快取裡存著的設定；
 * 草稿選的模型在清單上已經不存在時，當作沒選（存的時候送 null）。
 */
export function useAiSettingsForm(settings: AiSettings): AiSettingsFormState {
  const [draftSelection, setDraftSelection] = useState<{ readonly value: AiSelection | null } | null>(null);
  const save = useSettingsSave();
  const limitsState = useLimitsDraft(settings, save);
  const flags = useFlagDrafts(settings, save.touch);

  const choices = modelChoices(settings.connections);
  const { enabled, allowPersonal } = flags;
  const selection = effectiveSelection(choices, draftSelection === null ? settings.default : draftSelection.value);
  const { limits } = limitsState;
  const hasChanges = hasUnsavedChanges(settings, choices, { enabled, allowPersonal, selection, limits });
  const guard = useUnsavedChangesGuard(hasChanges, '離開這一頁的話，AI 設定的修改會不見。');

  return {
    enabled,
    allowPersonal,
    selection,
    choices,
    choice: findChoice(choices, selection),
    limits,
    limitErrors: save.limitErrors,
    focusCount: save.focusCount,
    formError: save.formError,
    message: save.message,
    isSaving: save.isSaving,
    hasChanges,
    guardDialog: guard.dialog,
    setEnabled: flags.setEnabled,
    setAllowPersonal: flags.setAllowPersonal,
    chooseModel: (value) => {
      const next = choices.find((candidate) => candidate.value === value);
      if (next === undefined) return;
      save.touch();
      setDraftSelection({ value: selectionFor(next, selection?.effort ?? null) });
    },
    chooseEffort: (effort) => {
      if (selection === null) return;
      save.touch();
      setDraftSelection({ value: { ...selection, effort } });
    },
    setLimit: limitsState.setLimit,
    save: () => {
      const limitsChanged = !sameLimits(limits, draftFromLimits(settings.limits));
      save.run({ enabled, allowPersonalConnections: allowPersonal, default: selection }, limits, limitsChanged, () => {
        flags.reset();
        setDraftSelection(null);
        limitsState.reset();
      });
    },
  };
}
