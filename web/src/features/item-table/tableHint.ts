/*
 * 表格的操作說明（表格的 aria-describedby，docs/DESIGN.md 8.3「清單的鍵盤操作」）：進到表格時唸一次，不在每一格重複。
 * 專案的表格、我的工作與成員的表格共用。不能改的人（沒有 item.update；我的工作是沒有任何專案能改狀態）不提編輯；
 * 欄寬可以調的表格（專案的表格、我的工作，7.1「欄寬」）多一句調寬的按鍵。
 */
const TABLE_HINT = '用方向鍵在格子之間移動，Enter 或 F2 編輯，Tab 離開表格。';
const READ_ONLY_TABLE_HINT = '用方向鍵在格子之間移動，Tab 離開表格。';
// 寫成「Shift 加左右方向鍵」而不是符號：報讀軟體唸得清楚。
const RESIZE_HINT = '在表頭按 Shift 加左右方向鍵調整欄寬，Shift 加 Enter 照內容調整。';

interface TableHintOptions {
  /** 欄寬可以調（表頭有調寬的把手）。 */
  readonly isResizable?: boolean;
}

export function tableHint(canEdit: boolean, { isResizable = false }: TableHintOptions = {}): string {
  const hint = canEdit ? TABLE_HINT : READ_ONLY_TABLE_HINT;
  return isResizable ? `${hint}${RESIZE_HINT}` : hint;
}
