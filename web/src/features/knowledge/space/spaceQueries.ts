import { useQuery } from '@tanstack/react-query';
import { getPage, getPageTree } from '../../../api/knowledge';
import { queryKeys } from '../../../api/queryKeys';

/** 空間與它的頁面樹（一次讀完：空間的權限、封存狀態與樹放在同一份回應）。 */
export function useSpaceTree(spaceKey: string) {
  return useQuery({
    queryKey: queryKeys.knowledgeTree(spaceKey),
    queryFn: ({ signal }) => getPageTree(spaceKey, signal),
  });
}

/** 一頁的內容；pageNumber 是 null（網址不合格、空間首頁）時不讀。 */
export function useKnowledgePage(spaceKey: string, pageNumber: number | null) {
  return useQuery({
    queryKey: queryKeys.knowledgePage(spaceKey, pageNumber ?? 0),
    queryFn: ({ signal }) => getPage(spaceKey, pageNumber ?? 0, signal),
    enabled: pageNumber !== null,
  });
}
