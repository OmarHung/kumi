import type { Person } from '../../api/types';

/** 職稱與負責範圍的長度上限，照後端 IdentityRules（MaxJobTitleLength、MaxResponsibilitiesLength）。 */
export const PROFILE_LIMITS = { jobTitle: 60, responsibilities: 200 } as const;

/** 人員名稱下面的次要文字：「職稱 · 負責範圍」，兩個都沒有是 null（不畫）。 */
export function profileText(person: Pick<Person, 'jobTitle' | 'responsibilities'>): string | null {
  const parts = [person.jobTitle, person.responsibilities].filter((part): part is string => part !== null && part !== '');
  return parts.length === 0 ? null : parts.join(' · ');
}
