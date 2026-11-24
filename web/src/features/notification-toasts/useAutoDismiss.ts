import { useEffect, useRef, useState, type FocusEvent } from 'react';

/** 提示自己消失之前停多久（毫秒）。 */
export const TOAST_DISMISS_MS = 8000;

export interface PauseHandlers {
  readonly onPointerEnter: () => void;
  readonly onPointerLeave: () => void;
  readonly onFocus: () => void;
  readonly onBlur: (event: FocusEvent<HTMLElement>) => void;
}

/**
 * 停夠時間就呼叫 onExpire；滑鼠移上去或焦點在裡面時暫停，離開之後接著算剩下的時間（不是重新數）。
 * 回傳的事件處理器放在整則提示的外框上。
 */
export function useAutoDismiss(onExpire: () => void, ms = TOAST_DISMISS_MS): PauseHandlers {
  const [isHovered, setIsHovered] = useState(false);
  const [isFocused, setIsFocused] = useState(false);
  const isPaused = isHovered || isFocused;
  const remaining = useRef(ms);
  const latest = useRef(onExpire);
  useEffect(() => {
    latest.current = onExpire;
  });
  useEffect(() => {
    if (isPaused) return undefined;
    const startedAt = Date.now();
    const timer = setTimeout(() => {
      latest.current();
    }, remaining.current);
    return () => {
      clearTimeout(timer);
      remaining.current = Math.max(0, remaining.current - (Date.now() - startedAt));
    };
  }, [isPaused]);
  return {
    onPointerEnter: () => {
      setIsHovered(true);
    },
    onPointerLeave: () => {
      setIsHovered(false);
    },
    onFocus: () => {
      setIsFocused(true);
    },
    // 焦點移到這則提示裡的別的地方（連結到關閉按鈕）不算離開。
    onBlur: (event) => {
      if (!event.currentTarget.contains(event.relatedTarget)) setIsFocused(false);
    },
  };
}
