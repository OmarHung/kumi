import { useMemo } from 'react';
import { usePeople } from '../../api/queries';
import { useCurrentUser } from '../../auth/CurrentUser';

/** 人員編號對職稱；沒有職稱的人不在裡面。 */
export type PersonTitles = ReadonlyMap<string, string>;

export const NO_TITLES: PersonTitles = new Map();

/**
 * 選單與清單裡顯示在名稱旁邊的職稱。成員的資料（Member、PersonRef）沒有職稱，從組織的人員清單拿：
 * 訪客讀不到人員清單（403），所以不發請求；讀不到時就沒有職稱，選單照常可以用。
 * isEnabled：選單打開之前不必讀（清單和人員頁共用同一份快取）。
 */
export function usePersonTitles(isEnabled = true): PersonTitles {
  const user = useCurrentUser();
  const people = usePeople(isEnabled && user.role !== 'guest');
  return useMemo(() => {
    if (people.data === undefined) return NO_TITLES;
    const titles = new Map<string, string>();
    for (const person of people.data) {
      if (person.jobTitle !== null && person.jobTitle !== '') titles.set(person.personId, person.jobTitle);
    }
    return titles;
  }, [people.data]);
}
