import type { InfiniteData } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import { queryKeys } from '../../api/queryKeys';
import type { Item, ItemPage, ItemQuery } from '../../api/types';
import { createQueryClient } from '../../app/queryClient';
import { smkItem } from '../../test/fixtures';
import { detailOf } from '../../test/items';
import { insertByNumber, removeItemFromCache, replaceItemInCache, replaceListedItemsInCache } from './itemCache';

type Pages = InfiniteData<ItemPage, string | null>;

function pagesOf(...pages: readonly (readonly Item[])[]): Pages {
  return {
    pages: pages.map((items, index) => ({ items, nextCursor: index + 1 < pages.length ? `page-${String(index + 1)}` : null })),
    pageParams: pages.map((_, index) => (index === 0 ? null : `page-${String(index)}`)),
  };
}

function column(state: string): ItemQuery {
  return { type: ['task'], state: [state], sort: { field: 'number', direction: 'asc' }, limit: 50 };
}

function keysIn(data: Pages | undefined): string[][] {
  return (data?.pages ?? []).map((page) => page.items.map((item) => item.key));
}

describe('insertByNumber', () => {
  it('放在最後一個編號比它小的後面；最前面有剛新增的大編號也不影響', () => {
    // Arrange
    const items = [smkItem({ number: 9 }), smkItem({ number: 1 }), smkItem({ number: 2 }), smkItem({ number: 5 })];

    // Act
    const result = insertByNumber(items, smkItem({ number: 3 }));

    // Assert
    expect(result.map((item) => item.number)).toEqual([9, 1, 2, 3, 5]);
  });

  it('比每一個都小時放在最前面', () => {
    // Arrange / Act
    const result = insertByNumber([smkItem({ number: 4 })], smkItem({ number: 2 }));

    // Assert
    expect(result.map((item) => item.number)).toEqual([2, 4]);
  });
});

describe('replaceItemInCache：看板的欄', () => {
  it('改了狀態的項目從舊的欄拿掉，照編號放進新的欄（跨頁時放進該在的那一頁）', () => {
    // Arrange
    const queryClient = createQueryClient();
    const todoKey = queryKeys.boardColumn('SMK', column('todo'));
    const doingKey = queryKeys.boardColumn('SMK', column('doing'));
    const moved = smkItem({ number: 4, state: 'todo' });
    queryClient.setQueryData<Pages>(todoKey, pagesOf([smkItem({ number: 2 }), moved]));
    const doing = (number: number) => smkItem({ number, state: 'doing', stateCategory: 'in_progress' });
    queryClient.setQueryData<Pages>(doingKey, pagesOf([doing(1), doing(3)], [doing(6)]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...moved, state: 'doing', stateCategory: 'in_progress' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(todoKey))).toEqual([['SMK-2']]);
    expect(keysIn(queryClient.getQueryData<Pages>(doingKey))).toEqual([['SMK-1', 'SMK-3', 'SMK-4'], ['SMK-6']]);
  });

  it('類型不在這一欄的條件裡就不放進去', () => {
    // Arrange
    const queryClient = createQueryClient();
    const otherTypes = queryKeys.boardColumn('SMK', { ...column('doing'), type: ['bug'] });
    queryClient.setQueryData<Pages>(otherTypes, pagesOf([]));

    // Act
    replaceItemInCache(queryClient, detailOf(smkItem({ number: 4, state: 'doing', stateCategory: 'in_progress' })));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(otherTypes))).toEqual([[]]);
  });

  it('表格的清單照舊在原位換掉，即使篩選的狀態對不上了', () => {
    // Arrange
    const queryClient = createQueryClient();
    const tableKey = queryKeys.items('SMK', { state: ['todo'], limit: 50 });
    const item = smkItem({ number: 4, state: 'todo' });
    queryClient.setQueryData<Pages>(tableKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, state: 'done', stateCategory: 'done' }));

    // Assert
    expect(queryClient.getQueryData<Pages>(tableKey)?.pages[0]?.items[0]?.state).toBe('done');
  });
});

