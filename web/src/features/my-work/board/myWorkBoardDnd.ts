import type { Active, Announcements, Over } from '@dnd-kit/core';
import type { StateCategory } from '../../../api/blueprint';
import type { Item } from '../../../api/types';
import { STATE_CATEGORY_LABELS } from '../../../domain/labels';
import { cardOf } from '../../board/boardDnd';

/** 欄帶的資料：「我的工作」的欄是狀態類別（各專案的流程不同，沒有共同的狀態）。blockedReason：拖曳中走不到的原因。 */
export interface CategoryColumnData {
  readonly kind: 'category-column';
  readonly category: StateCategory;
  readonly blockedReason: string | null;
}

function isCategoryColumnData(value: unknown): value is CategoryColumnData {
  return typeof value === 'object' && value !== null && 'kind' in value && value.kind === 'category-column' && 'category' in value;
}

export function categoryColumnOf(over: Pick<Over, 'data'> | null): CategoryColumnData | undefined {
  const data: unknown = over?.data.current;
  return isCategoryColumnData(data) ? data : undefined;
}

export interface CategoryDrop {
  readonly item: Item;
  readonly category: StateCategory;
}

/** 放下之後要處理的：放在別的類別的欄上。拖出看板（不在任何一欄上）、放回原欄都不處理。 */
export function categoryDrop(active: Pick<Active, 'data'>, over: Pick<Over, 'data'> | null): CategoryDrop | null {
  const item = cardOf(active);
  const column = categoryColumnOf(over);
  if (item === undefined || column === undefined || column.category === item.stateCategory) return null;
  return { item, category: column.category };
}

function cardName(active: Pick<Active, 'data'>): string {
  const item = cardOf(active);
  return item === undefined ? '卡片' : `${item.key}「${item.title}」`;
}

/** 拖曳時唸給螢幕閱讀器聽的話（dnd-kit 預設是英文）。 */
export const MY_WORK_BOARD_ANNOUNCEMENTS: Announcements = {
  onDragStart: ({ active }) => `開始拖曳 ${cardName(active)}。`,
  onDragOver: ({ active, over }) => {
    const column = categoryColumnOf(over);
    if (column === undefined) return `${cardName(active)} 不在任何一欄上。`;
    const note = column.blockedReason === null ? '' : `，看起來走不到：${column.blockedReason}`;
    return `${cardName(active)} 在「${STATE_CATEGORY_LABELS[column.category]}」欄上${note}。`;
  },
  // 真的要處理時回空字串：dnd-kit 只略過 null，空字串才會清掉它唸的區域。送出中、成功、被擋下來、問要哪一個狀態
  // 都由看板自己的 status、alert 與選單唸。
  onDragEnd: ({ active, over }) => (categoryDrop(active, over) === null ? `${cardName(active)} 放回原處，狀態沒有改。` : ''),
  onDragCancel: ({ active }) => `取消拖曳，${cardName(active)} 放回原處。`,
};
