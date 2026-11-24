import { useRef, useState } from 'react';
import type { FillRequest } from '../../item-table/cells/useFillSubmit';

/** 等著補填的移動：連同錯誤一起記著（改成在看板上方說明時用）。 */
export interface QueuedFill extends FillRequest {
  readonly error: unknown;
}

/**
 * 補填對話框的佇列：一次開一個，後面的排隊。最新的內容另外放在 ref：好幾個移動接連失敗時，
 * 各自的 onError 是建立那個移動時的版本（舊的閉包），要看同一份才不會互相蓋掉。
 */
export function useMoveFillQueue() {
  const [queue, setQueue] = useState<readonly QueuedFill[]>([]);
  const queueRef = useRef<readonly QueuedFill[]>([]);
  const replace = (next: readonly QueuedFill[]) => {
    queueRef.current = next;
    setQueue(next);
  };
  return {
    queue,
    /** 自己的補填對話框開著（或馬上要開）。 */
    isActive: () => queueRef.current.length > 0,
    add: (fill: QueuedFill) => {
      replace([...queueRef.current.filter((other) => other.item.key !== fill.item.key), fill]);
    },
    /** 關上前面那一個，後面的接著開。 */
    next: () => {
      replace(queueRef.current.slice(1));
    },
    /** 全部收起來，回傳還沒開的那些。 */
    clear: (): readonly QueuedFill[] => {
      const rest = queueRef.current.slice(1);
      replace([]);
      return rest;
    },
  };
}
