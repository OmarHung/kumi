import { useState } from 'react';
import { useNavigate } from 'react-router';
import { isApiError } from '../../api/http';
import { useCreateDescriptionTemplate, useUpdateDescriptionTemplate } from '../../api/descriptionTemplates';
import type { DescriptionTemplateInput } from '../../api/types';
import { hasErrors, mapApiErrorToFields, type FieldErrors } from '../../lib/formErrors';
import { useUnsavedChangesGuard } from '../../lib/useUnsavedChangesGuard';
import { TEMPLATE_ERROR_MAPPING, validateTemplate, type TemplateField } from './templateRules';
import { TEMPLATES_PATH } from './templatePaths';


/** 送出後有問題時，焦點要去的欄位；n 每次加一，同一個欄位連續兩次有問題也會再移一次。 */
export interface FocusRequest {
  readonly field: TemplateField;
  readonly n: number;
}

const NOT_FOUND_CODE = 'description_template.not_found';
const GONE_MESSAGE = '這個說明格式已經被刪掉了。按「儲存」會另存成新的格式。';

/** 草稿、欄位的錯誤、焦點要去的欄位。改了某個欄位就清掉那一欄上次送出的錯誤。 */
function useTemplateDraft(initial: DescriptionTemplateInput) {
  const [draft, setDraft] = useState(initial);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<TemplateField>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [focusRequest, setFocusRequest] = useState<FocusRequest | null>(null);
  return {
    draft,
    fieldErrors,
    formError,
    focusRequest,
    hasChanges: draft.name !== initial.name || draft.body !== initial.body || draft.isDefault !== initial.isDefault,
    change: (patch: Partial<DescriptionTemplateInput>, field?: TemplateField) => {
      setDraft((current) => ({ ...current, ...patch }));
      if (field !== undefined) setFieldErrors((errors) => ({ ...errors, [field]: undefined }));
    },
    showErrors: (errors: FieldErrors<TemplateField>, message: string | null) => {
      setFieldErrors(errors);
      setFormError(message);
      if (hasErrors(errors)) setFocusRequest((current) => ({ field: errors.name === undefined ? 'body' : 'name', n: (current?.n ?? 0) + 1 }));
    },
  };
}

/**
 * 新增或修改一份說明格式的表單狀態：草稿、欄位的錯誤、儲存（修改時別人剛好刪掉了就另存成新的）。伺服器的 409／422 照路徑放到名稱或內容下面（內容不丟），
 * 對不到欄位的（例如上限）放在表單上方；有改過卻要離開時問一次（useUnsavedChangesGuard）。
 * 存好回清單，提示「已儲存「名稱」。」（useArrivalNotice 讀 navigate 的 state）。
 */
export function useTemplateForm(initial: DescriptionTemplateInput, id: string | undefined) {
  const navigate = useNavigate();
  const create = useCreateDescriptionTemplate();
  const update = useUpdateDescriptionTemplate();
  const state = useTemplateDraft(initial);
  // 儲存時才知道別人已經把這一份刪掉（404）：表單與內容留著，之後的「儲存」改成另存新的。
  const [isGone, setIsGone] = useState(false);
  const isSaving = create.isPending || update.isPending;
  const guard = useUnsavedChangesGuard(state.hasChanges, '離開這一頁的話，名稱與內容的修改會不見。');

  function save() {
    if (isSaving) return;
    const input = { ...state.draft, name: state.draft.name.trim() };
    const errors = validateTemplate(input);
    if (hasErrors(errors)) {
      state.showErrors(errors, null);
      return;
    }
    state.showErrors({}, null);
    const options = {
      onSuccess: () => {
        guard.allowLeave();
        void navigate(TEMPLATES_PATH, { state: { notice: `已儲存「${input.name}」。` } });
      },
      onError: (error: unknown) => {
        if (isApiError(error) && error.code === NOT_FOUND_CODE) {
          setIsGone(true);
          state.showErrors({}, GONE_MESSAGE);
          return;
        }
        const mapped = mapApiErrorToFields(error, TEMPLATE_ERROR_MAPPING);
        state.showErrors(mapped.fieldErrors, mapped.formError);
      },
    };
    if (id === undefined || isGone) create.mutate(input, options);
    else update.mutate({ id, input }, options);
  }

  return {
    ...state,
    isSaving,
    guardDialog: guard.dialog,
    save,
    cancel: () => {
      void navigate(TEMPLATES_PATH);
    },
  };
}

export type TemplateFormState = ReturnType<typeof useTemplateForm>;
