import { useEffect, useRef, useState, type RefObject } from 'react';
import { useLocation } from 'react-router';
import type { Activity } from '../../api/types';
import { prefersReducedMotion } from '../../lib/prefersReducedMotion';
import { panelItemOf } from '../work-items/itemLinks';
import { commentIdOfHash, HIGHLIGHT_MS, ownsCommentTarget } from './commentTarget';
import { useIsInItemPanel } from './ItemPanelContext';
import { scrollTargetIntoView } from './scrollToTarget';

/** 活動分頁的查詢結果裡這個 hook 要用的部分。 */
interface PagedActivity {
  readonly hasNextPage: boolean;
  readonly isFetchingNextPage: boolean;
  readonly isFetchNextPageError: boolean;
  readonly fetchNextPage: () => unknown;
}

interface CommentTargetOptions {
  readonly itemKey: string;
  readonly entries: readonly Activity[];
  readonly result: PagedActivity;
  readonly listRef: RefObject<HTMLOListElement | null>;
}

/**
 * 同一頁可能有兩個時間軸（項目頁加面板），只有「該回應的那一個」去找：
 * 面板裡的看網址的 item 參數是不是自己的項目；整頁的在面板沒開時才找（面板開著，hash 指的是面板裡的項目）。
 * 不靠「找得到才動」判斷：目標不在的那一個會一頁一頁往下載，那是浪費請求。
 */
function useIsTargetOwner(itemKey: string): boolean {
  const { search } = useLocation();
  const isInPanel = useIsInItemPanel();
  const panelKey = panelItemOf(new URLSearchParams(search));
  return ownsCommentTarget(isInPanel, panelKey, itemKey);
}

function scrollToEntry(list: HTMLOListElement | null, id: string): HTMLElement | null {
  // 回覆在它那一串的清單裡，不是時間軸的直接子層：找所有帶著活動編號的元素。
  const entry = Array.from(list?.querySelectorAll<HTMLElement>('[data-activity-id]') ?? []).find((candidate) => candidate.dataset.activityId === id);
  if (entry === undefined) return null;
  // 減少動態效果時不平滑捲。
  scrollTargetIntoView(entry, prefersReducedMotion() ? 'auto' : 'smooth');
  return entry;
}

/** 標出來的狀態：phase 每次導航在 a、b 之間交替，CSS 換一個動畫名字，動畫才會重新開始（不必重新掛載）。 */
export type FlashPhase = 'a' | 'b';

export interface CommentFlash {
  readonly id: string;
  readonly phase: FlashPhase;
}

interface FlashState {
  readonly id: string;
  readonly round: number;
}

/**
 * 網址的 hash 是 #comment-<活動編號> 時：目標還沒載入就一頁一頁往後載，找到之後捲過去並標出來一陣子。
 * 回傳要標出來的那一筆（沒有是 null）。一次導航只做一次：用 location.key 加目標編號判斷，
 * 重抓、新留言進來都不會再捲；再點一次同一則通知（replace 換 key）才會再來一次。
 * 計時器放在只看 flash 的 effect 裡、自己清：StrictMode 把 effect 掛上、清理、再掛上時才不會丟掉計時器。
 */
export function useCommentTarget({ itemKey, entries, result, listRef }: CommentTargetOptions): CommentFlash | null {
  const location = useLocation();
  const isOwner = useIsTargetOwner(itemKey);
  const targetId = isOwner ? commentIdOfHash(location.hash) : null;
  const navigationKey = `${location.key}:${targetId ?? ''}`;
  const handledKey = useRef<string | null>(null);
  const [flash, setFlash] = useState<FlashState | null>(null);
  const isLoaded = targetId !== null && entries.some((entry) => entry.id === targetId);
  const { hasNextPage, isFetchingNextPage, isFetchNextPageError, fetchNextPage } = result;
  const shouldFetchMore = targetId !== null && !isLoaded && hasNextPage && !isFetchingNextPage && !isFetchNextPageError;

  useEffect(() => {
    if (shouldFetchMore) void fetchNextPage();
  }, [shouldFetchMore, fetchNextPage]);

  useEffect(() => {
    if (targetId === null || !isLoaded || handledKey.current === navigationKey) return;
    if (scrollToEntry(listRef.current, targetId) === null) return;
    handledKey.current = navigationKey;
    setFlash((previous) => ({ id: targetId, round: (previous?.round ?? 0) + 1 }));
  }, [targetId, isLoaded, navigationKey, listRef]);

  useEffect(() => {
    if (flash === null) return;
    const timer = window.setTimeout(() => {
      setFlash(null);
    }, HIGHLIGHT_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [flash]);

  // hash 不見或換了目標就立刻不標，不必等計時器。
  if (flash === null || flash.id !== targetId) return null;
  return { id: flash.id, phase: flash.round % 2 === 0 ? 'a' : 'b' };
}
