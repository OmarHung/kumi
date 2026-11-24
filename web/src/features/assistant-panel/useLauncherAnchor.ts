import { useLayoutEffect } from 'react';
import type { LauncherPosition } from './launcherPosition';

/**
 * 把按鈕的位置放在根元素的 CSS 變數上（--launcher-right、--launcher-bottom）：位置要執行時才算得出來（使用者拖的），
 * 而且面板在 body 底下、不在按鈕裡面，要讀同一份才對得齊按鈕的右下角。夾回畫面、預設值都在 AssistantPanel.module.css。
 * 沒有位置（沒拖過）就不設，CSS 用預設的右下角。
 */
export function useLauncherAnchor(position: LauncherPosition | null): void {
  const right = position?.right;
  const bottom = position?.bottom;
  useLayoutEffect(() => {
    if (right === undefined || bottom === undefined) return undefined;
    const root = document.documentElement;
    root.style.setProperty('--launcher-right', `${String(right)}px`);
    root.style.setProperty('--launcher-bottom', `${String(bottom)}px`);
    return () => {
      root.style.removeProperty('--launcher-right');
      root.style.removeProperty('--launcher-bottom');
    };
  }, [bottom, right]);
}
