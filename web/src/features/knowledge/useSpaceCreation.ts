import { useProjectDetails, useProjects } from '../../api/queries';
import type { Project } from '../../api/types';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { useKnowledgeSpaces } from './useKnowledgeSpaces';

export interface SpaceCreation {
  /** 還在判斷能建哪些（空間清單、專案、專案的權限還在讀）：先不顯示「新增空間」，也不先顯示再消失。 */
  readonly isPending: boolean;
  readonly canCreateOrganizationSpace: boolean;
  /** 能建專案空間的專案：沒封存、還沒有空間、自己有 project.manage（組織管理員是每個專案）。 */
  readonly projectChoices: readonly Project[];
  readonly canCreate: boolean;
}

/**
 * 「新增空間」要不要出現、對話框能選什麼：組織空間只有組織管理員能建；專案空間要那個專案的 project.manage。
 * 這只決定入口，真正的檢查在伺服器（看不到的專案已經有空間時，建立會回 409 space_project_taken）。
 */
export function useSpaceCreation(): SpaceCreation {
  const user = useCurrentUser();
  const isAdmin = isOrganizationAdmin(user);
  const projects = useProjects();
  const spaces = useKnowledgeSpaces();
  const taken = new Set((spaces.data ?? []).flatMap((space) => (space.project === null ? [] : [space.project.key])));
  const candidates = (projects.data ?? []).filter((project) => project.archivedAt === null && !taken.has(project.key));
  // 組織管理員的權限是全部，不必一個個專案去問；一般成員要讀專案內容才知道自己的權限。
  const details = useProjectDetails(isAdmin || spaces.data === undefined ? [] : candidates.map((project) => project.key));
  const projectChoices = isAdmin
    ? candidates
    : candidates.filter((project) => details.get(project.key)?.myPermissions.includes('project.manage') === true);
  const isDetailsPending = !isAdmin && spaces.isSuccess && projects.isSuccess && details.size < candidates.length;
  const isPending = projects.isPending || spaces.isPending || isDetailsPending;
  return {
    isPending,
    canCreateOrganizationSpace: isAdmin,
    projectChoices,
    canCreate: isAdmin || projectChoices.length > 0,
  };
}