/** 最後一頁還有下一頁（伺服器上還有沒載入的）。 */
function withMore(data: Pages): Pages {
  const last = data.pages.length - 1;
  return { ...data, pages: data.pages.map((page, index) => (index === last ? { ...page, nextCursor: 'page-more' } : page)) };
}

function isInvalidated(queryClient: ReturnType<typeof createQueryClient>, queryKey: readonly unknown[]): boolean {
  return queryClient.getQueryState(queryKey)?.isInvalidated ?? false;
}

const doingItem = (number: number) => smkItem({ number, state: 'doing', stateCategory: 'in_progress' });

describe('replaceItemInCache：還有下一頁的欄（游標是位移量）', () => {
  it('移出還有下一頁的欄：先拿掉，再讓那一欄重抓，下一頁才不會跳過一張', () => {
    // Arrange
    const queryClient = createQueryClient();
    const todoKey = queryKeys.boardColumn('SMK', column('todo'));
    const moved = smkItem({ number: 1 });
    queryClient.setQueryData<Pages>(todoKey, withMore(pagesOf([moved, smkItem({ number: 2 })])));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...moved, state: 'doing', stateCategory: 'in_progress' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(todoKey))).toEqual([['SMK-2']]);
    expect(isInvalidated(queryClient, todoKey)).toBe(true);
  });

  it('移進還有下一頁的欄：編號在已載入的範圍內就照編號放，並且重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const doingKey = queryKeys.boardColumn('SMK', column('doing'));
    queryClient.setQueryData<Pages>(doingKey, withMore(pagesOf([doingItem(2), doingItem(6)])));
    const moved = smkItem({ number: 4 });
    queryClient.setQueryData<Pages>(queryKeys.boardColumn('SMK', column('todo')), pagesOf([moved]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...moved, state: 'doing', stateCategory: 'in_progress' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(doingKey))).toEqual([['SMK-2', 'SMK-4', 'SMK-6']]);
    expect(isInvalidated(queryClient, doingKey)).toBe(true);
  });

  it('移進還有下一頁的欄、編號超過已載入的範圍：不放進去（還沒載入的較小編號該排在它前面），重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const doingKey = queryKeys.boardColumn('SMK', column('doing'));
    queryClient.setQueryData<Pages>(doingKey, withMore(pagesOf([doingItem(2), doingItem(6)])));
    const moved = smkItem({ number: 9 });
    queryClient.setQueryData<Pages>(queryKeys.boardColumn('SMK', column('todo')), pagesOf([moved]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...moved, state: 'doing', stateCategory: 'in_progress' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(doingKey))).toEqual([['SMK-2', 'SMK-6']]);
    expect(isInvalidated(queryClient, doingKey)).toBe(true);
  });

  it('沒有下一頁的欄照舊只改快取，不重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const todoKey = queryKeys.boardColumn('SMK', column('todo'));
    const doingKey = queryKeys.boardColumn('SMK', column('doing'));
    const moved = smkItem({ number: 1 });
    queryClient.setQueryData<Pages>(todoKey, pagesOf([moved]));
    queryClient.setQueryData<Pages>(doingKey, pagesOf([]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...moved, state: 'doing', stateCategory: 'in_progress' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(doingKey))).toEqual([['SMK-1']]);
    expect(isInvalidated(queryClient, todoKey)).toBe(false);
    expect(isInvalidated(queryClient, doingKey)).toBe(false);
  });
});

