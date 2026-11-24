import { useIsMutating, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getPageRestriction, setPageRestriction } from '../../../api/knowledge';
import { getProject } from '../../../api/projects';
import { queryKeys } from '../../../api/queryKeys';

export function useRestrictionQuery(spaceKey: string, pageNumber: number) {
  return useQuery({
    queryKey: queryKeys.knowledgePageRestriction(spaceKey, pageNumber),
    queryFn: ({ signal }) => getPageRestriction(spaceKey, pageNumber, signal),
    // 名單是要馬上拿來改的：對話框每次打開都讀最新的，不拿舊的快取當起點。
    gcTime: 0,
  });
}

/** 專案空間的專案成員（選人時排在前面）。讀不到就當作沒有：只是少了排序，不擋住選人。 */
export function useProjectMemberIds(projectKey: string | null): ReadonlySet<string> {
  const query = useQuery({
    queryKey: queryKeys.project(projectKey ?? ''),
    queryFn: ({ signal }) => getProject(projectKey ?? '', signal),
    enabled: projectKey !== null,
  });
  return new Set(query.data?.members.map((member) => member.personId));
}

const saveKey = (spaceKey: string, pageNumber: number) => ['knowledge', 'restriction-save', spaceKey, pageNumber];

/** 這一頁的名單正在存：外框的對話框要知道（儲存中不能關），但存的動作在裡面的表單。 */
export function useIsSavingRestriction(spaceKey: string, pageNumber: number): boolean {
  return useIsMutating({ mutationKey: saveKey(spaceKey, pageNumber) }) > 0;
}

/** 存名單（null 是拿掉限制）。存好讓整個空間失效：樹的鎖、頁面上那一行、子頁面的「繼承自」一起重抓。 */
export function useSaveRestriction(spaceKey: string, pageNumber: number) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: saveKey(spaceKey, pageNumber),
    mutationFn: (people: readonly string[] | null) => setPageRestriction(spaceKey, pageNumber, people),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeSpace(spaceKey) }),
  });
}
