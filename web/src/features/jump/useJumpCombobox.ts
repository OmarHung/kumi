import { useState, type KeyboardEvent } from 'react';
import { isComposingKey } from '../../lib/keyboard';
import type { JumpOption } from './jumpGroups';

export interface JumpCombobox {
  /** 目前選起來的那一個在攤平的選項裡第幾個；沒有選項是 -1。 */
  readonly activeIndex: number;
  /** 滑鼠移到某一個上面時用。 */
  readonly activate: (key: string) => void;
  /** 輸入的字變了：回到第一個。 */
  readonly reset: () => void;
  readonly handleKeyDown: (event: KeyboardEvent<HTMLInputElement>) => void;
}

/**
 * 快速跳轉的 combobox（ARIA 的 combobox 加 listbox）：焦點一直在輸入框，選起來的那一個用 aria-activedescendant 表示。
 * ↑／↓ 移動（到頭尾繞回另一端）、Enter 前往；Esc 交給 Dialog 關上。
 * 記的是選項的 key 不是位置：伺服器的搜尋結果晚一點才接在最後面，選起來的那一個不會被換掉。
 */
export function useJumpCombobox(options: readonly JumpOption[], onChoose: (option: JumpOption) => void): JumpCombobox {
  const [activeKey, setActiveKey] = useState<string | null>(null);
  const activeIndex = options.length === 0 ? -1 : Math.max(0, options.findIndex((option) => option.key === activeKey));

  function moveBy(delta: number) {
    const next = options[(activeIndex + delta + options.length) % options.length];
    if (next !== undefined) setActiveKey(next.key);
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (isComposingKey(event)) {
      // 輸入法選字中的按鍵是輸入法的：Enter 是選字、方向鍵是換候選字、Esc 是取消選字（Dialog 收到 Esc 會關上，不讓它往外傳）。
      if (event.key === 'Escape') event.stopPropagation();
      return;
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      moveBy(event.key === 'ArrowDown' ? 1 : -1);
      return;
    }
    const active = options[activeIndex];
    if (event.key === 'Enter' && active !== undefined) {
      event.preventDefault();
      onChoose(active);
    }
  }

  return {
    activeIndex,
    activate: setActiveKey,
    reset: () => {
      setActiveKey(null);
    },
    handleKeyDown,
  };
}