describe('replaceItemInCache：欄的條件有負責人或搜尋', () => {
  it('不自己判斷要不要放進去：不插入，標成要重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const mineKey = queryKeys.boardColumn('SMK', { ...column('doing'), assignee: 'me' });
    const searchKey = queryKeys.boardColumn('SMK', { ...column('doing'), q: '設計' });
    queryClient.setQueryData<Pages>(mineKey, pagesOf([]));
    queryClient.setQueryData<Pages>(searchKey, pagesOf([]));
    const moved = smkItem({ number: 1, assignee: null });
    queryClient.setQueryData<Pages>(queryKeys.boardColumn('SMK', column('todo')), pagesOf([moved]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...moved, state: 'doing', stateCategory: 'in_progress' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(mineKey))).toEqual([[]]);
    expect(keysIn(queryClient.getQueryData<Pages>(searchKey))).toEqual([[]]);
    expect(isInvalidated(queryClient, mineKey)).toBe(true);
    expect(isInvalidated(queryClient, searchKey)).toBe(true);
  });

  it('在欄裡、改了負責人：原位換掉，再重抓（可能已經不符合條件）', () => {
    // Arrange
    const queryClient = createQueryClient();
    const mineKey = queryKeys.boardColumn('SMK', { ...column('todo'), assignee: 'me' });
    const item = smkItem({ number: 1 });
    queryClient.setQueryData<Pages>(mineKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, assignee: null }));

    // Assert
    expect(queryClient.getQueryData<Pages>(mineKey)?.pages[0]?.items[0]?.assignee).toBeNull();
    expect(isInvalidated(queryClient, mineKey)).toBe(true);
  });

  it('改的欄位和條件無關（優先度）：原位換掉，不重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const mineKey = queryKeys.boardColumn('SMK', { ...column('todo'), assignee: 'me', q: '首頁' });
    const item = smkItem({ number: 1 });
    queryClient.setQueryData<Pages>(mineKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, priority: 'high' }));

    // Assert
    expect(queryClient.getQueryData<Pages>(mineKey)?.pages[0]?.items[0]?.priority).toBe('high');
    expect(isInvalidated(queryClient, mineKey)).toBe(false);
  });
});

describe('replaceItemInCache：欄的條件有週期或里程碑', () => {
  const SPRINT = '0190a000-0000-7000-8000-0000000000d2';
  const V1 = '0190a000-0000-7000-8000-0000000000e1';

  it('搬進這一欄時不自己判斷（週期與里程碑以伺服器為準）：不插入，標成要重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const cycleKey = queryKeys.boardColumn('SMK', { ...column('doing'), cycleId: SPRINT });
    const noCycleKey = queryKeys.boardColumn('SMK', { ...column('doing'), noCycle: true });
    const milestoneKey = queryKeys.boardColumn('SMK', { ...column('doing'), milestoneId: V1 });
    [cycleKey, noCycleKey, milestoneKey].forEach((key) => queryClient.setQueryData<Pages>(key, pagesOf([])));
    const moved = smkItem({ number: 1, cycleId: SPRINT });
    queryClient.setQueryData<Pages>(queryKeys.boardColumn('SMK', column('todo')), pagesOf([moved]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...moved, state: 'doing', stateCategory: 'in_progress' }));

    // Assert
    [cycleKey, noCycleKey, milestoneKey].forEach((key) => {
      expect(keysIn(queryClient.getQueryData<Pages>(key))).toEqual([[]]);
      expect(isInvalidated(queryClient, key)).toBe(true);
    });
  });

  it('在欄裡、換了週期或里程碑：原位換掉，再重抓（可能已經不符合條件）', () => {
    // Arrange
    const queryClient = createQueryClient();
    const cycleKey = queryKeys.boardColumn('SMK', { ...column('todo'), cycleId: SPRINT });
    const milestoneKey = queryKeys.boardColumn('SMK', { ...column('todo'), noMilestone: true });
    const item = smkItem({ number: 1, cycleId: SPRINT });
    queryClient.setQueryData<Pages>(cycleKey, pagesOf([item]));
    queryClient.setQueryData<Pages>(milestoneKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, cycleId: null, milestoneId: V1 }));

    // Assert
    expect(queryClient.getQueryData<Pages>(cycleKey)?.pages[0]?.items[0]?.cycleId).toBeNull();
    expect(isInvalidated(queryClient, cycleKey)).toBe(true);
    expect(isInvalidated(queryClient, milestoneKey)).toBe(true);
  });
});

