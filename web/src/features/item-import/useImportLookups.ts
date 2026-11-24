import { useMemo } from 'react';
import { useCycles, useMilestones } from '../../api/planning';
import { usePeople } from '../../api/queries';
import type { ProjectDetail } from '../../api/types';
import type { ImportMember, NamedRef } from '../../domain/importValues';

export interface ImportLookups {
  /** 人員清單、週期、里程碑都讀完了（失敗也算讀完：沒有的就是空的）才能開始檢查，否則會誤報「找不到」。 */
  readonly isReady: boolean;
  readonly members: readonly ImportMember[];
  readonly cycles: readonly NamedRef[];
  readonly milestones: readonly NamedRef[];
}

/** 轉換值要查的清單：專案成員（Email 來自人員清單，讀不到時只能用名稱）、週期與里程碑。 */
export function useImportLookups(detail: ProjectDetail): ImportLookups {
  const projectKey = detail.project.key;
  const people = usePeople();
  const cycles = useCycles(projectKey);
  const milestones = useMilestones(projectKey);
  const members = useMemo(
    () =>
      detail.members.map((member): ImportMember => ({
        personId: member.personId,
        displayName: member.displayName,
        email: people.data?.find((person) => person.personId === member.personId)?.email ?? null,
      })),
    [detail.members, people.data],
  );
  const cycleRefs = useMemo(() => cycles.data ?? [], [cycles.data]);
  const milestoneRefs = useMemo(() => milestones.data ?? [], [milestones.data]);
  return {
    isReady: !people.isPending && !cycles.isPending && !milestones.isPending,
    members,
    cycles: cycleRefs,
    milestones: milestoneRefs,
  };
}
