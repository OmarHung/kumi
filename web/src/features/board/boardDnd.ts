import {
  pointerWithin,
  PointerSensor,
  TouchSensor,
  useSensor,
  useSensors,
  type Active,
  type Announcements,
  type AutoScrollOptions,
  type Collision,
  type CollisionDetection,
  type DropAnimation,
  type Over,
  type PointerSensorOptions,
  type ScreenReaderInstructions,
  type TouchSensorOptions,
} from '@dnd-kit/core';
import type { PointerEvent as ReactPointerEvent } from 'react';
import type { WorkflowState } from '../../api/blueprint';
import type { Item } from '../../api/types';

/** 滑鼠與觸控筆：移動 6px 以上才開始拖。單純點一下卡片上的連結或按鈕不會變成拖曳。 */
const POINTER_SENSOR_OPTIONS: PointerSensorOptions = { activationConstraint: { distance: 6 } };

/** 手指：按住 250ms、期間移動不超過 8px 才開始拖。一碰就滑是捲動，不會被拖曳擋住。 */
const TOUCH_SENSOR_OPTIONS: TouchSensorOptions = { activationConstraint: { delay: 250, tolerance: 8 } };

/**
 * Pointer 感應器只接滑鼠與觸控筆。手指的 pointerdown 也會先到這裡，而 dnd-kit 一次只讓一個感應器接手：
 * 不擋掉的話，手指會用「移動一點就開始拖」的規則，捲動看板時就變成拖曳，Touch 感應器的按住延遲也沒有機會用上。
 */
export class CardPointerSensor extends PointerSensor {
  static override activators = PointerSensor.activators.map((activator) => ({
    ...activator,
    handler: (event: ReactPointerEvent, options: PointerSensorOptions) =>
      event.nativeEvent.pointerType !== 'touch' && activator.handler(event, options),
  }));
}

export function useBoardSensors() {
  return useSensors(useSensor(CardPointerSensor, POINTER_SENSOR_OPTIONS), useSensor(TouchSensor, TOUCH_SENSOR_OPTIONS));
}

/** 拖曳中的卡片帶的資料。 */
export interface CardData {
  readonly kind: 'card';
  readonly item: Item;
}

/** 欄帶的資料：拖曳中依藍圖與自己的角色判斷走不到時，blockedReason 是原因。 */
export interface ColumnData {
  readonly kind: 'column';
  readonly state: WorkflowState;
  readonly blockedReason: string | null;
}

function isCardData(value: unknown): value is CardData {
  return typeof value === 'object' && value !== null && 'kind' in value && value.kind === 'card' && 'item' in value;
}

function isColumnData(value: unknown): value is ColumnData {
  return typeof value === 'object' && value !== null && 'kind' in value && value.kind === 'column' && 'state' in value;
}

export function cardOf(active: Pick<Active, 'data'>): Item | undefined {
  const data: unknown = active.data.current;
  return isCardData(data) ? data.item : undefined;
}

export function columnOf(over: Pick<Over, 'data'> | null): ColumnData | undefined {
  const data: unknown = over?.data.current;
  return isColumnData(data) ? data : undefined;
}

/**
 * 放在哪一欄：指標要在看板（欄的捲動容器）看得到的範圍裡，再用 pointerWithin 找它在哪一欄上。
 * dnd-kit 量的是欄沒有被裁切的位置：看板橫向捲過之後，捲出去的欄還「在」側欄底下，不先擋掉就會放進看不見的欄。
 * 指標在可見範圍內又在某一欄上，就是在那一欄看得到的部分上。
 */
export function pointerWithinVisibleLanes(lanes: Element | null, args: Parameters<CollisionDetection>[0]): Collision[] {
  const pointer = args.pointerCoordinates;
  if (lanes === null || pointer === null) {
    return [];
  }
  const visible = lanes.getBoundingClientRect();
  const isInside = pointer.x >= visible.left && pointer.x <= visible.right && pointer.y >= visible.top && pointer.y <= visible.bottom;
  return isInside ? pointerWithin(args) : [];
}

export interface DropTarget {
  readonly item: Item;
  readonly to: string;
}

/**
 * 放下之後要不要改狀態。拖出看板（沒有在任何一欄上）、放回原欄（同一欄）都不送請求；
 * 拖到看起來走不到的欄照樣送，最後以後端的結果與說明為準。
 */
export function dropTarget(active: Pick<Active, 'data'>, over: Pick<Over, 'data'> | null): DropTarget | null {
  const item = cardOf(active);
  const column = columnOf(over);
  if (item === undefined || column === undefined || column.state.key === item.state) {
    return null;
  }
  return { item, to: column.state.key };
}

