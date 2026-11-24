import type { FieldOption, ProjectBlueprint } from '../api/blueprint';
import type { CalendarDate, Priority } from '../api/types';
import { PRIORITY_LABELS } from './labels';
import { findItemType, workflowForType } from './blueprint';

/**
 * 匯入時把一格文字轉成伺服器要的值（PM-4b）。純函式：轉得了回 value 與給預覽看的 display，
 * 轉不了回一句說明（不含列號與欄位名稱，由呼叫端組成「第 5 列「負責人」：…」）。呼叫端先處理空白的格子。
 */
export type Converted<T> =
  | { readonly ok: true; readonly value: T; readonly display: string }
  | { readonly ok: false; readonly message: string };

/** 專案成員，加上人員清單裡的 Email（讀不到人員清單時是 null）。 */
export interface ImportMember {
  readonly personId: string;
  readonly displayName: string;
  readonly email: string | null;
}

export interface NamedRef {
  readonly id: string;
  readonly name: string;
}

function good<T>(value: T, display: string): Converted<T> {
  return { ok: true, value, display };
}

function bad(message: string): Converted<never> {
  return { ok: false, message };
}

/** 比對名稱用：前後空白、全形半形、大小寫不計。 */
export function fold(text: string): string {
  return text.trim().normalize('NFKC').toLowerCase();
}

const TAB = 9;
const LINE_FEED = 10;
const CARRIAGE_RETURN = 13;
const LAST_CONTROL = 31;
const DELETE_CHAR = 127;

/** PostgreSQL 不收 NUL，其他控制字元也一律擋（CLAUDE.md）；allowed 是可以留的（說明的換行與 tab）。 */
function hasControlCharacter(text: string, allowed: readonly number[]): boolean {
  return Array.from(text).some((char) => {
    const code = char.codePointAt(0) ?? 0;
    return (code <= LAST_CONTROL || code === DELETE_CHAR) && !allowed.includes(code);
  });
}

/** 一行的文字（標題、文字與連結欄位）：不能有換行與控制字元。 */
export function convertSingleLine(raw: string): Converted<string> {
  const text = raw.trim();
  return hasControlCharacter(text, []) ? bad('不能有換行或控制字元') : good(text, text);
}

/** 多行的文字（說明、長文字欄位）：換行統一成 \n，其他控制字元不收。 */
export function convertMultiLine(raw: string): Converted<string> {
  const text = raw.replace(/\r\n|\r/g, '\n').trim();
  return hasControlCharacter(text, [TAB, LINE_FEED, CARRIAGE_RETURN]) ? bad('不能有控制字元') : good(text, text);
}

const CURRENCY_PREFIX = /^(?:NT\$|NTD|TWD|USD|US\$|RMB|CNY|JPY|[$¥￥€£])\s*/i;
const CURRENCY_SUFFIX = /\s*元$/;
const THOUSANDS_PATTERN = /^[+-]?\d{1,3}(?:,\d{3})+(?:\.\d+)?$/;
const NUMBER_PATTERN = /^[+-]?(?:\d+\.?\d*|\.\d+)$/;

/** 數字與金額：去掉千分位、空白與幣別符號（NT$、$、元…）。 */
export function convertNumber(raw: string): Converted<number> {
  const stripped = raw
    .normalize('NFKC')
    .trim()
    .replace(CURRENCY_PREFIX, '')
    .replace(CURRENCY_SUFFIX, '')
    .replace(/\s/g, '');
  // 逗號只當千分位：1,234 與 1,234.5 可以，3,5 這種（歐洲的小數點、手誤）不收，免得悄悄變成 35。
  const unseparated = stripped.includes(',') ? stripped.replace(/,/g, '') : stripped;
  const isValid = stripped.includes(',') ? THOUSANDS_PATTERN.test(stripped) : NUMBER_PATTERN.test(stripped);
  if (!isValid) return bad(`「${raw.trim()}」不是數字`);
  return good(Number(unseparated), unseparated);
}

const DATE_PATTERN = /^(\d{4})[-/](\d{1,2})[-/](\d{1,2})$/;

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

/** 日期：YYYY-MM-DD、YYYY/MM/DD、YYYY/M/D；要是真的有這一天（2 月 30 日不行）。 */
export function convertDate(raw: string): Converted<CalendarDate> {
  const match = DATE_PATTERN.exec(raw.normalize('NFKC').trim());
  const [, year, month, day] = match ?? [];
  if (year === undefined || month === undefined || day === undefined) {
    return bad(`「${raw.trim()}」不是日期，請用 YYYY-MM-DD 或 YYYY/MM/DD`);
  }
  const check = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day)));
  const isReal = check.getUTCFullYear() === Number(year) && check.getUTCMonth() === Number(month) - 1 && check.getUTCDate() === Number(day);
  if (!isReal) return bad(`「${raw.trim()}」沒有這一天`);
  const value = `${year}-${pad(Number(month))}-${pad(Number(day))}`;
  return good(value, value);
}

