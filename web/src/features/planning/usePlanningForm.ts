import { useEffect, useState, type RefObject, type SubmitEvent } from 'react';
import { mapApiErrorToFields, type FieldErrors, type FieldMapping } from '../../lib/formErrors';

interface PlanningFormOptions<D extends { readonly name: string }, F extends string> {
  /** 名稱的輸入框：對話框自己建（ref 放在回傳的物件裡，React 編譯器的檢查會當成在 render 時讀 ref）。 */
  readonly nameRef: RefObject<HTMLInputElement | null>;
  readonly initial: D;
  readonly mapping: FieldMapping<F>;
  /** 送出前先擋掉伺服器一定會退回的；回傳有問題的欄位。 */
  readonly validate: (draft: D) => FieldErrors<F>;
  /** 送出的內容（去掉名稱前後的空白等）。 */
  readonly normalize: (draft: D) => D;
  readonly error: unknown;
  readonly onSubmit: (draft: D) => void;
  readonly onEdit: () => void;
}

export interface PlanningForm<D, F extends string> {
  readonly draft: D;
  readonly change: (patch: Partial<D>) => void;
  /** 欄位的錯誤：先看自己擋下的，再看伺服器退回的。 */
  readonly errorOf: (field: F) => string | undefined;
  readonly formError: string | null;
  readonly handleSubmit: (event: SubmitEvent<HTMLFormElement>) => void;
}

/**
 * 週期與里程碑的對話框共用的表單狀態：預設名稱整個選取（要換直接打字）、名稱重複的回應回來後焦點到名稱、
 * 改了任何欄位就清掉上一次的失敗。
 */
export function usePlanningForm<D extends { readonly name: string }, F extends string>({
  nameRef,
  initial,
  mapping,
  validate,
  normalize,
  error,
  onSubmit,
  onEdit,
}: PlanningFormOptions<D, F>): PlanningForm<D, F> {
  const [draft, setDraft] = useState(initial);
  // 以字串為鍵：空物件不能直接當成泛型欄位的錯誤（TypeScript 證明不了），讀的時候才用欄位名稱。
  const [localErrors, setLocalErrors] = useState<Readonly<Partial<Record<string, string>>>>({});
  const mapped = error === null || error === undefined ? null : mapApiErrorToFields(error, mapping);
  const hasNameError = mapped?.fieldErrors['name' as F] !== undefined;

  useEffect(() => {
    nameRef.current?.select();
  }, [nameRef]);

  useEffect(() => {
    if (hasNameError) nameRef.current?.focus();
  }, [error, hasNameError, nameRef]);

  return {
    draft,
    change: (patch) => {
      setDraft((current) => ({ ...current, ...patch }));
      setLocalErrors({});
      onEdit();
    },
    errorOf: (field) => localErrors[field] ?? mapped?.fieldErrors[field],
    formError: mapped?.formError ?? null,
    handleSubmit: (event) => {
      event.preventDefault();
      const problems = validate(draft);
      setLocalErrors(problems);
      if (Object.keys(problems).length === 0) {
        onSubmit(normalize(draft));
      } else if ('name' in problems) {
        nameRef.current?.focus();
      }
    },
  };
}