describe('replaceItemInCache：狀態沒變的修改', () => {
  it('不在已載入的欄裡就不放進去，在的話原位換掉，都不重抓', () => {
    // Arrange：表格裡有 #9，看板「待辦」只載入 #1、#3（沒有下一頁也一樣不插入）
    const queryClient = createQueryClient();
    const todoKey = queryKeys.boardColumn('SMK', column('todo'));
    const tableKey = queryKeys.items('SMK', { q: '首頁' });
    const item = smkItem({ number: 9, title: '舊標題' });
    queryClient.setQueryData<Pages>(todoKey, pagesOf([smkItem({ number: 1 }), smkItem({ number: 3 })]));
    queryClient.setQueryData<Pages>(tableKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, title: '新標題' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(todoKey))).toEqual([['SMK-1', 'SMK-3']]);
    expect(isInvalidated(queryClient, todoKey)).toBe(false);
  });
});

describe('replaceItemInCache：表格的清單還有下一頁', () => {
  it('篩選或排序用到的欄位變了：原位換掉，再重抓（項目可能離開條件或換位置）', () => {
    // Arrange
    const queryClient = createQueryClient();
    const byState = queryKeys.items('SMK', { state: ['todo'], limit: 50 });
    const byTitle = queryKeys.items('SMK', { sort: { field: 'title', direction: 'asc' }, limit: 50 });
    const item = smkItem({ number: 1 });
    queryClient.setQueryData<Pages>(byState, withMore(pagesOf([item])));
    queryClient.setQueryData<Pages>(byTitle, withMore(pagesOf([item])));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, state: 'done', stateCategory: 'done' }));

    // Assert
    expect(queryClient.getQueryData<Pages>(byState)?.pages[0]?.items[0]?.state).toBe('done');
    expect(isInvalidated(queryClient, byState)).toBe(true);
    expect(isInvalidated(queryClient, byTitle)).toBe(false);
  });

  it('下層項目的清單裡換了上層：拿掉，還有下一頁就重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const children = queryKeys.items('SMK', { parent: 'SMK-7' });
    const item = smkItem({ number: 1, parentKey: 'SMK-7' });
    queryClient.setQueryData<Pages>(children, withMore(pagesOf([item, smkItem({ number: 2, parentKey: 'SMK-7' })])));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, parentKey: 'SMK-8' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(children))).toEqual([['SMK-2']]);
    expect(isInvalidated(queryClient, children)).toBe(true);
  });

  it('只改了和條件、排序無關的欄位：原位換掉，不重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const byState = queryKeys.items('SMK', { state: ['todo'], limit: 50 });
    const item = smkItem({ number: 1 });
    queryClient.setQueryData<Pages>(byState, withMore(pagesOf([item])));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, title: '改過的標題' }));

    // Assert
    expect(queryClient.getQueryData<Pages>(byState)?.pages[0]?.items[0]?.title).toBe('改過的標題');
    expect(isInvalidated(queryClient, byState)).toBe(false);
  });
});

