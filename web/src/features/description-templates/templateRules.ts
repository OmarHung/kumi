import type { DescriptionTemplateInput } from '../../api/types';
import type { FieldErrors, FieldMapping } from '../../lib/formErrors';

/** 和後端 ProjectDescriptionRules.MaxTemplateNameLength 相同；前端先檢查只是早一點告訴使用者。 */
export const TEMPLATE_NAME_MAX_LENGTH = 100;

export type TemplateField = 'name' | 'body';

export const TEMPLATE_FIELDS: readonly TemplateField[] = ['name', 'body'];

export const TEMPLATE_ERROR_MAPPING: FieldMapping<TemplateField> = {
  fields: TEMPLATE_FIELDS,
  codes: {
    'description_template.name_required': 'name',
    'description_template.name_too_long': 'name',
    'description_template.name_taken': 'name',
    'description_template.body_required': 'body',
    'project.description_too_long': 'body',
    'project.description_invalid': 'body',
    'project.description_secret': 'body',
  },
};

export function validateTemplate(input: DescriptionTemplateInput): FieldErrors<TemplateField> {
  const name = input.name.trim();
  return {
    name: name === '' ? '請輸入名稱。' : name.length > TEMPLATE_NAME_MAX_LENGTH ? `名稱最多 ${String(TEMPLATE_NAME_MAX_LENGTH)} 個字。` : undefined,
    body: input.body.trim() === '' ? '請輸入內容。' : undefined,
  };
}

/** 複製內建的格式時帶入的名稱。 */
export function copyName(name: string): string {
  return `${name}（副本）`;
}
