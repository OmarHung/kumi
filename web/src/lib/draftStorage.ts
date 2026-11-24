/**
 * 打到一半的多行輸入（留言、說明）存在這個分頁的 sessionStorage：換頁、重新整理之後還在，關掉分頁就沒了。
 * 不放 localStorage：共用電腦上別人開新的分頁也看得到。登出時由 clearAllDrafts 全部清掉。
 * 讀寫失敗（不能用、容量滿了、沒有 sessionStorage）一律當成沒有草稿：草稿只是方便，不能因為它讓畫面壞掉。
 */

/** 所有草稿的鍵都以這個開頭；登出時照它清。 */
export const DRAFT_KEY_PREFIX = 'draft.';

export type DraftKind = 'comment' | 'comment-edit' | 'description' | 'project-description';

/** replyTo 是留言的回覆框要回覆的那一則最上層留言（活動編號）：每一串各存各的，不和最上層的留言框混在一起。
 * 'comment-edit' 借這個位置放正在編輯的那一則留言的編號（每一則各存各的）。 */
export function draftKey(kind: DraftKind, itemKey: string, replyTo?: string): string {
  const base = `${DRAFT_KEY_PREFIX}${kind}.${itemKey}`;
  return replyTo === undefined ? base : `${base}.reply.${replyTo}`;
}

function storage(): Storage | null {
  try {
    // 測試或特殊環境裡可能根本沒有 sessionStorage；讀屬性本身也可能因為隱私設定丟例外。
    return typeof sessionStorage === 'undefined' ? null : sessionStorage;
  } catch {
    return null;
  }
}

export function readDraft(key: string): string | null {
  try {
    return storage()?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

export function writeDraft(key: string, value: string): void {
  try {
    storage()?.setItem(key, value);
  } catch {
    // 容量滿了：這一次沒存到，輸入框裡的內容照舊。
  }
}

export function removeDraft(key: string): void {
  try {
    storage()?.removeItem(key);
  } catch {
    // 拿不掉也沒關係：關掉分頁就沒了。
  }
}

function draftKeys(store: Storage): string[] {
  const keys: string[] = [];
  for (let index = 0; index < store.length; index += 1) {
    const key = store.key(index);
    if (key?.startsWith(DRAFT_KEY_PREFIX) === true) keys.push(key);
  }
  return keys;
}

/** 登出（包含工作階段失效）時呼叫：共用電腦上，下一個人不能看到上一個人打的字。 */
export function clearAllDrafts(): void {
  try {
    const store = storage();
    if (store === null) return;
    // 先收集再刪：邊刪邊數，索引會跟著移動。
    draftKeys(store).forEach((key) => {
      store.removeItem(key);
    });
  } catch {
    // 不能用就本來也沒存進去。
  }
}
