import { useEffectEvent, useLayoutEffect, type RefObject } from 'react';
import { fitColumnWidths } from './columnFit';
import { clampWidth, useColumnWidthState, type ColumnWidthStore, type WidthBounds } from './columnWidths';

/*
 * 沒拉過的欄的預設寬度照內容量（docs/DESIGN.md 7.1「欄寬」）：第一次畫出來時量一次（就是點兩下把手的那套量法，
 * 夾在範圍裡），量完固定下來、只放在記憶體。之後載入更多、重抓都不重新量（版面不跳，放不下的值用省略號）；
 * 換了顯示的欄位、換專案（換了 store）、按「重設欄寬」時，沒拉過的欄重新量。
 *
 * 量在 useLayoutEffect：表格掛上、瀏覽器畫出來之前就量完，量出來的寬度在同一輪畫上去，不會先閃一下別的寬度。
 * 字型還沒載完時量的是備用字型的寬度，載完再量一次。
 */

/**
 * 照內容量的預設寬度最寬到這裡（rem）：原本表格裡的文字欄位最多 16rem 加上左右的內距（--space-3）。長的說明、網址
 * 不會把預設寬度撐到 40rem；要更寬自己拉，或點兩下把手照全部的內容調整（那時照一般的範圍）。
 */
const MEASURED_MAX = 17.5;

/** 要量的一欄：在第幾格（表頭與每一列的 cellIndex）、可以調的範圍。 */
export interface MeasuredColumn {
  readonly id: string;
  readonly index: number;
  readonly bounds: WidthBounds;
}

function measure(store: ColumnWidthStore, scope: HTMLElement | null, columns: readonly MeasuredColumn[]): void {
  if (scope === null) return;
  const { user } = store.getSnapshot();
  const targets = columns.filter((column) => user[column.id] === undefined);
  const widths = fitColumnWidths(
    scope,
    targets.map((column) => column.index),
  );
  // 量不到（沒有版面）的不記：用表格 CSS 的預設寬度。
  const measured = targets.flatMap((column, at) => {
    const width = widths[at] ?? 0;
    const bounds = { min: column.bounds.min, max: Math.min(column.bounds.max, MEASURED_MAX) };
    return width > 0 ? [[column.id, clampWidth(width, bounds)] as const] : [];
  });
  store.setMeasured(Object.fromEntries(measured));
}

/** 字型還在載入時，載完再呼叫一次；回傳取消的函式。 */
function afterFontsLoad(callback: () => void): (() => void) | undefined {
  // 沒有 document.fonts 的環境（jsdom）：透過只有這個屬性的型別讀，不必另外判斷型別。
  const page: { readonly fonts?: FontFaceSet } = document;
  const { fonts } = page;
  if (fonts === undefined || fonts.status === 'loaded') return undefined;
  const cancelled = new AbortController();
  void fonts.ready.then(() => {
    if (!cancelled.signal.aborted) callback();
  });
  return () => {
    cancelled.abort();
  };
}

/**
 * 表格裡這幾欄（沒拉過的）照內容量一次，寫進 store 的 measured。columns 換了（顯示的欄位、順序）或重設過時重新量。
 * 量的範圍從 anchorRef 往上找：給表格裡自己畫的元素（colgroup）——這個 hook 在表格裡面的元件裡跑，那時候表格自己的
 * ref 還沒接上（React 先接子元素的 ref），表格的元素倒是已經在 DOM 裡。
 */
export function useMeasuredWidths(
  store: ColumnWidthStore,
  anchorRef: RefObject<Element | null>,
  columns: readonly MeasuredColumn[],
): void {
  const { epoch } = useColumnWidthState(store);
  const columnsKey = columns.map((column) => `${column.id}@${String(column.index)}`).join('|');
  const run = useEffectEvent(() => {
    measure(store, anchorRef.current?.closest('table') ?? null, columns);
  });
  useLayoutEffect(() => {
    run();
    return afterFontsLoad(run);
  }, [store, columnsKey, epoch]);
}