describe('replaceItemInCache：欄的條件有優先度或到期', () => {
  it('篩「高」的欄，把卡片改成「低」：原位換掉並標成要重抓（卡片要離開）', () => {
    // Arrange
    const queryClient = createQueryClient();
    const highKey = queryKeys.boardColumn('SMK', { ...column('todo'), priority: ['high'] });
    const item = smkItem({ number: 1, priority: 'high' });
    queryClient.setQueryData<Pages>(highKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, priority: 'low' }));

    // Assert
    expect(isInvalidated(queryClient, highKey)).toBe(true);
  });

  it('篩「本週到期」的欄，別欄的項目拖進來：不自己插入，標成要重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const weekKey = queryKeys.boardColumn('SMK', { ...column('doing'), dueFrom: '2026-11-18', dueTo: '2026-11-22' });
    queryClient.setQueryData<Pages>(weekKey, pagesOf([]));
    const moved = smkItem({ number: 1, dueDate: '2027-03-01' });
    queryClient.setQueryData<Pages>(queryKeys.boardColumn('SMK', column('todo')), pagesOf([moved]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...moved, state: 'doing', stateCategory: 'in_progress' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(weekKey))).toEqual([[]]);
    expect(isInvalidated(queryClient, weekKey)).toBe(true);
  });

  it('篩「已過期」（帶完成類別的條件）的欄，別欄的項目拖進來：標成要重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const overdueKey = queryKeys.boardColumn('SMK', {
      ...column('doing'),
      dueTo: '2026-11-17',
      stateCategory: ['backlog', 'not_started', 'in_progress'],
    });
    queryClient.setQueryData<Pages>(overdueKey, pagesOf([]));
    const moved = smkItem({ number: 1, dueDate: '2026-11-01' });
    queryClient.setQueryData<Pages>(queryKeys.boardColumn('SMK', column('todo')), pagesOf([moved]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...moved, state: 'doing', stateCategory: 'in_progress' }));

    // Assert
    expect(isInvalidated(queryClient, overdueKey)).toBe(true);
  });

  it('篩「沒有到期日」的欄，在欄裡的卡片設了到期日：標成要重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const noDueKey = queryKeys.boardColumn('SMK', { ...column('todo'), noDue: true });
    const item = smkItem({ number: 1, dueDate: null });
    queryClient.setQueryData<Pages>(noDueKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, dueDate: '2026-12-01' }));

    // Assert
    expect(isInvalidated(queryClient, noDueKey)).toBe(true);
  });
});

describe('replaceItemInCache：表格的清單有優先度或到期的條件且還有下一頁', () => {
  it('改了優先度或到期日：原位換掉，再重抓；改無關的欄位不重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const byPriority = queryKeys.items('SMK', { priority: ['high'], limit: 50 });
    const byDue = queryKeys.items('SMK', { dueFrom: '2026-11-18', dueTo: '2026-11-22', limit: 50 });
    const item = smkItem({ number: 1, priority: 'high', dueDate: '2026-11-20' });
    queryClient.setQueryData<Pages>(byPriority, withMore(pagesOf([item])));
    queryClient.setQueryData<Pages>(byDue, withMore(pagesOf([item])));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, title: '只改標題' }));
    const afterTitle = [isInvalidated(queryClient, byPriority), isInvalidated(queryClient, byDue)];
    replaceItemInCache(queryClient, detailOf({ ...item, priority: 'low' }));
    const afterPriority = [isInvalidated(queryClient, byPriority), isInvalidated(queryClient, byDue)];
    replaceItemInCache(queryClient, detailOf({ ...item, dueDate: '2027-01-01' }));

    // Assert
    expect(afterTitle).toEqual([false, false]);
    expect(afterPriority).toEqual([true, false]);
    expect(isInvalidated(queryClient, byDue)).toBe(true);
  });
});

