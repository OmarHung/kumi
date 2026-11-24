import type { Active, Announcements, Over, ScreenReaderInstructions } from '@dnd-kit/core';
import { cardOf, type DropTarget } from './boardDnd';

/** 依負責人、欄位分欄時，欄帶的資料：拖曳中這張卡片放不進來時，blockedReason 是原因。 */
export interface GroupColumnData {
  readonly kind: 'group-column';
  readonly columnKey: string;
  readonly name: string;
  readonly blockedReason: string | null;
}

function isGroupColumnData(value: unknown): value is GroupColumnData {
  return typeof value === 'object' && value !== null && 'kind' in value && value.kind === 'group-column' && 'columnKey' in value;
}

export function groupColumnOf(over: Pick<Over, 'data'> | null): GroupColumnData | undefined {
  const data: unknown = over?.data.current;
  return isGroupColumnData(data) ? data : undefined;
}

/**
 * 放在哪一欄之後要不要修改。放回原欄、拖出看板不送；拖到看起來放不進的欄（類型沒有這個欄位）也不送：
 * 這是前端就知道的，和依狀態不同（那邊以後端的結果為準），不必讓使用者等一次必定失敗的請求。
 */
export function groupDropTarget(
  columnKeyOf: (item: NonNullable<ReturnType<typeof cardOf>>) => string,
  active: Pick<Active, 'data'>,
  over: Pick<Over, 'data'> | null,
): DropTarget | null {
  const item = cardOf(active);
  const column = groupColumnOf(over);
  if (item === undefined || column === undefined || column.blockedReason !== null || column.columnKey === columnKeyOf(item)) {
    return null;
  }
  return { item, to: column.columnKey };
}

function cardName(active: Pick<Active, 'data'>): string {
  const item = cardOf(active);
  return item === undefined ? '卡片' : `${item.key}「${item.title}」`;
}

/** 拖曳時唸給螢幕閱讀器聽的話；groupName 是「負責人」或欄位的名稱。 */
export function groupAnnouncements(groupName: string, isDropped: (active: Pick<Active, 'data'>, over: Pick<Over, 'data'> | null) => boolean): Announcements {
  return {
    onDragStart: ({ active }) => `開始拖曳 ${cardName(active)}。`,
    onDragOver: ({ active, over }) => {
      const column = groupColumnOf(over);
      if (column === undefined) {
        return `${cardName(active)} 不在任何一欄上。`;
      }
      const note = column.blockedReason === null ? '' : `，放不進去：${column.blockedReason}`;
      return `${cardName(active)} 在「${column.name}」欄上${note}。`;
    },
    // 真的要送出時回空字串（理由同 BOARD_ANNOUNCEMENTS）：送出中、成功、被擋下來由看板的 status 與 alert 唸。
    onDragEnd: ({ active, over }) => (isDropped(active, over) ? '' : `${cardName(active)} 放回原處，${groupName}沒有改。`),
    onDragCancel: ({ active }) => `取消拖曳，${cardName(active)} 放回原處。`,
  };
}

export function groupInstructions(groupName: string): ScreenReaderInstructions {
  return {
    draggable: `用滑鼠拖曳卡片，或用手指按住卡片一下再拖，放到別的${groupName}欄就會修改。用鍵盤時，請用卡片上的「移到…」選單。`,
  };
}
