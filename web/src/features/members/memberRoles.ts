import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import type { ProjectBlueprint } from '../../api/blueprint';
import { removeMember, updateMember } from '../../api/projects';
import { queryKeys } from '../../api/queryKeys';
import type { Member, Project, ProjectDetail } from '../../api/types';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { BUILT_IN_PROJECT_ROLE_DESCRIPTIONS, BUILT_IN_PROJECT_ROLES, isBuiltInProjectRole, projectRoleLabel } from '../../domain/labels';
import { removeQueriesWhenUnobserved } from '../../lib/removeQueriesWhenUnobserved';
import type { ArrivalState } from '../../lib/useArrivalNotice';

export interface RoleOption {
  readonly value: string;
  readonly label: string;
}

/** 改角色、加入、移除成功或失敗時，對話框上的說明。 */
export interface MemberFeedback {
  readonly onSaved: (message: string) => void;
  readonly onFailed: (message: string) => void;
}

/** 可以指派的角色：內建的五個，加上藍圖 roles 裡自訂的。 */
export function roleOptions(blueprint: ProjectBlueprint): RoleOption[] {
  const custom = (blueprint.roles ?? []).map((role) => role.key);
  return [...BUILT_IN_PROJECT_ROLES, ...custom].map((role) => ({
    value: role,
    label: projectRoleLabel(role, blueprint.roles),
  }));
}

/** 內建角色的一句說明（加入成員時寫在角色選單下面）；自訂角色的權限在設定頁，這裡沒有。 */
export function roleDescription(role: string): string | undefined {
  return isBuiltInProjectRole(role) ? BUILT_IN_PROJECT_ROLE_DESCRIPTIONS[role] : undefined;
}

/** 設定成員、設定角色權限、套用變更集的地方都給同一段說明：這兩個權限等同專案的最高權限。 */
export const PRIVILEGED_PERMISSIONS_NOTE =
  '有「管理專案」（project.manage）的角色能設定成員與角色；能「套用變更集」（blueprint.apply）的角色能改每個角色的權限。兩者都等同這個專案的最高權限，只給信得過的人。';

export interface MemberRoleChange {
  readonly personId: string;
  readonly role: string;
}

/** 加成員或改角色（後端同一個端點）。 */
export function useSetMemberRole(projectKey: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ personId, role }: MemberRoleChange): Promise<Member> =>
      updateMember(projectKey, personId, { role }),
    onSuccess: async () => {
      // 我的角色與 myPermissions 可能跟著變（例如把自己改成專案經理），專案內容整份重新讀。
      // 只重讀專案本身（exact）：項目清單不受成員影響，不必重抓。
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.project(projectKey), exact: true }),
        queryClient.invalidateQueries({ queryKey: queryKeys.projects }),
      ]);
    },
  });
}

/** 專案只剩這一位 admin：不能移除，也不能換成別的角色（伺服器回 409 project.last_admin）。 */
export function isLastAdmin(members: readonly Member[], member: Member): boolean {
  return member.role === 'admin' && members.filter((candidate) => candidate.role === 'admin').length === 1;
}

/** 移除之後自己還看不看得到這個專案：組織管理員不是成員也看得到。 */
export function losesAccess(member: Member, user: ReturnType<typeof useCurrentUser>): boolean {
  return member.personId === user.personId && !isOrganizationAdmin(user);
}

function dropMember(queryClient: QueryClient, projectKey: string, personId: string): void {
  queryClient.setQueryData<ProjectDetail>(queryKeys.project(projectKey), (detail) =>
    detail === undefined ? detail : { ...detail, members: detail.members.filter((member) => member.personId !== personId) },
  );
}

/**
 * 把一個人移出專案。成功後先從快取的成員裡拿掉（那一列馬上不見），再重新讀專案內容（我的權限可能跟著變）。
 * 移除的是自己、而且不是組織管理員時，已經看不到這個專案：回到專案清單並說明，等專案頁卸下之後再丟掉這個專案的快取
 * （還掛著就丟，專案頁會再讀一次、拿到 404）。
 */
export function useRemoveMember(projectKey: string) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const user = useCurrentUser();
  return useMutation({
    mutationFn: (member: Member) => removeMember(projectKey, member.personId),
    onSuccess: async (_removed, member) => {
      dropMember(queryClient, projectKey, member.personId);
      if (!losesAccess(member, user)) {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: queryKeys.project(projectKey), exact: true }),
          queryClient.invalidateQueries({ queryKey: queryKeys.projects }),
        ]);
        return;
      }
      const name = queryClient.getQueryData<ProjectDetail>(queryKeys.project(projectKey))?.project.name ?? projectKey;
      queryClient.setQueryData<readonly Project[]>(queryKeys.projects, (projects) => projects?.filter((project) => project.key !== projectKey));
      const state: ArrivalState = { notice: `你已經不是「${name}」的成員，看不到這個專案了。` };
      await navigate('/projects', { state });
      removeQueriesWhenUnobserved(queryClient, queryKeys.project(projectKey));
      void queryClient.invalidateQueries({ queryKey: queryKeys.projects });
      void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
    },
  });
}
