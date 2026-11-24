import { useCallback, useEffect, useRef, type RefObject } from 'react';
import { useLocation } from 'react-router';
import { focusPageHeading } from '../../lib/pageTop';

/** 新的一頁最多等這麼久才出現標題（項目與專案要先讀到）；再久就不搶焦點了。 */
export const HEADING_WAIT_MS = 10_000;

function focusHeadingIn(root: HTMLElement): boolean {
  const heading = root.querySelector('h1');
  if (heading === null) return false;
  // 多數頁面的 h1 沒有 tabIndex（只有要用程式放焦點的頁面才給）：補上 -1，焦點放得進去，Tab 也不會停在它上面。
  if (!heading.hasAttribute('tabindex')) heading.tabIndex = -1;
  focusPageHeading(heading);
  return true;
}

/**
 * 換頁之後把焦點放到新頁面的標題（docs/DESIGN.md 8.3「焦點」）。標題還沒出現（載入中、lazy 載入）就等它出現；
 * 等的期間使用者按了鍵或點了東西就不搶焦點，等太久也放棄。回傳停止等待的函式。
 */
export function watchForHeading(root: HTMLElement, timeoutMs: number = HEADING_WAIT_MS): () => void {
  if (focusHeadingIn(root)) return () => undefined;
  const controller = new AbortController();
  const observer = new MutationObserver(() => {
    if (focusHeadingIn(root)) stop();
  });
  const timer = window.setTimeout(() => {
    stop();
  }, timeoutMs);
  function stop() {
    observer.disconnect();
    window.clearTimeout(timer);
    controller.abort();
  }
  observer.observe(root, { childList: true, subtree: true });
  document.addEventListener('keydown', stop, { capture: true, signal: controller.signal });
  document.addEventListener('pointerdown', stop, { capture: true, signal: controller.signal });
  return stop;
}

/**
 * 快速跳轉選了一頁之後用：呼叫回傳的函式，再換頁；下一次網址真的換了（新的瀏覽紀錄），就照 watchForHeading 把焦點放到標題。
 * 等待不跟著網址再換一次而取消：項目頁會把大小寫不同的編號換成正規的網址（replace），那一次也算同一趟。
 */
export function useFocusHeadingAfterNavigation(rootRef: RefObject<HTMLElement | null>): () => void {
  const { key } = useLocation();
  // 要求的時間：換頁被擋下（設定頁的離開提醒按了取消）時網址不會換，舊的要求不能留到之後不相干的換頁。
  const requestedAtRef = useRef<number | null>(null);
  const stopRef = useRef<(() => void) | null>(null);

  useEffect(() => {
    const root = rootRef.current;
    const requestedAt = requestedAtRef.current;
    requestedAtRef.current = null;
    if (requestedAt === null || root === null || Date.now() - requestedAt > HEADING_WAIT_MS) return;
    stopRef.current?.();
    stopRef.current = watchForHeading(root);
  }, [key, rootRef]);

  useEffect(
    () => () => {
      stopRef.current?.();
    },
    [],
  );

  return useCallback(() => {
    requestedAtRef.current = Date.now();
  }, []);
}
