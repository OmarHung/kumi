import { useMemo } from 'react';
import type { Member } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { useProjectTable } from '../item-table/ProjectTableContext';

/** 打 @ 時可以選的人：這個專案的成員，不含自己（提及自己不會通知，列出來只會讓人選錯）。 */
export function useMentionCandidates(): readonly Member[] {
  const { detail } = useProjectTable();
  const { personId } = useCurrentUser();
  return useMemo(() => detail.members.filter((member) => member.personId !== personId), [detail.members, personId]);
}
