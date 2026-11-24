import { useQuery } from '@tanstack/react-query';
import { getProject } from '../../api/projects';
import { queryKeys } from '../../api/queryKeys';
import type { ProjectDetail } from '../../api/types';

export interface MyWorkScope {
  /** 選了專案、而且讀到了它的內容；沒選或讀不到是 undefined。 */
  readonly detail: ProjectDetail | undefined;
  /** 可以送清單的請求了：沒選專案，或選了的專案已經讀完（成功或失敗）。 */
  readonly isReady: boolean;
}

/**
 * 選了專案時要讀那個專案的內容（類型、狀態的選項，清掉不在那個專案裡的條件），和 useProject 共用快取。
 * 內容還沒回來時清單先不送：用舊的條件送出去只會多一個沒有用的請求，還會閃一下「沒有符合條件的項目」。
 * 讀不到（沒有權限、不存在）不擋住清單：結果由伺服器說了算。
 */
export function useMyWorkScope(projectKey: string | null): MyWorkScope {
  const query = useQuery({
    queryKey: queryKeys.project(projectKey ?? ''),
    queryFn: ({ signal }) => getProject(projectKey ?? '', signal),
    enabled: projectKey !== null,
  });
  return { detail: projectKey === null ? undefined : query.data, isReady: projectKey === null || !query.isPending };
}
