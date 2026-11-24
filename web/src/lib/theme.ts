import { useCallback, useEffect, useState } from 'react';

/** 主題：跟著系統、淺色、深色。手動選的記在這個瀏覽器，套到 <html data-theme>（tokens.css 依它換 color-scheme）。 */
export type ThemeChoice = 'system' | 'light' | 'dark';

export const THEME_CHOICES: readonly ThemeChoice[] = ['system', 'light', 'dark'];

export const THEME_STORAGE_KEY = 'shell.theme';

function isThemeChoice(value: unknown): value is ThemeChoice {
  return THEME_CHOICES.some((choice) => choice === value);
}

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成跟著系統。 */
export function readTheme(): ThemeChoice {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    return isThemeChoice(stored) ? stored : 'system';
  } catch {
    return 'system';
  }
}

/** 跟著系統就拿掉設定。寫不進去時這次照樣套用，下次載入回到跟著系統：只是個人偏好，不值得打斷使用者。 */
export function saveTheme(choice: ThemeChoice): void {
  try {
    if (choice === 'system') {
      localStorage.removeItem(THEME_STORAGE_KEY);
    } else {
      localStorage.setItem(THEME_STORAGE_KEY, choice);
    }
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
  }
}

export function applyTheme(choice: ThemeChoice, root: HTMLElement = document.documentElement): void {
  if (choice === 'system') {
    root.removeAttribute('data-theme');
  } else {
    root.setAttribute('data-theme', choice);
  }
}

/**
 * 目前選的主題與換主題的函式：換了就記住。<html data-theme> 跟著狀態走（掛上時也套一次：
 * 載入時的初始化腳本還沒有，或讀到的跟畫面上的不一樣時，以記住的為準）。
 */
export function useThemeChoice(): readonly [ThemeChoice, (choice: ThemeChoice) => void] {
  const [choice, setChoice] = useState<ThemeChoice>(readTheme);
  useEffect(() => {
    applyTheme(choice);
  }, [choice]);
  const choose = useCallback((next: ThemeChoice) => {
    saveTheme(next);
    setChoice(next);
  }, []);
  return [choice, choose];
}
