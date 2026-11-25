import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { purgeTrash } from '../../../api/knowledge';
import type { KnowledgeTrashPurge } from '../../../api/knowledgeTypes';
import { queryKeys } from '../../../api/queryKeys';

/** 要永久刪除什麼：一批（有 deletionId），或所有刪除超過 30 天的（deletionId 是 null）。 */
export interface PurgeTarget {
  readonly deletionId: string | null;
  /** 對話框的標題與說明用：一批時是那一批最上面那一頁的標題；全部到期的是 null。 */
  readonly title: string | null;
  /** 一批時是那一批的頁數；全部到期的是 null（要送出才知道）。 */
  readonly pageCount: number | null;
}

/** 一次永久刪除的結果，連同當時是指定一批還是清到期的（沒刪到東西時的說法不同）。 */
export interface PurgeOutcome {
  readonly purged: KnowledgeTrashPurge;
  readonly isSingleBatch: boolean;
}

/** 永久刪除的開啟、送出與結果：成功之後讓垃圾桶重抓，結果留在清單上面。 */
export function useTrashPurge(spaceKey: string) {
  const queryClient = useQueryClient();
  const [target, setTarget] = useState<PurgeTarget | null>(null);
  const [result, setResult] = useState<PurgeOutcome | null>(null);
  const mutation = useMutation({
    mutationFn: (purging: PurgeTarget) => purgeTrash(spaceKey, purging.deletionId),
    onSuccess: async (purged, purging) => {
      setResult({ purged, isSingleBatch: purging.deletionId !== null });
      setTarget(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeTrash(spaceKey) });
    },
  });
  return {
    target,
    result,
    mutation,
    open: (next: PurgeTarget) => {
      mutation.reset();
      setResult(null);
      setTarget(next);
    },
    close: () => {
      mutation.reset();
      setTarget(null);
    },
  };
}

export type TrashPurge = ReturnType<typeof useTrashPurge>;