const PRIORITY_NAMES: Readonly<Record<string, Priority>> = {
  none: 'none',
  未設定: 'none',
  low: 'low',
  低: 'low',
  medium: 'medium',
  中: 'medium',
  high: 'high',
  高: 'high',
  urgent: 'urgent',
  緊急: 'urgent',
};

export function convertPriority(raw: string): Converted<Priority> {
  const priority = PRIORITY_NAMES[fold(raw)];
  if (priority === undefined) return bad(`「${raw.trim()}」不是優先度，可以是：未設定、低、中、高、緊急`);
  return good(priority, PRIORITY_LABELS[priority]);
}

const TRUE_VALUES = new Set(['true', 'yes', 'y', '是', '1', 'v', '✓', '✔', '☑']);
const FALSE_VALUES = new Set(['false', 'no', 'n', '否', '0']);

export function convertCheckbox(raw: string): Converted<boolean> {
  const text = fold(raw);
  if (TRUE_VALUES.has(text)) return good(true, '是');
  if (FALSE_VALUES.has(text)) return good(false, '否');
  return bad(`「${raw.trim()}」不是勾選的值，可以是：是、否、true、false、1、0`);
}

function findOption(options: readonly FieldOption[], raw: string): FieldOption | undefined {
  const text = fold(raw);
  return options.find((option) => fold(option.name) === text || fold(option.key) === text);
}

export function convertOption(raw: string, options: readonly FieldOption[]): Converted<string> {
  const option = findOption(options, raw);
  return option === undefined ? bad(`「${raw.trim()}」不是這個欄位的選項`) : good(option.key, option.name);
}

const LIST_SEPARATOR = /[,，、;；]/;

/** 多選：用逗號、頓號、分號分開；每一項都要找得到，重複的只算一次。 */
export function convertMultiOptions(raw: string, options: readonly FieldOption[]): Converted<readonly string[]> {
  const names = raw.split(LIST_SEPARATOR).map((part) => part.trim()).filter((part) => part !== '');
  const found = names.map((name) => ({ name, option: findOption(options, name) }));
  const missing = found.find((entry) => entry.option === undefined);
  if (missing !== undefined) return bad(`「${missing.name}」不是這個欄位的選項`);
  const chosen = found.flatMap((entry) => (entry.option === undefined ? [] : [entry.option]));
  const unique = chosen.filter((option, index) => chosen.findIndex((other) => other.key === option.key) === index);
  return good(unique.map((option) => option.key), unique.map((option) => option.name).join('、'));
}

/** 成員（負責人、成員欄位）：名稱或 Email；同名的兩個人要改用 Email。 */
export function convertMember(raw: string, members: readonly ImportMember[]): Converted<string> {
  const text = fold(raw);
  const byEmail = members.filter((member) => member.email !== null && fold(member.email) === text);
  const matches = byEmail.length > 0 ? byEmail : members.filter((member) => fold(member.displayName) === text);
  const [first] = matches;
  if (first === undefined) return bad(`找不到「${raw.trim()}」這個成員`);
  if (matches.length > 1) return bad(`有 ${String(matches.length)} 位成員叫「${raw.trim()}」，請改用 Email`);
  return good(first.personId, first.displayName);
}

/** 週期、里程碑：名稱（專案裡不重複）。 */
export function convertNamed(raw: string, noun: string, list: readonly NamedRef[]): Converted<string> {
  const text = fold(raw);
  const found = list.find((entry) => fold(entry.name) === text);
  return found === undefined ? bad(`找不到「${raw.trim()}」這個${noun}`) : good(found.id, found.name);
}

const ITEM_KEY_PATTERN = /^[A-Z][A-Z0-9]*-[1-9]\d*$/;

/** 上層項目：項目編號，例如 WEB-12（只檢查寫法，在不在由伺服器檢查）。 */
export function convertItemKey(raw: string): Converted<string> {
  const key = raw.normalize('NFKC').trim().toUpperCase();
  return ITEM_KEY_PATTERN.test(key) ? good(key, key) : bad(`「${raw.trim()}」不是項目編號（例如 WEB-12）`);
}

export function convertType(raw: string, blueprint: ProjectBlueprint): Converted<string> {
  const text = fold(raw);
  const found = blueprint.itemTypes.find((type) => fold(type.name) === text || fold(type.key) === text);
  return found === undefined ? bad(`找不到「${raw.trim()}」這個類型`) : good(found.key, found.name);
}

/** 狀態：名稱或 key，要在這個類型的流程裡。 */
export function convertState(raw: string, blueprint: ProjectBlueprint, typeKey: string): Converted<string> {
  const type = findItemType(blueprint, typeKey);
  const workflow = workflowForType(blueprint, typeKey);
  if (type === undefined || workflow === undefined) return bad('要先有可用的類型才能檢查狀態');
  const text = fold(raw);
  const found = workflow.states.find((state) => fold(state.name) === text || fold(state.key) === text);
  if (found === undefined) return bad(`「${raw.trim()}」不是「${type.name}」流程裡的狀態`);
  return good(found.key, found.name);
}
