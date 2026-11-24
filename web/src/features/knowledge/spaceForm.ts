import {
  KNOWLEDGE_SPACE_DESCRIPTION_MAX,
  KNOWLEDGE_SPACE_KEY_PATTERN,
  KNOWLEDGE_SPACE_NAME_MAX,
} from '../../api/knowledge';
import type { FieldErrors, FieldMapping } from '../../lib/formErrors';

export type SpaceField = 'projectKey' | 'name' | 'key' | 'description';

/** 順序就是畫面上的順序（第一個有問題的欄位拿到焦點）。 */
export const SPACE_FIELDS: readonly SpaceField[] = ['projectKey', 'name', 'key', 'description'];

export const SPACE_ERROR_MAPPING: FieldMapping<SpaceField> = {
  fields: SPACE_FIELDS,
  codes: {
    'knowledge.space_key_taken': 'key',
    'knowledge.space_project_taken': 'projectKey',
  },
};

export const SPACE_KEY_MAX_LENGTH = 10;
export const SPACE_KEY_HINT = '大寫英文字母開頭，只能有大寫字母與數字，2 到 10 個字元。會出現在網址裡（例如 /knowledge/HR），建立後不能改。';

/**
 * 組織空間的代號從名稱推：名稱本身就是一個英文字（HR、Handbook）時直接拿來用，
 * 中文或有空白的名稱推不出有意義的代號，留空讓使用者自己取。
 */
export function suggestSpaceKey(name: string): string {
  const candidate = name.trim().toUpperCase();
  return KNOWLEDGE_SPACE_KEY_PATTERN.test(candidate) ? candidate : '';
}

export function validateSpaceName(name: string): string | undefined {
  const trimmed = name.trim();
  if (trimmed === '') return '請輸入空間名稱。';
  return trimmed.length > KNOWLEDGE_SPACE_NAME_MAX ? `名稱最多 ${String(KNOWLEDGE_SPACE_NAME_MAX)} 個字。` : undefined;
}

export function validateSpaceKey(key: string): string | undefined {
  if (key === '') return '請輸入空間代號。';
  return KNOWLEDGE_SPACE_KEY_PATTERN.test(key) ? undefined : '代號要用大寫英文字母開頭，只能有大寫字母與數字，2 到 10 個字元。';
}

export function validateSpaceDescription(description: string): string | undefined {
  return description.trim().length > KNOWLEDGE_SPACE_DESCRIPTION_MAX
    ? `簡介最多 ${String(KNOWLEDGE_SPACE_DESCRIPTION_MAX)} 個字。`
    : undefined;
}

export function withoutErrorOf(errors: FieldErrors<SpaceField>, field: SpaceField): FieldErrors<SpaceField> {
  return { ...errors, [field]: undefined };
}
