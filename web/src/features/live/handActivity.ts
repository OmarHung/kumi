import { useEffect } from 'react';

type Listener = () => void;

// 目前有幾件「手上有東西」的事在進行（拖曳中的卡片、打開著輸入的表格儲存格）：
// 即時更新的失效要等它們結束，不在手上的東西底下換資料。沒有上限（拖曳、編輯一定會結束）。
let activeCount = 0;
const listeners = new Set<Listener>();

function notify(): void {
  listeners.forEach((listener) => {
    listener();
  });
}

export function isHandBusy(): boolean {
  return activeCount > 0;
}

/** 開始、結束時被呼叫；回傳取消訂閱的函式。 */
export function subscribeToHandBusy(listener: Listener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** 拖曳中、儲存格開著輸入時呼叫（isActive）；結束或畫面被卸載時自動算結束。 */
export function useReportHandBusy(isActive: boolean): void {
  useEffect(() => {
    if (!isActive) return;
    activeCount += 1;
    notify();
    return () => {
      activeCount -= 1;
      notify();
    };
  }, [isActive]);
}
