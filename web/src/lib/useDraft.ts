import { useState } from 'react';
import { readDraft, removeDraft, writeDraft } from './draftStorage';

export interface DraftOptions {
  /**
   * 值得留下來的內容（留言：不是只有空白；說明：和伺服器現在的值不同）。不值得的不存，存著的也拿掉；
   * 掛上時讀到的草稿不值得留下來，就當成沒有。
   */
  readonly isWorthKeeping: (value: string) => boolean;
  /**
   * 連同草稿記下它是在哪一個內容上打的（說明：當時伺服器上的說明），還原時拿來看別人是不是之後改過。
   * 有給才存成 JSON（{ text, base }）；讀到舊的純文字草稿時照樣還原，只是不知道當時的內容。
   * 沒給時照字面存：留言打的字就算長得像 JSON 也不會被拆開。
   */
  readonly withBase?: boolean;
}

export interface Draft {
  /** 掛上時還原的內容；沒有草稿是 null。之後打的字不會改變它。 */
  readonly restored: string | null;
  /** 還原的草稿是在哪一個內容上打的；沒有記下（沒用 withBase、舊的純文字草稿）是 undefined。 */
  readonly restoredBase: string | undefined;
  /** 還在顯示「已還原」的說明：還原之後，直到捨棄、送出、取消，或內容變得不值得留下來。 */
  readonly isRestored: boolean;
  /** 每次輸入都呼叫：值得留下來就存（withBase 時連同 base），否則拿掉。 */
  readonly save: (value: string, base?: string) => void;
  /** 送出成功、取消、捨棄：拿掉草稿，也不再說「已還原」。 */
  readonly clear: () => void;
}

interface StoredDraft {
  readonly text: string;
  readonly base: string | undefined;
}

function isStoredEntry(value: unknown): value is { readonly text: string; readonly base?: unknown } {
  return typeof value === 'object' && value !== null && 'text' in value && typeof value.text === 'string';
}

function parseDraft(raw: string, withBase: boolean): StoredDraft {
  if (!withBase) return { text: raw, base: undefined };
  try {
    const parsed: unknown = JSON.parse(raw);
    if (isStoredEntry(parsed)) return { text: parsed.text, base: typeof parsed.base === 'string' ? parsed.base : undefined };
  } catch {
    // 舊的純文字草稿（還沒記下當時的內容）：照字面還原。
  }
  return { text: raw, base: undefined };
}

function readStored(key: string, withBase: boolean, isWorthKeeping: (value: string) => boolean): StoredDraft | null {
  const raw = readDraft(key);
  if (raw === null) return null;
  const draft = parseDraft(raw, withBase);
  return isWorthKeeping(draft.text) ? draft : null;
}

/**
 * 某一個鍵（lib/draftStorage.ts 的 draftKey）的草稿。掛上時讀一次：呼叫端換了鍵（例如換了項目）要重新掛上，
 * 自己的輸入狀態才會跟著換。讀寫失敗當成沒有草稿（draftStorage 處理）。
 */
export function useDraft(key: string, { isWorthKeeping, withBase = false }: DraftOptions): Draft {
  const [stored] = useState(() => readStored(key, withBase, isWorthKeeping));
  const [isRestored, setIsRestored] = useState(stored !== null);

  function save(value: string, base?: string) {
    if (isWorthKeeping(value)) {
      writeDraft(key, withBase ? JSON.stringify({ text: value, base }) : value);
      return;
    }
    removeDraft(key);
    setIsRestored(false);
  }

  function clear() {
    removeDraft(key);
    setIsRestored(false);
  }

  return { restored: stored?.text ?? null, restoredBase: stored?.base, isRestored, save, clear };
}
