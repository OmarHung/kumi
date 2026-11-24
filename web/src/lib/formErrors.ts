import { errorMessage, isApiError, type ApiError } from '../api/http';

export type FieldErrors<F extends string> = Readonly<Partial<Record<F, string>>>;

export interface MappedErrors<F extends string> {
  readonly fieldErrors: FieldErrors<F>;
  /** 對不到欄位的訊息，顯示在表單上方。 */
  readonly formError: string | null;
}

export interface FieldMapping<F extends string> {
  /** 表單上的欄位，也是 details 的 path 可能的值，順序就是畫面上的順序。 */
  readonly fields: readonly F[];
  /** 沒有 path 時用錯誤代碼對到欄位，例如 project.key_taken → key。 */
  readonly codes?: Readonly<Partial<Record<string, F>>>;
}

function fieldFor<F extends string>(
  mapping: FieldMapping<F>,
  path: string | null,
  code: string,
): F | undefined {
  const byPath = mapping.fields.find((field) => field === path);
  const byCode = mapping.codes?.[code];
  // 代碼對到的欄位這次不在畫面上（例如這個類型沒有上層項目），當成對不到，訊息才不會不見。
  return byPath ?? (byCode !== undefined && mapping.fields.includes(byCode) ? byCode : undefined);
}

// TypeScript 證明不了「從 [欄位, 訊息] 組出來的物件」符合泛型的 mapped type，只好在這裡轉型一次；
// 輸入的型別已經限制欄位只能是 F。
function toFieldErrors<F extends string>(entries: readonly (readonly [F, string])[]): FieldErrors<F> {
  return Object.fromEntries(entries) as FieldErrors<F>;
}

interface Entry<F extends string> {
  readonly field: F | undefined;
  readonly message: string;
}

function entriesOf<F extends string>(error: ApiError, mapping: FieldMapping<F>): Entry<F>[] {
  if (error.details.length === 0) {
    return [{ field: fieldFor(mapping, null, error.code), message: error.message }];
  }
  return error.details.map((detail) => ({
    field: fieldFor(mapping, detail.path, detail.code),
    message: detail.message,
  }));
}

/**
 * 把後端的失敗分到表單的欄位：先看 details 的 path，再看代碼；都對不到的放在表單上方。
 * 每個欄位只留第一則訊息，畫面才不會一次塞好幾行。
 */
export function mapApiErrorToFields<F extends string>(
  error: unknown,
  mapping: FieldMapping<F>,
): MappedErrors<F> {
  if (!isApiError(error)) {
    return { fieldErrors: toFieldErrors<F>([]), formError: errorMessage(error) };
  }
  const entries = entriesOf(error, mapping);
  const matched = entries.flatMap(({ field, message }) =>
    field === undefined ? [] : [[field, message] as const],
  );
  const firstPerField = matched.filter(
    ([field], index) => matched.findIndex(([other]) => other === field) === index,
  );
  const unmatched = entries.filter((entry) => entry.field === undefined);
  return {
    fieldErrors: toFieldErrors(firstPerField),
    formError: unmatched.length > 0 ? unmatched.map((entry) => entry.message).join(' ') : null,
  };
}

export function firstInvalidField<F extends string>(
  fields: readonly F[],
  errors: FieldErrors<F>,
): F | undefined {
  return fields.find((field) => errors[field] !== undefined);
}

export function hasErrors<F extends string>(errors: FieldErrors<F>): boolean {
  return Object.values(errors).some((message) => message !== undefined);
}
