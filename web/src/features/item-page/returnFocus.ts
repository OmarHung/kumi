/**
 * 從清單進到整頁的項目頁、再用瀏覽器的上一頁回到清單時，焦點放回清單上那一筆（docs/DESIGN.md 8.1 第 5 點「回得去」）。
 * 鍵是清單那一筆瀏覽紀錄的 location.key（React Router 把它存在 history 的 state 裡，上一頁回去時是同一個值），
 * 值是項目編號。記在 sessionStorage：重新整理項目頁之後再上一頁也找得到；分頁關掉就沒了。讀寫失敗當成沒有。
 */
const STORAGE_KEY = 'item.returnFocus';

/** 只留最近的幾筆：一般只會往回走一兩層。 */
const MAX_ENTRIES = 20;

type Records = readonly (readonly [string, string])[];

function isRecord(value: unknown): value is readonly [string, string] {
  return Array.isArray(value) && value.length === 2 && value.every((part) => typeof part === 'string');
}

function readRecords(): Records {
  try {
    const parsed: unknown = JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter(isRecord) : [];
  } catch {
    return [];
  }
}

function writeRecords(records: Records): void {
  try {
    if (records.length === 0) {
      sessionStorage.removeItem(STORAGE_KEY);
    } else {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(records));
    }
  } catch {
    // 存不了只是回去時焦點不會自己放回那一筆，不影響畫面。
  }
}

/**
 * React Router 給一份文件的第一筆紀錄的 key 一律是 default：重新載入、另開分頁都是同一個值，
 * 拿它當鍵會把以前的記錄套到新的一頁上（一打開清單焦點就被拉走），所以不記也不放。
 */
const FIRST_ENTRY = 'default';

function isUsableEntry(entry: string): boolean {
  return entry !== '' && entry !== FIRST_ENTRY;
}

function fromEntryOf(state: unknown): string | null {
  if (typeof state !== 'object' || state === null || !('fromEntry' in state)) return null;
  return typeof state.fromEntry === 'string' && isUsableEntry(state.fromEntry) ? state.fromEntry : null;
}

/** 項目頁顯示一個從清單連進來的項目時呼叫：state 是連進來時帶的 ItemPageEntry。 */
export function rememberReturnFocus(state: unknown, itemKey: string): void {
  const entry = fromEntryOf(state);
  if (entry === null) return;
  const others = readRecords().filter(([key]) => key !== entry);
  writeRecords([...others, [entry, itemKey] as const].slice(-MAX_ENTRIES));
}

/** 回到這一筆紀錄時，要把焦點放回哪個項目；沒有記錄是 null。 */
export function peekReturnFocus(entry: string): string | null {
  if (!isUsableEntry(entry)) return null;
  return readRecords().find(([key]) => key === entry)?.[1] ?? null;
}

/** 焦點放好了就拿掉（開發模式下 effect 會跑兩次，所以不是讀了就拿掉）。 */
export function forgetReturnFocus(entry: string): void {
  const records = readRecords();
  const remaining = records.filter(([key]) => key !== entry);
  if (remaining.length !== records.length) writeRecords(remaining);
}
