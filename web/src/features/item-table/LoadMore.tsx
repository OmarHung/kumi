import { useEffectEvent, useLayoutEffect, useRef, type MouseEvent, type RefObject } from 'react';
import { errorMessage } from '../../api/http';
import type { useProjectItems } from '../../api/queries';
import type { Item, ItemPage } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { isFocusLost } from '../../lib/updateThenFocus';
import type { RovingGridHandle } from '../../lib/useRovingGrid';
import styles from './ItemTableView.module.css';

export type ItemsResult = ReturnType<typeof useProjectItems>;

/**
 * 載完最後一頁、「載入更多」跟著不見時焦點要去的那一列：最後一頁的第一個項目（各頁以編號去重，快速新增先放進來的
 * 項目會換位置，不能用「原本有幾個」算）；最後一頁是空的、或那一個不在清單裡，就是清單的最後一列。
 */
export function firstOfLastPage(
  data: { readonly pages: readonly ItemPage[] } | undefined,
  items: readonly Item[],
): string | undefined {
  const key = data?.pages.at(-1)?.items[0]?.key;
  return key !== undefined && items.some((item) => item.key === key) ? key : items.at(-1)?.key;
}

/** 表格的停駐點那一欄、新載入的第一列（窄螢幕的清單沒有表格就不做）。 */
export function focusLastPage(result: ItemsResult, items: readonly Item[], gridRef: RefObject<RovingGridHandle | null>): void {
  const key = firstOfLastPage(result.data, items);
  if (key !== undefined) gridRef.current?.focusRow(key);
}

interface LoadMoreProps {
  readonly result: ItemsResult;
  readonly count: number;
  /** 用鍵盤按了「載入更多」、載完最後一頁、按鈕跟著不見而焦點掉了。 */
  readonly onButtonGone: () => void;
}

/**
 * 按了「載入更多」之後：還有下一頁時按鈕留著，焦點也留在上面；最後一頁載完、按鈕不見時焦點不能掉回頁面本體，
 * 交給呼叫端（表格把它放到新載入的第一列）。只有用鍵盤按的才接手：按的時候按鈕有焦點、click 的 detail 是 0
 * （鍵盤觸發的 click 沒有點擊次數）。用滑鼠按的（Safari 點了按鈕也不會取得焦點）不把焦點拉進表格。
 * 記下按的時候有幾頁，頁數變了才算結束：中間怎麼重畫都不會把它清掉；載入失敗就不接手。
 */
function useLoadMoreFocus({ result, onButtonGone }: LoadMoreProps) {
  const pendingPages = useRef<number | null>(null);
  const handleGone = useEffectEvent(onButtonGone);
  const pageCount = result.data?.pages.length ?? 0;
  useLayoutEffect(() => {
    const from = pendingPages.current;
    if (from === null) return;
    if (result.isFetchNextPageError) {
      pendingPages.current = null;
      return;
    }
    if (pageCount === from) return;
    pendingPages.current = null;
    if (pageCount > from && !result.hasNextPage && isFocusLost()) handleGone();
  }, [pageCount, result.hasNextPage, result.isFetchNextPageError]);
  return (event: MouseEvent<HTMLButtonElement>) => {
    const isKeyboard = event.detail === 0 && event.currentTarget === document.activeElement;
    pendingPages.current = isKeyboard ? pageCount : null;
  };
}

/**
 * 表格或清單的最下面：已載入幾個、「載入更多」、載入更多失敗的說明。捲到最下面才看得到（7.1）。
 * 載入中標 aria-disabled（不用 disabled）：用鍵盤按的，焦點留在按鈕上（停用的按鈕會讓焦點掉到頁面本體）。
 */
export function LoadMore(props: LoadMoreProps) {
  const { result, count } = props;
  const markPending = useLoadMoreFocus(props);
  const summary = result.isPlaceholderData ? '正在更新結果…' : `已載入 ${String(count)} 個項目`;
  return (
    <div className={styles.footer}>
      <p role="status" className={styles.count}>
        {summary}
      </p>
      {result.hasNextPage && (
        <Button
          size="sm"
          // 重抓中（例如剛改的項目離開了條件）時，手上的游標可能已經對不上，等重抓完再往下讀。
          aria-disabled={result.isFetching || undefined}
          onClick={(event) => {
            if (result.isFetching) return;
            markPending(event);
            void result.fetchNextPage();
          }}
        >
          {result.isFetchingNextPage ? '載入中…' : '載入更多'}
        </Button>
      )}
      {result.isFetchNextPageError && (
        <p role="alert" className={styles.loadError}>
          載入更多失敗：{errorMessage(result.error)}
        </p>
      )}
    </div>
  );
}
