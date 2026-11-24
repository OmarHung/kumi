import { afterEach, describe, expect, it } from 'vitest';
import {
  createColumnOrderStore,
  DEFAULT_COLUMN_ORDER,
  dropColumn,
  MY_WORK_ORDER_KEY,
  normalizeColumnOrder,
  pinnedColumnsOf,
  resolveDropSlot,
  stepColumn,
} from './myWorkColumnOrder';
import type { MyWorkColumnId } from './myWorkColumns';

const ALL: readonly MyWorkColumnId[] = DEFAULT_COLUMN_ORDER;
const NO_PROJECT: readonly MyWorkColumnId[] = ['key', 'title', 'state', 'priority', 'due'];

afterEach(() => {
  localStorage.clear();
});

describe('normalizeColumnOrder：記住的順序可能是舊的或被改壞', () => {
  it('預設是編號、專案、標題、狀態、優先度、到期日', () => {
    // Arrange / Act / Assert
    expect(normalizeColumnOrder(null)).toEqual(['key', 'project', 'title', 'state', 'priority', 'due']);
  });

  it('不認得的、重複的拿掉，少了的照預設的位置補回去', () => {
    // Arrange
    const raw = ['due', 'nope', 'key', 'due', 'title', 7];

    // Act
    const order = normalizeColumnOrder(raw);

    // Assert
    expect(order).toEqual(['key', 'project', 'title', 'due', 'state', 'priority']);
  });

  it('狀態這類的欄被放到標題左邊時，移到標題的右邊', () => {
    // Arrange
    const raw = ['state', 'key', 'title', 'priority', 'due', 'project'];

    // Act
    const order = normalizeColumnOrder(raw);

    // Assert
    expect(order).toEqual(['key', 'title', 'state', 'priority', 'due', 'project']);
  });

  it.each([['不是陣列'], [{ key: 1 }], [42]])('不是陣列就是預設：%j', (raw) => {
    // Arrange / Act / Assert
    expect(normalizeColumnOrder(raw)).toEqual(DEFAULT_COLUMN_ORDER);
  });
});

describe('pinnedColumnsOf：標題和它左邊的欄固定在左邊', () => {
  it('預設是編號、專案、標題', () => {
    // Arrange / Act / Assert
    expect(pinnedColumnsOf(ALL)).toEqual(['key', 'project', 'title']);
  });

  it('專案放到標題右邊就不固定', () => {
    // Arrange / Act / Assert
    expect(pinnedColumnsOf(['key', 'title', 'state', 'project'])).toEqual(['key', 'title']);
  });
});

describe('dropColumn：拖到第幾個交界', () => {
  it('把專案拖到最後', () => {
    // Arrange / Act
    const order = dropColumn(ALL, ALL, 'project', 6);

    // Assert
    expect(order).toEqual(['key', 'title', 'state', 'priority', 'due', 'project']);
  });

  it('放回原位（自己的左右兩個交界）是沒有變化', () => {
    // Arrange / Act / Assert
    expect(dropColumn(ALL, ALL, 'state', 3)).toBeNull();
    expect(dropColumn(ALL, ALL, 'state', 4)).toBeNull();
  });

  it('狀態拖到標題左邊時，放在最近的可以放的地方（標題的右邊）', () => {
    // Arrange：到期日拖到最前面。
    const order = dropColumn(ALL, ALL, 'due', 0);

    // Assert
    expect(order).toEqual(['key', 'project', 'title', 'due', 'state', 'priority']);
  });

  it('標題拖到狀態的右邊時，放在最近的可以放的地方：沒有地方可去就是沒有變化', () => {
    // Arrange / Act / Assert
    expect(dropColumn(ALL, ALL, 'title', 6)).toBeNull();
  });

  it('標題可以拖到編號、專案的左邊', () => {
    // Arrange / Act / Assert
    expect(dropColumn(ALL, ALL, 'title', 0)).toEqual(['title', 'key', 'project', 'state', 'priority', 'due']);
  });

  it('依專案分組沒有專案欄：交界照看得到的欄算，專案留在原本的相對位置', () => {
    // Arrange：看得到的是 編號、標題、狀態、優先度、到期日；把編號拖到標題的右邊（交界 2）。
    const order = dropColumn(ALL, NO_PROJECT, 'key', 2);

    // Assert
    expect(order).toEqual(['project', 'title', 'key', 'state', 'priority', 'due']);
  });
});

