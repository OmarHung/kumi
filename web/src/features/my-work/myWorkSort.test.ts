import { describe, expect, it } from 'vitest';
import type { Item } from '../../api/types';
import { makeItem } from '../../test/fixtures';
import {
  effectiveMyWorkSort,
  myWorkSortOf,
  nextMyWorkSort,
  sortMyWorkRows,
  withMyWorkSort,
  type MyWorkSort,
} from './myWorkSort';
import { rowOf, type MyWorkRow } from './useMyWorkRows';

function row(overrides: Partial<Item>): MyWorkRow {
  const item = makeItem(overrides);
  return rowOf(item, item);
}

function keys(rows: readonly MyWorkRow[]): string[] {
  return rows.map((entry) => entry.key);
}

const NAMES: Readonly<Record<string, string>> = { WEB: '官網改版', OPS: '客戶維運', APP: 'App' };
const projectName = (projectKey: string) => NAMES[projectKey];

function sorted(rows: readonly MyWorkRow[], sort: MyWorkSort | null): string[] {
  return keys(sortMyWorkRows(rows, sort, projectName));
}

describe('myWorkSortOf：網址的 sort', () => {
  it('讀得懂「欄位:方向」', () => {
    // Arrange
    const params = new URLSearchParams('sort=priority:desc');

    // Act
    const sort = myWorkSortOf(params);

    // Assert
    expect(sort).toEqual({ field: 'priority', direction: 'desc' });
  });

  it.each(['', 'priority', 'priority:up', 'nope:asc', 'start_date:asc', 'field.client:asc', 'due_date:asc'])(
    '不認得的值與預設（到期日遞增）都當成沒有排序：%s',
    (value) => {
      // Arrange
      const params = new URLSearchParams({ sort: value });

      // Act
      const sort = myWorkSortOf(params);

      // Assert
      expect(sort).toBeNull();
    },
  );
});

describe('依專案分組時沒有專案欄', () => {
  it('依專案排序當成沒有排序（每一組只有一個專案）：表頭、下拉與存的檢視都不留著看不見的排序', () => {
    // Arrange
    const grouped = new URLSearchParams('group=project&sort=project:asc');
    const byUrgency = new URLSearchParams('sort=project:desc');
    const other = new URLSearchParams('group=project&sort=title:desc');

    // Act / Assert
    expect(myWorkSortOf(grouped)).toBeNull();
    expect(myWorkSortOf(byUrgency)).toEqual({ field: 'project', direction: 'desc' });
    expect(myWorkSortOf(other)).toEqual({ field: 'title', direction: 'desc' });
    expect(effectiveMyWorkSort({ field: 'project', direction: 'asc' }, 'project')).toBeNull();
    expect(effectiveMyWorkSort({ field: 'project', direction: 'asc' }, 'urgency')).toEqual({ field: 'project', direction: 'asc' });
  });
});

describe('withMyWorkSort', () => {
  it('換掉排序，其他參數留著', () => {
    // Arrange
    const params = new URLSearchParams('group=project&q=設計&item=WEB-1');

    // Act
    const next = withMyWorkSort(params, { field: 'title', direction: 'asc' });

    // Assert
    expect(Object.fromEntries(next)).toEqual({ group: 'project', q: '設計', item: 'WEB-1', sort: 'title:asc' });
  });

  it('回到預設就從網址拿掉', () => {
    // Arrange
    const params = new URLSearchParams('sort=title:asc&q=設計');

    // Act
    const next = withMyWorkSort(params, null);

    // Assert
    expect(next.toString()).toBe(`q=${encodeURIComponent('設計')}`);
  });
});

describe('nextMyWorkSort：點表頭', () => {
  it('換一欄從遞增開始，再點遞減，再點回到預設', () => {
    // Arrange / Act
    const first = nextMyWorkSort(null, 'priority');
    const second = nextMyWorkSort(first, 'priority');
    const third = nextMyWorkSort(second, 'priority');

    // Assert
    expect(first).toEqual({ field: 'priority', direction: 'asc' });
    expect(second).toEqual({ field: 'priority', direction: 'desc' });
    expect(third).toBeNull();
  });

  it('預設就是到期日遞增：點到期日先變成遞減，再點回到預設', () => {
    // Arrange / Act
    const first = nextMyWorkSort(null, 'due_date');
    const second = nextMyWorkSort(first, 'due_date');

    // Assert
    expect(first).toEqual({ field: 'due_date', direction: 'desc' });
    expect(second).toBeNull();
  });
});

