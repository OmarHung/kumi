import type { AiUsageLimits } from '../../api/aiTypes';
import { MAX_REQUEST_LIMIT, MAX_TOKEN_LIMIT } from '../host-agents/hostAgentText';

/** 空字串是不限。數字欄位照輸入的字存，送出時才轉。 */
export interface LimitsDraft {
  readonly dailyRequestsPerPerson: string;
  readonly monthlyRequests: string;
  readonly monthlyTokens: string;
}

export type LimitField = keyof LimitsDraft;

/** 畫面上的順序（送出有問題時焦點移到第一個）。 */
export const LIMIT_FIELDS: readonly LimitField[] = ['dailyRequestsPerPerson', 'monthlyRequests', 'monthlyTokens'];

export type LimitErrors = Readonly<Partial<Record<LimitField, string>>>;

const MAXIMUMS: Readonly<Record<LimitField, number>> = {
  dailyRequestsPerPerson: MAX_REQUEST_LIMIT,
  monthlyRequests: MAX_REQUEST_LIMIT,
  monthlyTokens: MAX_TOKEN_LIMIT,
};

const text = (value: number | null): string => (value === null ? '' : String(value));

export function draftFromLimits(limits: AiUsageLimits): LimitsDraft {
  return {
    dailyRequestsPerPerson: text(limits.dailyRequestsPerPerson),
    monthlyRequests: text(limits.monthlyRequests),
    monthlyTokens: text(limits.monthlyTokens),
  };
}

function parseLimit(value: string, max: number): number | null | 'invalid' {
  const trimmed = value.trim();
  if (trimmed === '') return null;
  if (!/^\d+$/.test(trimmed)) return 'invalid';
  const parsed = Number(trimmed);
  return parsed >= 1 && parsed <= max ? parsed : 'invalid';
}

const rangeMessage = (max: number) => `填 1 到 ${max.toLocaleString('en-US')} 的整數，留白是不限`;

/** 送出前的檢查（和後端同一套範圍；真正的檢查還是後端）。 */
export function validateLimits(draft: LimitsDraft): LimitErrors {
  const errors: Partial<Record<LimitField, string>> = {};
  for (const field of LIMIT_FIELDS) {
    if (parseLimit(draft[field], MAXIMUMS[field]) === 'invalid') errors[field] = rangeMessage(MAXIMUMS[field]);
  }
  return errors;
}

function limitOf(field: LimitField, draft: LimitsDraft): number | null {
  const parsed = parseLimit(draft[field], MAXIMUMS[field]);
  return parsed === 'invalid' ? null : parsed;
}

/** 草稿通過 validateLimits 之後換成要送的內容。 */
export function limitsFromDraft(draft: LimitsDraft): AiUsageLimits {
  return {
    dailyRequestsPerPerson: limitOf('dailyRequestsPerPerson', draft),
    monthlyRequests: limitOf('monthlyRequests', draft),
    monthlyTokens: limitOf('monthlyTokens', draft),
  };
}

export function sameLimits(a: LimitsDraft, b: LimitsDraft): boolean {
  return LIMIT_FIELDS.every((field) => a[field].trim() === b[field].trim());
}

/** 「已用 12 / 上限 1,000」；不限時只寫已用。 */
export function usageText(used: number, limit: number | null, unit: string): string {
  const base = `這個月已用 ${used.toLocaleString('en-US')} ${unit}`;
  return limit === null ? `${base}（不限）` : `${base}，上限 ${limit.toLocaleString('en-US')} ${unit}`;
}
