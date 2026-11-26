import { useState } from 'react';

/** 欄位 id 對到「顯示與否」。沒寫的欄位是顯示。 */
export type ColumnVisibility = Readonly<Record<string, boolean>>;

/** 預設不顯示的欄：週期與里程碑是規劃用的、建立者是追蹤自己開出去的項目用的，都不是每個人每天都要看。使用者打開或關上之後，以記下的為準。 */
const DEFAULT_HIDDEN: ColumnVisibility = { cycle: false, milestone: false, createdBy: false };

/**
 * 使用者自己關掉的欄（「隱藏了 N 欄」只數這些）。預設就不顯示的欄不算：有週期的專案一打開就掛著「隱藏了 2 欄」，
 * 那不是使用者做的事，還佔掉工具列的寬度（實測 1024 寬時把工具列擠成三列）。
 */
export function isHiddenByChoice(columnId: string, visibility: ColumnVisibility): boolean {
  return visibility[columnId] === false && DEFAULT_HIDDEN[columnId] !== false;
}

const STORAGE_PREFIX = 'item-table.columns.';

function isVisibility(value: unknown): value is ColumnVisibility {
  return (
    typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    Object.values(value).every((entry) => typeof entry === 'boolean')
  );
}

// localStorage 可能被停用（私密瀏覽、政策）或內容被改壞；讀不到就當成沒有設定，不影響表格。
function read(projectKey: string): ColumnVisibility {
  try {
    const raw = localStorage.getItem(STORAGE_PREFIX + projectKey);
    const parsed: unknown = raw === null ? {} : JSON.parse(raw);
    return { ...DEFAULT_HIDDEN, ...(isVisibility(parsed) ? parsed : {}) };
  } catch {
    return DEFAULT_HIDDEN;
  }
}

function write(projectKey: string, visibility: ColumnVisibility): void {
  try {
    localStorage.setItem(STORAGE_PREFIX + projectKey, JSON.stringify(visibility));
  } catch {
    // 存不了只是下次要重新選，不必打斷使用者。
  }
}

/** 表格顯示哪些欄，以專案為鍵記在這個瀏覽器。 */
export function useColumnVisibility(projectKey: string) {
  const [visibility, setVisibility] = useState<ColumnVisibility>(() => read(projectKey));

  function update(next: ColumnVisibility) {
    setVisibility(next);
    write(projectKey, next);
  }

  return [visibility, update] as const;
}