describe('resolveDropSlot：拖曳中畫放下去的位置', () => {
  it('放得下的地方就是那個交界', () => {
    // Arrange / Act / Assert
    expect(resolveDropSlot(ALL, ALL, 'project', 6)).toBe(6);
    expect(resolveDropSlot(ALL, ALL, 'due', 4)).toBe(4);
  });

  it('放不下的地方畫在最近放得下的交界；沒有變化不畫', () => {
    // Arrange / Act / Assert
    expect(resolveDropSlot(ALL, ALL, 'due', 0)).toBe(3);
    expect(resolveDropSlot(ALL, ALL, 'title', 6)).toBeNull();
    expect(resolveDropSlot(ALL, ALL, 'state', 4)).toBeNull();
  });
});

describe('stepColumn：「欄的順序」裡的往前、往後移', () => {
  it('往後移一欄', () => {
    // Arrange / Act / Assert
    expect(stepColumn(ALL, ALL, 'state', 1)).toEqual(['key', 'project', 'title', 'priority', 'state', 'due']);
  });

  it('第一欄不能再往前、最後一欄不能再往後', () => {
    // Arrange / Act / Assert
    expect(stepColumn(ALL, ALL, 'key', -1)).toBeNull();
    expect(stepColumn(ALL, ALL, 'due', 1)).toBeNull();
  });

  it('狀態不能往前越過標題；專案可以往後越過標題', () => {
    // Arrange / Act / Assert
    expect(stepColumn(ALL, ALL, 'state', -1)).toBeNull();
    expect(stepColumn(ALL, ALL, 'project', 1)).toEqual(['key', 'title', 'project', 'state', 'priority', 'due']);
  });

  it('跳過看不到的欄（依專案分組時的專案）', () => {
    // Arrange / Act / Assert
    expect(stepColumn(ALL, NO_PROJECT, 'key', 1)).toEqual(['project', 'title', 'key', 'state', 'priority', 'due']);
  });
});

describe('createColumnOrderStore：記在這個瀏覽器', () => {
  it('換了順序就記住，下次打開讀得回來', () => {
    // Arrange
    const store = createColumnOrderStore(MY_WORK_ORDER_KEY);
    const order: readonly MyWorkColumnId[] = ['key', 'title', 'state', 'priority', 'due', 'project'];

    // Act
    store.set(order);

    // Assert
    expect(store.getSnapshot()).toEqual({ order, isCustom: true });
    expect(createColumnOrderStore(MY_WORK_ORDER_KEY).getSnapshot().order).toEqual(order);
  });

  it('回到預設就清掉記住的', () => {
    // Arrange
    const store = createColumnOrderStore(MY_WORK_ORDER_KEY);
    store.set(['key', 'title', 'state', 'priority', 'due', 'project']);

    // Act
    store.reset();

    // Assert
    expect(store.getSnapshot()).toEqual({ order: DEFAULT_COLUMN_ORDER, isCustom: false });
    expect(localStorage.getItem(MY_WORK_ORDER_KEY)).toBeNull();
  });

  it('記住的內容讀不懂時照預設，不擋住表格', () => {
    // Arrange
    localStorage.setItem(MY_WORK_ORDER_KEY, '{壞掉');

    // Act
    const store = createColumnOrderStore(MY_WORK_ORDER_KEY);

    // Assert
    expect(store.getSnapshot().order).toEqual(DEFAULT_COLUMN_ORDER);
  });
});
