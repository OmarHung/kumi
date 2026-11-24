import { useCallback, type FocusEvent, type RefObject } from 'react';
import { useCloseOnOutsidePointer } from '../../lib/useCloseOnOutsidePointer';

/**
 * 選單與浮層共用的「什麼時候自己關上」（Esc 各自處理，因為焦點要回到按鈕）：
 * - 點在外面（rootRef 包住按鈕與浮層；浮層在按鈕旁邊的 DOM 裡，點在浮層裡不算外面）。
 * - 焦點移到外面的元素（例如報讀軟體跳走、程式把焦點移走）：只關上，不搬動焦點。
 *   relatedTarget 是 null（焦點只是離開視窗、切到別的程式）不算，回來時浮層還在。
 * - 按鈕被捲出看得到的範圍（Floating 的 onAnchorHidden）：浮層不該留在原處蓋住別的東西。
 *   焦點在浮層裡的話放回按鈕，不捲動畫面，鍵盤使用者才不會掉到頁面本體。呼叫端要分得出這一種時給 closeHidden
 *   （下拉的格子用它把焦點放回格子），沒給就和其他兩種一樣用 close。
 * triggerRef 是打開浮層的元素（按鈕，或日期欄位的輸入框）。close、closeHidden 要是固定的函式（useCallback）。
 */
export function useFloatingDismiss(
  isOpen: boolean,
  close: () => void,
  triggerRef: RefObject<HTMLElement | null>,
  closeHidden: () => void = close,
) {
  const rootRef = useCloseOnOutsidePointer<HTMLDivElement>(isOpen, close);
  const handleBlur = useCallback(
    (event: FocusEvent<HTMLElement>) => {
      const next = event.relatedTarget;
      if (next instanceof Node && !event.currentTarget.contains(next)) {
        close();
      }
    },
    [close],
  );
  const closeWhenAnchorHidden = useCallback(() => {
    if (rootRef.current?.contains(document.activeElement) === true) {
      triggerRef.current?.focus({ preventScroll: true });
    }
    closeHidden();
  }, [closeHidden, rootRef, triggerRef]);
  return { rootRef, handleBlur, closeWhenAnchorHidden };
}
