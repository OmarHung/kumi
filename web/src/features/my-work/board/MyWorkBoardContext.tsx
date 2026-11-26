import { createContext, use, type ReactNode, type RefObject } from 'react';
import type { StateCategory } from '../../../api/blueprint';
import type { CalendarDate, Item, ProjectDetail } from '../../../api/types';
import type { MoveTarget } from '../../../domain/workflow';
import type { BoardMove } from '../../board/useBoardMoves';

/** 拖到某個類別、有好幾個走得到的狀態：在那張卡片旁邊開選單問要哪一個。id 讓同一張卡片再拖一次時重新打開。 */
export interface StatePicker {
  readonly id: number;
  readonly item: Item;
  readonly category: StateCategory;
  readonly targets: readonly MoveTarget[];
}

export interface MyWorkBoardContextValue {
  /** 讀到的專案內容（藍圖與權限），以專案代號為鍵；還沒讀到的卡片先寫類別、不能移動。 */
  readonly projects: ReadonlyMap<string, ProjectDetail>;
  readonly todayDate: CalendarDate;
  /** 看的是「我建立的」：負責人不一定是自己，卡片多寫出負責人（看「我負責的」時每一張都是自己，不寫）。 */
  readonly showAssignee: boolean;
  readonly pending: readonly BoardMove[];
  /** 剛被擋下來、還沒關掉說明的卡片。 */
  readonly failedKeys: ReadonlySet<string>;
  /** 最近一次移動成功的卡片，標出它的落點。 */
  readonly landedKey: string | null;
  /** 正在拖曳的卡片；拖曳中欄要標出走得到、走不到。 */
  readonly activeItem: Item | null;
  readonly picker: StatePicker | null;
  readonly focusAfterMoveRef: RefObject<string | null>;
  /** keepFocus：從選單移動的，焦點跟著卡片到新的欄。 */
  readonly move: (item: Item, to: string, keepFocus: boolean) => void;
}

const MyWorkBoardContext = createContext<MyWorkBoardContextValue | null>(null);

export function MyWorkBoardProvider({ value, children }: { readonly value: MyWorkBoardContextValue; readonly children: ReactNode }) {
  return <MyWorkBoardContext value={value}>{children}</MyWorkBoardContext>;
}

export function useMyWorkBoard(): MyWorkBoardContextValue {
  const value = use(MyWorkBoardContext);
  if (value === null) {
    throw new Error('useMyWorkBoard 只能用在「我的工作」的看板裡。');
  }
  return value;
}
