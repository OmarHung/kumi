import { useState } from 'react';
import { useSaveAiSettings } from '../../api/aiSettings';
import type { AiEffort, AiSelection, AiSettings, AiSettingsInput } from '../../api/aiTypes';
import { isApiError } from '../../api/http';
import { mapApiErrorToFields } from '../../lib/formErrors';
import { useUnsavedChangesGuard } from '../../lib/useUnsavedChangesGuard';
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
  readonly formError: string | null;
  readonly message: string | null;
  readonly isSaving: boolean;
  readonly hasChanges: boolean;
  readonly guardDialog: ReturnType<typeof useUnsavedChangesGuard>['dialog'];
  readonly setEnabled: (enabled: boolean) => void;
  readonly setAllowPersonal: (allowed: boolean) => void;
  readonly chooseModel: (value: string) => void;
  readonly chooseEffort: (effort: AiEffort | null) => void;
  readonly save: () => void;
}

/** 儲存與它的結果說明（「已儲存。」或失敗的原因）；改了任何東西就清掉上一次的結果。 */
function useSettingsSave() {
  const [formError, setFormError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const mutation = useSaveAiSettings();
  return {
    formError,
    message,
    isSaving: mutation.isPending,
    touch: () => {
      setMessage(null);
      setFormError(null);
    },
    run: (input: AiSettingsInput, onSaved: () => void) => {
      if (mutation.isPending) return;
      setMessage(null);
      setFormError(null);
      mutation.mutate(input, {
        onSuccess: () => {
          onSaved();
          setMessage('已儲存。');
        },
        onError: (error) => {
          const isRateLimited = isApiError(error) && error.status === HTTP_TOO_MANY_REQUESTS;
          setFormError(isRateLimited ? RATE_LIMITED : mapApiErrorToFields(error, { fields: [] }).formError);
        },
      });
    },
  };
}

function hasUnsavedChanges(
  settings: AiSettings,
  choices: readonly ModelChoice[],
  draft: { readonly enabled: boolean; readonly allowPersonal: boolean; readonly selection: AiSelection | null },
): boolean {
  return (
    draft.enabled !== settings.enabled ||
    draft.allowPersonal !== settings.allowPersonalConnections ||
    !sameSelection(draft.selection, effectiveSelection(choices, settings.default))
  );
}

/**
 * 「啟用」、「允許個人」與「預設」的草稿。連線的增減不經過這裡（對話框自己存），所以起點永遠是快取裡存著的設定；
 * 草稿選的模型在清單上已經不存在時，當作沒選（存的時候送 null）。
 */
export function useAiSettingsForm(settings: AiSettings): AiSettingsFormState {
  const [draftFlags, setDraftFlags] = useState<{ readonly enabled?: boolean; readonly allowPersonal?: boolean }>({});
  const [draftSelection, setDraftSelection] = useState<{ readonly value: AiSelection | null } | null>(null);
  const save = useSettingsSave();

  const choices = modelChoices(settings.connections);
  const enabled = draftFlags.enabled ?? settings.enabled;
  const allowPersonal = draftFlags.allowPersonal ?? settings.allowPersonalConnections;
  const selection = effectiveSelection(choices, draftSelection === null ? settings.default : draftSelection.value);
  const hasChanges = hasUnsavedChanges(settings, choices, { enabled, allowPersonal, selection });
  const guard = useUnsavedChangesGuard(hasChanges, '離開這一頁的話，AI 設定的修改會不見。');

  return {
    enabled,
    allowPersonal,
    selection,
    choices,
    choice: findChoice(choices, selection),
    formError: save.formError,
    message: save.message,
    isSaving: save.isSaving,
    hasChanges,
    guardDialog: guard.dialog,
    setEnabled: (next) => {
      save.touch();
      setDraftFlags((current) => ({ ...current, enabled: next }));
    },
    setAllowPersonal: (next) => {
      save.touch();
      setDraftFlags((current) => ({ ...current, allowPersonal: next }));
    },
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
    save: () => {
      save.run({ enabled, allowPersonalConnections: allowPersonal, default: selection }, () => {
        setDraftFlags({});
        setDraftSelection(null);
      });
    },
  };
}
