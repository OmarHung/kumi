import { createContext, use, type ReactNode, type RefObject } from 'react';
import type { ItemTypeDefinition, Workflow } from '../../api/blueprint';
import type { Item } from '../../api/types';
import type { BoardGroup } from '../../domain/boardGroup';
import type { BoardFilters } from '../../domain/projectView';
import type { BoardMove } from './useBoardMoves';

export interface BoardContextValue {
  /** 依狀態分組時看的流程；依負責人、欄位分組時不分流程，是 null。 */
  readonly workflow: Workflow | null;
  /** 看板依什麼分欄：卡片的移動、選單與快速新增看這個。 */
  readonly group: BoardGroup;
  /** 用這個流程的類型（分組時是全部的類型）：每一欄只讀這些類型，快速新增也只能選這些。 */
  readonly types: readonly ItemTypeDefinition[];
  readonly filters: BoardFilters;
  readonly pending: readonly BoardMove[];
  /** 剛被擋下來、還沒關掉說明的卡片。 */
  readonly failedKeys: ReadonlySet<string>;
  /** 最近一次移動成功的卡片，標出它的落點。 */
  readonly landedKey: string | null;
  /** 正在拖曳的卡片；拖曳中欄要標出走得到、走不到。 */
  readonly activeItem: Item | null;
  readonly focusAfterMoveRef: RefObject<string | null>;
  /** to 是欄的 key（狀態、人員編號或選項的 key，「沒有…」是 `_none`）。keepFocus：從「移到…」選單移動的，焦點跟著卡片到新的欄。 */
  readonly move: (item: Item, to: string, keepFocus: boolean) => void;
}

const BoardContext = createContext<BoardContextValue | null>(null);

export function BoardProvider({ value, children }: { readonly value: BoardContextValue; readonly children: ReactNode }) {
  return <BoardContext value={value}>{children}</BoardContext>;
}

export function useBoard(): BoardContextValue {
  const value = use(BoardContext);
  if (value === null) {
    throw new Error('useBoard 只能用在看板裡。');
  }
  return value;
}
