import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { listProjectOverviews, type OverviewScope } from '../../api/overview';
import { queryKeys } from '../../api/queryKeys';

/**
 * 專案總覽。每次回到這一頁都重抓一次（refetchOnMount: 'always'）：離開去改了項目再回來，數字不能是 30 秒內的舊的；
 * 在這一頁停著時由即時更新讓它失效（features/live/liveInvalidation.ts）。換範圍時先顯示上一份，不閃成骨架。
 */
export function useProjectOverviews(scope: OverviewScope) {
  return useQuery({
    queryKey: queryKeys.overview(scope),
    queryFn: ({ signal }) => listProjectOverviews(scope, signal),
    refetchOnMount: 'always',
    placeholderData: keepPreviousData,
  });
}
