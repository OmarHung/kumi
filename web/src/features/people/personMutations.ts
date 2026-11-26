import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { disablePerson, enablePerson, resetPersonPassword, setPersonProfile, setPersonRole } from '../../api/people';
import { isApiError } from '../../api/http';
import { queryKeys } from '../../api/queryKeys';
import type { OrganizationRole, Person, SetPersonProfileRequest } from '../../api/types';

/** 伺服器回的那個人換掉清單裡的他（停用之後 disabled 變了）：不必整份重抓。 */
function replaceInPeople(queryClient: QueryClient, person: Person): void {
  queryClient.setQueryData<readonly Person[]>(queryKeys.people, (people) =>
    people?.map((candidate) => (candidate.personId === person.personId ? person : candidate)),
  );
}

/**
 * 失敗時重抓人員清單：被擋下來多半是畫面上的資料舊了（他被刪掉、角色被別人改了），重抓之後入口跟著對。
 * 不等它：錯誤要先顯示出來。
 */
function refetchPeople(queryClient: QueryClient): void {
  void queryClient.invalidateQueries({ queryKey: queryKeys.people });
}

export interface SetDisabledRequest {
  readonly personId: string;
  readonly disabled: boolean;
}

/** 停用或重新啟用帳號。成功時清單裡的他換成伺服器回的樣子。 */
export function useSetPersonDisabled() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ personId, disabled }: SetDisabledRequest) => (disabled ? disablePerson(personId) : enablePerson(personId)),
    onSuccess: (person) => {
      replaceInPeople(queryClient, person);
    },
    onError: () => {
      refetchPeople(queryClient);
    },
  });
}

export interface ResetPasswordVariables {
  readonly personId: string;
  readonly newPassword: string;
}

/** 替別人設一組新密碼。他在每個裝置上的登入都作廢；清單上看不出差別，照樣換成伺服器回的樣子。 */
export function useResetPersonPassword() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ personId, newPassword }: ResetPasswordVariables) => resetPersonPassword(personId, { newPassword }),
    onSuccess: (person) => {
      replaceInPeople(queryClient, person);
    },
    onError: () => {
      refetchPeople(queryClient);
    },
  });
}

export interface SetRoleVariables {
  readonly personId: string;
  readonly role: OrganizationRole;
}

/** 變更組織角色。成功時清單裡的他換成伺服器回的樣子（不必整份重抓）；被擋下來時重抓，畫面上的角色多半舊了。 */
export function useSetPersonRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ personId, role }: SetRoleVariables) => setPersonRole(personId, { role }),
    onSuccess: (person) => {
      replaceInPeople(queryClient, person);
    },
    onError: (error) => {
      refetchPeople(queryClient);
      // 403：登入的人自己的組織角色多半已經變了（被降級），選單的選項要跟著變，所以他自己的資料也重抓。
      if (isApiError(error) && error.code === 'person.forbidden') {
        void queryClient.invalidateQueries({ queryKey: queryKeys.session });
      }
    },
  });
}

export interface SetProfileVariables extends SetPersonProfileRequest {
  readonly personId: string;
}

/** 設定職稱與負責範圍。成功時清單裡的他換成伺服器回的樣子；被擋下來時重抓，畫面上的資料多半舊了。 */
export function useSetPersonProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ personId, ...profile }: SetProfileVariables) => setPersonProfile(personId, profile),
    onSuccess: (person) => {
      replaceInPeople(queryClient, person);
    },
    onError: () => {
      refetchPeople(queryClient);
    },
  });
}
