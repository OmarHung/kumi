import { useCallback, useLayoutEffect, useRef, type RefObject } from 'react';

/** 離最下面還有這麼近就算「在最下面」：差幾個像素不該讓人以為自己往上捲了。 */
const BOTTOM_TOLERANCE_PX = 48;

export interface StickToBottom {
  readonly scrollerRef: RefObject<HTMLDivElement | null>;
  readonly onScroll: () => void;
  /** 使用者送出訊息：不管剛才捲到哪裡，回到最下面跟著長。 */
  readonly pin: () => void;
}

/**
 * 聊天的捲動習慣：人在最下面時，內容長出來就跟著捲；往上捲了（看前面的內容）就不跟，不把人拉回去。
 * dependency 變了（長出新的文字、換了對話）時才檢查。換對話時 resetKey 不同，一律回到最下面。
 */
export function useStickToBottom(dependency: unknown, resetKey: string): StickToBottom {
  const scrollerRef = useRef<HTMLDivElement>(null);
  const isPinnedRef = useRef(true);

  const onScroll = useCallback(() => {
    const scroller = scrollerRef.current;
    if (scroller === null) return;
    isPinnedRef.current = scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight <= BOTTOM_TOLERANCE_PX;
  }, []);

  const pin = useCallback(() => {
    isPinnedRef.current = true;
  }, []);

  useLayoutEffect(() => {
    isPinnedRef.current = true;
  }, [resetKey]);

  useLayoutEffect(() => {
    const scroller = scrollerRef.current;
    if (scroller !== null && isPinnedRef.current) scroller.scrollTop = scroller.scrollHeight;
  }, [dependency, resetKey]);

  return { scrollerRef, onScroll, pin };
}
