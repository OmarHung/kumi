import type { FieldType, ProjectBlueprint } from '../../api/blueprint';
import type { FieldDraft, SettingsDraft } from '../../domain/settingsDraft';
import { BLUEPRINT_KEY_PATTERN, fieldKeyError, nameError, suggestKey } from '../../domain/settingsValidation';

/** 新增欄位的表單：key 是 null 表示還沒自己改過，跟著名稱給建議值。 */
export interface OptionInput {
  readonly id: number;
  readonly name: string;
  readonly key: string | null;
}

export interface FieldForm {
  readonly name: string;
  readonly key: string | null;
  readonly type: FieldType;
  readonly currency: string;
  readonly options: readonly OptionInput[];
  readonly appliesTo: readonly string[];
}

export interface FieldFormErrors {
  readonly name?: string;
  readonly key?: string;
  readonly options?: string;
  /** 每個選項的問題，以選項輸入列的 id 為鍵。 */
  readonly optionErrors: ReadonlyMap<number, { readonly name?: string; readonly key?: string }>;
}

export const FIELD_TYPE_ORDER: readonly FieldType[] = [
  'text',
  'long_text',
  'number',
  'currency',
  'date',
  'single_select',
  'multi_select',
  'person',
  'checkbox',
  'url',
];

/** 常用的幣別（ISO 4217）。藍圖接受任何三個大寫字母的代碼，這裡先列常見的。 */
export const CURRENCIES: readonly { readonly code: string; readonly name: string }[] = [
  { code: 'TWD', name: '新臺幣' },
  { code: 'USD', name: '美元' },
  { code: 'EUR', name: '歐元' },
  { code: 'JPY', name: '日圓' },
  { code: 'CNY', name: '人民幣' },
  { code: 'HKD', name: '港幣' },
  { code: 'GBP', name: '英鎊' },
  { code: 'KRW', name: '韓元' },
  { code: 'SGD', name: '新加坡幣' },
  { code: 'AUD', name: '澳幣' },
];

export function isSelectType(type: FieldType): boolean {
  return type === 'single_select' || type === 'multi_select';
}

export function emptyFieldForm(types: readonly { readonly key: string }[]): FieldForm {
  return {
    name: '',
    key: null,
    type: 'text',
    currency: 'TWD',
    options: [{ id: 1, name: '', key: null }],
    // 預設套用到每一種類型；只有一種類型的專案（例如簡單任務）不必再勾。
    appliesTo: types.map((type) => type.key),
  };
}

export function suggestedFieldKey(form: FieldForm, base: ProjectBlueprint, draft: SettingsDraft): string {
  const taken = new Set([...base.fields, ...draft.fields].map((field) => field.key));
  return form.key ?? suggestKey(form.name, 'field', taken);
}

/** 每個新選項實際的 key：自己填的，或依名稱建議、避開前面幾個選項已經用的。 */
export function resolvedOptionKeys(options: readonly OptionInput[]): readonly string[] {
  return options.reduce<readonly string[]>(
    (keys, option) => [...keys, option.key ?? suggestKey(option.name, 'option', new Set(keys))],
    [],
  );
}

function optionErrorsOf(options: readonly OptionInput[]): FieldFormErrors['optionErrors'] {
  const keys = resolvedOptionKeys(options);
  const entries = options.flatMap((option, index) => {
    const key = keys[index] ?? '';
    const keyError = !BLUEPRINT_KEY_PATTERN.test(key)
      ? 'key 要用小寫英文字母開頭，只能有小寫字母、數字與底線，2 到 40 個字元。'
      : keys.indexOf(key) !== index
        ? '和前面的選項 key 重複了。'
        : undefined;
    const errors = { name: nameError(option.name), key: keyError };
    return errors.name === undefined && errors.key === undefined ? [] : [[option.id, errors] as const];
  });
  return new Map(entries);
}

export function validateFieldForm(form: FieldForm, base: ProjectBlueprint, draft: SettingsDraft): FieldFormErrors {
  const isSelect = isSelectType(form.type);
  return {
    name: nameError(form.name),
    key: fieldKeyError(base, draft, suggestedFieldKey(form, base, draft)),
    options: isSelect && form.options.length === 0 ? '選項型的欄位至少要有一個選項。' : undefined,
    optionErrors: isSelect ? optionErrorsOf(form.options) : new Map(),
  };
}

export function hasFieldFormErrors(errors: FieldFormErrors): boolean {
  return errors.name !== undefined || errors.key !== undefined || errors.options !== undefined || errors.optionErrors.size > 0;
}

/** 表單變成草稿裡的欄位：選項型才帶選項、金額才帶幣別。 */
export function fieldFromForm(form: FieldForm, key: string): FieldDraft {
  const keys = resolvedOptionKeys(form.options);
  return {
    key,
    name: form.name.trim(),
    type: form.type,
    ...(isSelectType(form.type) && {
      options: form.options.map((option, index) => ({ key: keys[index] ?? '', name: option.name.trim() })),
    }),
    ...(form.type === 'currency' && { currency: form.currency }),
    appliesTo: form.appliesTo,
  };
}
