import type { NewProjectRequest } from '../../api/types';
import type { FieldErrors, FieldMapping } from '../../lib/formErrors';

/** 和後端 WorkRules 相同；前端先檢查只是為了早一點告訴使用者，最後仍以後端為準。 */
export const PROJECT_KEY_PATTERN = /^[A-Z][A-Z0-9]{1,9}$/;
export const PROJECT_KEY_MAX_LENGTH = 10;
export const PROJECT_NAME_MAX_LENGTH = 100;

export type ProjectField = 'key' | 'name' | 'template' | 'description';

export const PROJECT_FIELDS: readonly ProjectField[] = ['key', 'name', 'template', 'description'];

export const PROJECT_ERROR_MAPPING: FieldMapping<ProjectField> = {
  fields: PROJECT_FIELDS,
  codes: {
    'project.key_invalid': 'key',
    'project.key_taken': 'key',
    'project.name_required': 'name',
    'project.name_too_long': 'name',
    'template.not_found': 'template',
    'project.description_too_long': 'description',
    'project.description_invalid': 'description',
    'project.description_secret': 'description',
  },
};

export const PROJECT_KEY_HINT = '大寫英文字母開頭，只能有大寫字母與數字，2 到 10 個字元。會成為項目編號的前綴，例如 WEB-12。';

function keyError(key: string): string | undefined {
  if (key === '') {
    return '請輸入專案代號。';
  }
  return PROJECT_KEY_PATTERN.test(key)
    ? undefined
    : '代號要用大寫英文字母開頭，只能有大寫字母與數字，2 到 10 個字元。';
}

function nameError(name: string): string | undefined {
  if (name === '') {
    return '請輸入專案名稱。';
  }
  return name.length > PROJECT_NAME_MAX_LENGTH
    ? `名稱最多 ${PROJECT_NAME_MAX_LENGTH} 個字。`
    : undefined;
}

/** 回傳每個欄位的問題；沒有問題的欄位是 undefined。 */
export function validateProject(request: NewProjectRequest): FieldErrors<ProjectField> {
  return {
    key: keyError(request.key),
    name: nameError(request.name),
    template: request.template === '' ? '請選擇模板。' : undefined,
  };
}
