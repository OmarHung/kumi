import { useQuery } from '@tanstack/react-query';
import { getSpace, listSpaces } from '../../api/knowledge';
import type { KnowledgeSpace } from '../../api/knowledgeTypes';
import { queryKeys } from '../../api/queryKeys';

/** 看得到的空間（空間清單、專案頁列找自己的空間、新增空間的對話框判斷哪些專案已經有了）共用同一份。 */
export function useKnowledgeSpaces() {
  return useQuery({
    queryKey: queryKeys.knowledgeSpaces,
    queryFn: ({ signal }) => listSpaces(signal),
  });
}

/** 一個空間（頁面樹、垃圾桶、設定都從它拿 myPermissions）；找不到或看不到是 404。 */
export function useKnowledgeSpace(spaceKey: string) {
  return useQuery({
    queryKey: queryKeys.knowledgeSpace(spaceKey),
    queryFn: ({ signal }) => getSpace(spaceKey, signal),
  });
}

/**
 * 綁在這個專案上的空間；沒有或讀不到都是 undefined（頁列只是不顯示「知識庫」，不為了它擋住專案頁）。
 */
export function useProjectKnowledgeSpace(projectKey: string): KnowledgeSpace | undefined {
  const spaces = useKnowledgeSpaces();
  return spaces.data?.find((space) => space.project?.key === projectKey);
}