describe('replaceItemInCache：行事曆與時間軸的清單', () => {
  const MONTH = { dueFrom: '2026-10-26', dueTo: '2026-12-06', sort: { field: 'due_date', direction: 'asc' }, limit: 200 } as const;
  const WINDOW = { datesFrom: '2026-11-02', datesTo: '2026-11-29', sort: { field: 'number', direction: 'asc' }, limit: 200 } as const;
  const UNDATED = { noDue: true, limit: 200 } as const;

  it('補上本月的到期日：項目原本不在月曆的清單裡，也要重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const calendarKey = queryKeys.dateView('SMK', MONTH);
    const item = smkItem({ number: 1, dueDate: null });
    queryClient.setQueryData<Pages>(calendarKey, pagesOf([]));
    queryClient.setQueryData<Pages>(queryKeys.items('SMK', { limit: 50 }), pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, dueDate: '2026-11-20' }));

    // Assert
    expect(isInvalidated(queryClient, calendarKey)).toBe(true);
  });

  it('只改標題：原位換掉，不重抓；改了到期日在原位也重抓（不原位保留）', () => {
    // Arrange
    const queryClient = createQueryClient();
    const calendarKey = queryKeys.dateView('SMK', MONTH);
    const item = smkItem({ number: 1, dueDate: '2026-11-20' });
    queryClient.setQueryData<Pages>(calendarKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, title: '新標題' }));
    const afterTitle = isInvalidated(queryClient, calendarKey);
    replaceItemInCache(queryClient, detailOf({ ...item, title: '新標題', dueDate: '2027-02-01' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(calendarKey))).toEqual([['SMK-1']]);
    expect(afterTitle).toBe(false);
    expect(isInvalidated(queryClient, calendarKey)).toBe(true);
  });

  it('時間軸：改了開始日或到期日，期間的清單與「沒有日期」的清單都重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const windowKey = queryKeys.dateView('SMK', WINDOW);
    const undatedKey = queryKeys.dateView('SMK', UNDATED);
    const item = smkItem({ number: 1, dueDate: null, startDate: null });
    queryClient.setQueryData<Pages>(windowKey, pagesOf([]));
    queryClient.setQueryData<Pages>(undatedKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, startDate: '2026-11-10' }));

    // Assert
    expect(isInvalidated(queryClient, windowKey)).toBe(true);
    expect(isInvalidated(queryClient, undatedKey)).toBe(true);
  });

  it('日期以外的條件沒動時不重抓別的清單', () => {
    // Arrange
    const queryClient = createQueryClient();
    const windowKey = queryKeys.dateView('SMK', WINDOW);
    const item = smkItem({ number: 1, dueDate: '2026-11-20' });
    queryClient.setQueryData<Pages>(windowKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, estimate: 5 }));

    // Assert
    expect(isInvalidated(queryClient, windowKey)).toBe(false);
  });
});

describe('replaceItemInCache：依負責人、欄位分欄的一份清單（沒結束的類別，不分狀態）', () => {
  const grouped: ItemQuery = {
    stateCategory: ['backlog', 'not_started', 'in_progress'],
    sort: { field: 'number', direction: 'asc' },
    limit: 200,
  };

  it('改了負責人：項目留在這份清單的原位（新的值），不因為沒有狀態條件而被拿掉', () => {
    // Arrange
    const queryClient = createQueryClient();
    const key = queryKeys.boardColumn('SMK', grouped);
    const first = smkItem({ number: 1, assignee: null });
    queryClient.setQueryData<Pages>(key, pagesOf([first, smkItem({ number: 2 })]));
    const owner = { personId: 'p1', displayName: 'A' };

    // Act
    replaceItemInCache(queryClient, detailOf({ ...first, assignee: owner }));

    // Assert
    const items = queryClient.getQueryData<Pages>(key)?.pages[0]?.items;
    expect(items?.map((item) => item.key)).toEqual(['SMK-1', 'SMK-2']);
    expect(items?.[0]?.assignee).toEqual(owner);
  });

  it('改成完成（離開沒結束的類別）：從這份清單拿掉', () => {
    // Arrange
    const queryClient = createQueryClient();
    const key = queryKeys.boardColumn('SMK', grouped);
    const first = smkItem({ number: 1 });
    queryClient.setQueryData<Pages>(key, pagesOf([first, smkItem({ number: 2 })]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...first, state: 'done', stateCategory: 'done' }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(key))).toEqual([['SMK-2']]);
  });
});

