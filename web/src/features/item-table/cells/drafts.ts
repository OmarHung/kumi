import type { FieldDefinition } from '../../../api/blueprint';
import type { FieldValue } from '../../../api/types';
import { isCalendarDate } from '../../../components/DatePicker/calendarDates';
import { invalidCommand, patchCommand, UNCHANGED, type CellCommand } from './useItemCommand';

/** 和後端 WorkRules 相同的長度上限；超過的字在輸入框就打不進去。 */
export const MAX_TITLE_LENGTH = 300;
export const MAX_TEXT_LENGTH = 2_000;
export const MAX_LONG_TEXT_LENGTH = 50_000;
export const MAX_DESCRIPTION_LENGTH = 50_000;
export const MAX_COMMENT_LENGTH = 10_000;

export const NUMBER_MESSAGE = '請輸入數字。';

type ParsedNumber = number | null | 'invalid';

/** 數字欄用一般的文字框讀：瀏覽器的數字框遇到打錯的字會回空字串，分不出「清掉」還是「打錯」。 */
export function parseNumber(draft: string): ParsedNumber {
  const trimmed = draft.trim().replaceAll(',', '');
  if (trimmed === '') {
    return null;
  }
  const value = Number(trimmed);
  return Number.isFinite(value) ? value : 'invalid';
}

export const DATE_MESSAGE = '日期的格式不對，請輸入像 2026/12/15 的日期。';

/**
 * 日期欄的草稿（components/DatePicker 的值）：解析得了的是 'YYYY-MM-DD'，空的是清掉，其他的是打了解析不了的字
 * （不合法：不默默當成清掉，使用者會以為存了）。
 */
export function isValidDateDraft(draft: string): boolean {
  return draft.trim() === '' || isCalendarDate(draft);
}

/** 合法的日期草稿要送的值：空的是 null（清掉）。 */
export function dateValue(draft: string): string | null {
  return draft.trim() === '' ? null : draft;
}

export function numberDraft(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : String(value);
}

/** 文字類：去掉前後空白，空的就是清掉。 */
export function textValue(draft: string): string | null {
  const trimmed = draft.trim();
  return trimmed === '' ? null : trimmed;
}

function sameValue(a: FieldValue | null | undefined, b: FieldValue | null | undefined): boolean {
  return JSON.stringify(a ?? null) === JSON.stringify(b ?? null);
}

/** 自訂欄位的值轉成編輯框裡的字串。 */
export function fieldDraft(definition: FieldDefinition, value: FieldValue | undefined): string {
  if (value === undefined) {
    return '';
  }
  return definition.type === 'number' || definition.type === 'currency'
    ? numberDraft(typeof value === 'number' ? value : null)
    : String(value);
}

function textFieldValue(definition: FieldDefinition, draft: string): string | null {
  // 長文字保留使用者打的換行與縮排，只有整段空白才算清掉。
  if (definition.type === 'long_text') {
    return draft.trim() === '' ? null : draft;
  }
  return textValue(draft);
}

/** 自訂欄位的修改：值是 null 表示清掉那個欄位。 */
export function fieldCommand(
  definition: FieldDefinition,
  current: FieldValue | undefined,
  draft: string,
): CellCommand {
  if (definition.type === 'date') {
    return isValidDateDraft(draft) ? fieldValueCommand(definition.key, current, dateValue(draft)) : invalidCommand(DATE_MESSAGE);
  }
  if (definition.type !== 'number' && definition.type !== 'currency') {
    return fieldValueCommand(definition.key, current, textFieldValue(definition, draft));
  }
  const value = parseNumber(draft);
  return value === 'invalid'
    ? invalidCommand(NUMBER_MESSAGE)
    : fieldValueCommand(definition.key, current, value);
}

export function fieldValueCommand(
  fieldKey: string,
  current: FieldValue | undefined,
  value: FieldValue | null,
): CellCommand {
  return sameValue(current, value) ? UNCHANGED : patchCommand({ fields: { [fieldKey]: value } });
}
