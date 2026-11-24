import { useSyncExternalStore } from 'react';

/**
 * 說明、留言、專案說明的編輯方式：所見即所得（rich），或直接編輯 Markdown 原文（markdown）。
 * 個人的偏好，記在這個瀏覽器，所有用到編輯器的地方共用（習慣打原文的人不必每次再切一次）；預設所見即所得。
 * 寫法和 lib/itemPanelMode.ts 一樣。
 */
export type EditorMode = 'rich' | 'markdown';

export const EDITOR_MODES: readonly EditorMode[] = ['rich', 'markdown'];

export const DEFAULT_EDITOR_MODE: EditorMode = 'rich';

export const EDITOR_MODE_STORAGE_KEY = 'editor.mode';

function isEditorMode(value: unknown): value is EditorMode {
  return EDITOR_MODES.some((mode) => mode === value);
}

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成預設。 */
export function readEditorMode(): EditorMode {
  try {
    const stored = localStorage.getItem(EDITOR_MODE_STORAGE_KEY);
    return isEditorMode(stored) ? stored : DEFAULT_EDITOR_MODE;
  } catch {
    return DEFAULT_EDITOR_MODE;
  }
}

// 只有寫不進去時（私密瀏覽）才在記憶體裡暫存，讓這一頁照樣生效；寫得進去就以儲存為準（別的分頁改了會跟著變）。
let unsavedValue: EditorMode | null = null;
const listeners = new Set<() => void>();

function notify(): void {
  listeners.forEach((listener) => {
    listener();
  });
}

function currentEditorMode(): EditorMode {
  return unsavedValue ?? readEditorMode();
}

/** 寫不進去時這次照樣套用，下次載入回到預設：只是個人偏好，不值得打斷使用者。 */
export function setEditorMode(value: EditorMode): void {
  try {
    localStorage.setItem(EDITOR_MODE_STORAGE_KEY, value);
    unsavedValue = null;
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
    unsavedValue = value;
  }
  notify();
}

/** 只給測試用：清掉寫不進 localStorage 時的記憶體暫存，不讓它漏到下一個測試。 */
export function resetEditorModeForTests(): void {
  unsavedValue = null;
}

function subscribe(onChange: () => void): () => void {
  listeners.add(onChange);
  const onStorage = (event: StorageEvent) => {
    if (event.key === EDITOR_MODE_STORAGE_KEY || event.key === null) onChange();
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(onChange);
    window.removeEventListener('storage', onStorage);
  };
}

export function useEditorMode(): EditorMode {
  return useSyncExternalStore(subscribe, currentEditorMode, () => DEFAULT_EDITOR_MODE);
}
