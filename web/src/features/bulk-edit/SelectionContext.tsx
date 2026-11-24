import { createContext, use, type ReactNode } from 'react';
import type { ItemSelection } from './useItemSelection';

const SelectionContext = createContext<ItemSelection | null>(null);

/** 表格的格子由 TanStack Table 渲染：選取的狀態用 Context 傳給勾選框與列。 */
export function SelectionProvider({ value, children }: { readonly value: ItemSelection; readonly children: ReactNode }) {
  return <SelectionContext value={value}>{children}</SelectionContext>;
}

/** 不在可以多選的表格裡（沒有提供）是 null。 */
export function useSelection(): ItemSelection | null {
  return use(SelectionContext);
}
