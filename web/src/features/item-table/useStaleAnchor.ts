import { useState } from 'react';
import { filtersToSearchParams, type ItemFilters } from '../../domain/itemFilters';

/**
 * 「停滯」的分界時間：條件換了（或這一頁剛開）才重新取一次「現在」，同一組條件下不再變。
 * 分界在查詢鍵裡；如果每分鐘跟著時鐘走，同一頁捲動、載入更多到一半查詢鍵就換了，已載入的頁面會整個丟掉重抓。
 * 清單與快速新增要用同一個值才對得上同一份快取，所以由表格狀態保管、往下傳。
 */
export function useStaleAnchor(filters: ItemFilters): Date {
  const key = filtersToSearchParams(filters).toString();
  const [anchor, setAnchor] = useState(() => ({ key, now: new Date() }));
  if (anchor.key === key) return anchor.now;
  // 在畫面重畫當中換掉：回傳的值就是存下的值，不會多一次用舊分界的畫面。
  const fresh = { key, now: new Date() };
  setAnchor(fresh);
  return fresh.now;
}