describe('replaceItemInCache：清單有自訂欄位的條件', () => {
  const clientA = (state: string): ItemQuery => ({ ...column(state), fields: { client: ['client_a'] } });

  it('在清單裡、改了那個欄位：原位換掉，再重抓（可能已經不符合）', () => {
    // Arrange
    const queryClient = createQueryClient();
    const key = queryKeys.boardColumn('SMK', clientA('todo'));
    const item = smkItem({ number: 1, fields: { client: 'client_a' } });
    queryClient.setQueryData<Pages>(key, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, fields: { client: 'client_b' } }));

    // Assert
    expect(isInvalidated(queryClient, key)).toBe(true);
  });

  it('改了欄位、從別的清單進來：不自己判斷放不放，不插入，標成要重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const key = queryKeys.boardColumn('SMK', clientA('todo'));
    queryClient.setQueryData<Pages>(key, pagesOf([]));
    const item = smkItem({ number: 1, fields: { client: 'client_b' } });
    queryClient.setQueryData<Pages>(queryKeys.boardColumn('SMK', column('todo')), pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, fields: { client: 'client_a' } }));

    // Assert
    expect(keysIn(queryClient.getQueryData<Pages>(key))).toEqual([[]]);
    expect(isInvalidated(queryClient, key)).toBe(true);
  });

  it('改的不是條件裡的欄位：原位換掉，不重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const key = queryKeys.boardColumn('SMK', clientA('todo'));
    const item = smkItem({ number: 1, fields: { client: 'client_a' } });
    queryClient.setQueryData<Pages>(key, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, priority: 'high' }));

    // Assert
    expect(isInvalidated(queryClient, key)).toBe(false);
  });

  it('批次修改改了欄位：整份重抓', () => {
    // Arrange
    const queryClient = createQueryClient();
    const key = queryKeys.boardColumn('SMK', clientA('todo'));
    const item = smkItem({ number: 1, fields: { client: 'client_a' } });
    queryClient.setQueryData<Pages>(key, pagesOf([item]));

    // Act
    replaceListedItemsInCache(queryClient, 'SMK', [{ ...item, fields: { client: 'client_b' } }]);

    // Assert
    expect(isInvalidated(queryClient, key)).toBe(true);
  });
});

describe('「我的工作」的清單', () => {
  const query: ItemQuery = { assignee: 'me', stateCategory: ['in_progress'], limit: 200 };

  it('表格與看板的清單：修改時不重抓、不動它（停在原位靠頁面記下的那一份）', () => {
    // Arrange
    const queryClient = createQueryClient();
    const tableKey = queryKeys.myWorkItems(query);
    const boardKey = queryKeys.myWorkBoard(query);
    const item = smkItem({ number: 4, state: 'doing', stateCategory: 'in_progress' });
    queryClient.setQueryData<Pages>(tableKey, pagesOf([item]));
    queryClient.setQueryData<Pages>(boardKey, pagesOf([item]));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, state: 'done', stateCategory: 'done' }));

    // Assert
    expect(queryClient.getQueryState(tableKey)?.isInvalidated).toBe(true);
    expect(queryClient.getQueryData<Pages>(tableKey)?.pages[0]?.items[0]?.state).toBe('doing');
  });

  it('行事曆與時間軸的清單：沒動到條件的修改只在原位換掉（動到條件的重抓見行事曆與時間軸的測試）', () => {
    // Arrange
    const queryClient = createQueryClient();
    const key = queryKeys.myWorkDateView({ ...query, dueFrom: '2026-11-01', dueTo: '2026-11-30' });
    const item = smkItem({ number: 4, title: '舊標題', dueDate: '2026-11-10' });
    queryClient.setQueryData<Pages>(key, pagesOf([item]));
    queryClient.setQueryData(queryKeys.item(item.key), detailOf(item));

    // Act
    replaceItemInCache(queryClient, detailOf({ ...item, title: '新標題' }));

    // Assert
    expect(queryClient.getQueryData<Pages>(key)?.pages[0]?.items[0]?.title).toBe('新標題');
  });

  it('刪除：四種版面的每一份清單、每一頁都拿掉那個項目', () => {
    // Arrange
    const queryClient = createQueryClient();
    const gone = smkItem({ number: 4 });
    const kept = smkItem({ number: 5 });
    const keys = [queryKeys.myWorkItems(query), queryKeys.myWorkBoard(query), queryKeys.myWorkDateView(query)];
    keys.forEach((key) => queryClient.setQueryData<Pages>(key, pagesOf([kept], [gone])));

    // Act
    removeItemFromCache(queryClient, 'SMK', gone.key);

    // Assert
    keys.forEach((key) => {
      expect(keysIn(queryClient.getQueryData<Pages>(key))).toEqual([['SMK-5'], []]);
      expect(queryClient.getQueryState(key)?.isInvalidated).toBe(true);
    });
  });
});
