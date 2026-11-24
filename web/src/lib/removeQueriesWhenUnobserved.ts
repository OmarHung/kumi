import type { QueryClient, QueryKey } from '@tanstack/react-query';

/**
 * 丟掉這個前綴底下的每一份查詢，但要等到沒有元件在看（換頁之後，舊的頁面卸下）才丟。
 * navigate 回來的時候舊的頁面可能還掛著：這時丟掉，它下一次渲染會重建一份、再讀一次，
 * 讀一個已經看不到的專案只會拿到 404。先取消正在讀的，讀回來的舊資料才不會又寫進快取。
 */
export function removeQueriesWhenUnobserved(queryClient: QueryClient, queryKey: QueryKey): void {
  const cache = queryClient.getQueryCache();
  const isObserved = () => cache.findAll({ queryKey }).some((query) => query.getObserversCount() > 0);
  const remove = () => {
    void queryClient.cancelQueries({ queryKey });
    queryClient.removeQueries({ queryKey });
  };
  if (!isObserved()) {
    remove();
    return;
  }
  const unsubscribe = cache.subscribe((event) => {
    if (event.type !== 'observerRemoved' || isObserved()) return;
    unsubscribe();
    remove();
  });
}