describe('sortMyWorkRows：在一組裡面排', () => {
  it('沒有排序時照到期日再照編號，沒有到期日的在最後', () => {
    // Arrange
    const rows = [
      row({ number: 3 }),
      row({ number: 2, dueDate: '2026-11-20' }),
      row({ number: 1, dueDate: '2026-11-20' }),
      row({ number: 4, dueDate: '2026-11-18' }),
    ];

    // Act / Assert
    expect(sorted(rows, null)).toEqual(['WEB-4', 'WEB-1', 'WEB-2', 'WEB-3']);
  });

  it('到期日遞減：晚的在前，沒有到期日的照樣在最後', () => {
    // Arrange
    const rows = [row({ number: 1 }), row({ number: 2, dueDate: '2026-11-18' }), row({ number: 3, dueDate: '2026-11-20' })];

    // Act / Assert
    expect(sorted(rows, { field: 'due_date', direction: 'desc' })).toEqual(['WEB-3', 'WEB-2', 'WEB-1']);
  });

  it('優先度：遞增由低到緊急、遞減反過來，沒設的兩個方向都在最後', () => {
    // Arrange
    const rows = [
      row({ number: 1, priority: 'none' }),
      row({ number: 2, priority: 'urgent' }),
      row({ number: 3, priority: 'low' }),
      row({ number: 4, priority: 'high' }),
    ];

    // Act / Assert
    expect(sorted(rows, { field: 'priority', direction: 'asc' })).toEqual(['WEB-3', 'WEB-4', 'WEB-2', 'WEB-1']);
    expect(sorted(rows, { field: 'priority', direction: 'desc' })).toEqual(['WEB-2', 'WEB-4', 'WEB-3', 'WEB-1']);
  });

  it('狀態照類別的先後（待規劃、未開始、進行中、完成、取消），同類別照狀態的 key', () => {
    // Arrange
    const rows = [
      row({ number: 1, state: 'done', stateCategory: 'done' }),
      row({ number: 2, state: 'review', stateCategory: 'in_progress' }),
      row({ number: 3, state: 'todo', stateCategory: 'not_started' }),
      row({ number: 4, state: 'doing', stateCategory: 'in_progress' }),
    ];

    // Act / Assert
    expect(sorted(rows, { field: 'state', direction: 'asc' })).toEqual(['WEB-3', 'WEB-4', 'WEB-2', 'WEB-1']);
    expect(sorted(rows, { field: 'state', direction: 'desc' })).toEqual(['WEB-1', 'WEB-2', 'WEB-4', 'WEB-3']);
  });

  it('標題照字的順序，同名時照到期日再照編號', () => {
    // Arrange
    const rows = [
      row({ number: 1, title: 'beta' }),
      row({ number: 2, title: 'Alpha' }),
      row({ number: 3, title: 'beta', dueDate: '2026-11-18' }),
    ];

    // Act / Assert
    expect(sorted(rows, { field: 'title', direction: 'asc' })).toEqual(['WEB-2', 'WEB-3', 'WEB-1']);
  });

  it('編號照專案代號再照號碼（數字的大小，不是字的順序）', () => {
    // Arrange
    const rows = [row({ number: 10 }), row({ projectKey: 'APP', number: 5 }), row({ number: 9 })];

    // Act / Assert
    expect(sorted(rows, { field: 'number', direction: 'asc' })).toEqual(['APP-5', 'WEB-9', 'WEB-10']);
    expect(sorted(rows, { field: 'number', direction: 'desc' })).toEqual(['WEB-10', 'WEB-9', 'APP-5']);
  });

  it('專案照畫面上的名稱（中文的排序：中文在英文前面）；名稱還沒讀到的用代號', () => {
    // Arrange
    const rows = [row({ projectKey: 'ZZZ', number: 1 }), row({ projectKey: 'APP', number: 1 }), row({ projectKey: 'WEB', number: 1 })];

    // Act / Assert
    expect(sorted(rows, { field: 'project', direction: 'asc' })).toEqual(['WEB-1', 'APP-1', 'ZZZ-1']);
    expect(sorted(rows, { field: 'project', direction: 'desc' })).toEqual(['ZZZ-1', 'APP-1', 'WEB-1']);
  });

  it('看的是列記下的位置（停在原位），不是改過之後的值', () => {
    // Arrange：WEB-1 在這一頁改成緊急，位置還是原本的「低」。
    const placed = makeItem({ number: 1, priority: 'low' });
    const changed = { ...placed, priority: 'urgent' as const };
    const rows = [rowOf(changed, placed), row({ number: 2, priority: 'high' })];

    // Act / Assert
    expect(sorted(rows, { field: 'priority', direction: 'desc' })).toEqual(['WEB-2', 'WEB-1']);
  });
});