function cardName(active: Pick<Active, 'data'>): string {
  const item = cardOf(active);
  return item === undefined ? '卡片' : `${item.key}「${item.title}」`;
}

/** 拖曳時唸給螢幕閱讀器聽的話（dnd-kit 預設是英文）。 */
export const BOARD_ANNOUNCEMENTS: Announcements = {
  onDragStart: ({ active }) => `開始拖曳 ${cardName(active)}。`,
  onDragOver: ({ active, over }) => {
    const column = columnOf(over);
    if (column === undefined) {
      return `${cardName(active)} 不在任何一欄上。`;
    }
    const note = column.blockedReason === null ? '' : `，看起來走不到：${column.blockedReason}`;
    return `${cardName(active)} 在「${column.state.name}」欄上${note}。`;
  },
  // 真的要送出時回空字串：dnd-kit 只略過 null，空字串才會清掉它唸的區域。送出中、成功、被擋下來
  // 都由看板自己的 status 與 alert 唸，不會和「在哪一欄上」同時留著或重複唸。
  onDragEnd: ({ active, over }) => (dropTarget(active, over) === null ? `${cardName(active)} 放回原處，狀態沒有改。` : ''),
  onDragCancel: ({ active }) => `取消拖曳，${cardName(active)} 放回原處。`,
};

export const BOARD_INSTRUCTIONS: ScreenReaderInstructions = {
  draggable: '用滑鼠拖曳卡片，或用手指按住卡片一下再拖，放到別的狀態欄就會改狀態。用鍵盤時，請用卡片上的「移到…」選單。',
};

/**
 * 自動捲動：只有看板（欄的橫向捲動容器）會捲，用來拖到捲出畫面的欄。欄自己的直向捲動不自動捲：
 * 欄裡照編號排，放在哪個高度結果都一樣，捲了沒有用處；而且指標不在任何一欄上（例如拖到工具列上取消）時，
 * dnd-kit 退回用被拖的卡片的捲動祖先，指標在欄的上方就會把原本那一欄一路捲到最上面。
 * 拖到邊緣多近才捲（佔看板寬度的比例）：dnd-kit 預設的 20% 在寬螢幕是右邊兩百多 px，拖到右邊那一欄還沒放下看板就捲走了；
 * 寬螢幕只留邊緣一小段，窄螢幕一欄八成寬、下一欄只露出一點邊，範圍放大到摸得到。
 * 速度是預設的一半（貼著邊時每秒約 1000px）：預設在手機上不到 0.2 秒就捲過一欄，停不到想要的那一欄。
 */
const AUTO_SCROLL_EDGE = { wideX: 0.05, compactX: 0.15, y: 0.12 } as const;
const AUTO_SCROLL_ACCELERATION = 5;

export function boardAutoScroll(isCompact: boolean, lanes: Element | null): AutoScrollOptions {
  return {
    threshold: { x: isCompact ? AUTO_SCROLL_EDGE.compactX : AUTO_SCROLL_EDGE.wideX, y: AUTO_SCROLL_EDGE.y },
    acceleration: AUTO_SCROLL_ACCELERATION,
    canScroll: (element) => lanes !== null && element === lanes,
  };
}

/** 讀不到設計變數時（測試環境沒有載入 CSS）用的值：和 tokens.css 的 --duration-normal、--ease-out 一樣。 */
const DROP_ANIMATION_FALLBACK = { duration: 200, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' } as const;

/** 「200ms」「0.2s」換成毫秒；看不懂的是 null。 */
function toMilliseconds(value: string): number | null {
  const match = /^(\d*\.?\d+)(ms|s)$/.exec(value.trim());
  if (match === null) {
    return null;
  }
  const amount = Number(match[1]);
  return match[2] === 's' ? amount * 1000 : amount;
}

/**
 * 放下時卡片滑到落點：時間與曲線用設計變數（dnd-kit 要的是數字與字串，不吃 CSS 變數，所以執行時讀出來）。
 * 使用者要求減少動態時不做。
 */
export function boardDropAnimation(): DropAnimation | null {
  // 有些環境（測試用的 jsdom）沒有 matchMedia；型別上它一定在，執行時不一定。
  const canQuery = typeof window.matchMedia === 'function';
  if (canQuery && window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    return null;
  }
  const tokens = window.getComputedStyle(document.documentElement);
  return {
    duration: toMilliseconds(tokens.getPropertyValue('--duration-normal')) ?? DROP_ANIMATION_FALLBACK.duration,
    easing: tokens.getPropertyValue('--ease-out').trim() || DROP_ANIMATION_FALLBACK.easing,
  };
}
