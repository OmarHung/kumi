import type { QueryClient } from '@tanstack/react-query';
import type { ActivityPages, ActivityView } from '../../api/activityPages';
import { queryKeys } from '../../api/queryKeys';
import type { Activity } from '../../api/types';

function withActivity(data: ActivityPages | undefined, activity: Activity): ActivityPages | undefined {
  const last = data?.pages.at(-1);
  if (data === undefined || last === undefined) {
    return data;
  }
  return { ...data, pages: [...data.pages.slice(0, -1), { ...last, items: [...last.items, activity] }] };
}

const VIEWS: readonly ActivityView[] = ['comments', 'all'];

/**
 * 新的活動（剛送出的留言）先接在已經載入的最後一頁後面，馬上看得到；再重抓一次，排到伺服器給的位置。
 * 留言兩份（只看留言、全部）都有，已經載入的兩份都先接上，切過去時不會少一則；沒載入過的那份不碰，之後讀會帶到。
 * 正在進行的重抓是送出之前發出的，結果裡沒有這則留言，回來會把它蓋掉，所以先取消、接上之後再重抓。
 * 重抓會讀到這則留言為止（activityPages.ts），之後載入的頁又帶到它時，畫面以編號去重。
 * 還沒載入過時間軸的話，只是重抓。
 */
export async function appendActivity(queryClient: QueryClient, itemKey: string, activity: Activity): Promise<void> {
  await queryClient.cancelQueries({ queryKey: queryKeys.activity(itemKey) });
  VIEWS.forEach((view) => {
    queryClient.setQueryData<ActivityPages>(queryKeys.activityView(itemKey, view), (data) => withActivity(data, activity));
  });
  void queryClient.invalidateQueries({ queryKey: queryKeys.activity(itemKey) });
}

function withReplaced(data: ActivityPages | undefined, activity: Activity): ActivityPages | undefined {
  if (data === undefined) return data;
  return { ...data, pages: data.pages.map((page) => ({ ...page, items: page.items.map((entry) => (entry.id === activity.id ? activity : entry)) })) };
}

/**
 * 編輯、刪除、復原成功之後：把兩份快取裡的那一筆（以編號找）換成伺服器回來的活動，位置不動；再重抓。
 * 自己做的事即時更新不會推回來（actorId，略過自己剛改的），要自己換。和 appendActivity 一樣先取消進行中的重抓：
 * 它是送出之前發出的，回來會把舊的內容蓋回去。已經載入的版本清單跟著活動的前綴一起失效。
 */
export async function replaceActivity(queryClient: QueryClient, itemKey: string, activity: Activity): Promise<void> {
  await queryClient.cancelQueries({ queryKey: queryKeys.activity(itemKey) });
  VIEWS.forEach((view) => {
    queryClient.setQueryData<ActivityPages>(queryKeys.activityView(itemKey, view), (data) => withReplaced(data, activity));
  });
  void queryClient.invalidateQueries({ queryKey: queryKeys.activity(itemKey) });
}
