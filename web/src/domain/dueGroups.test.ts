import { describe, expect, it } from 'vitest';
import { compareDueThenKey, dayOfWeekIndex, DUE_GROUPS, dueGroupOf, endOfWeek, groupByDue, startOfWeek } from './dates';

/** 2026-11-18 是週三；2026-11-16 是週一；2026-11-22 是週日。 */
const WEDNESDAY = '2026-11-18';
const MONDAY = '2026-11-16';
const SUNDAY = '2026-11-22';

function entry(projectKey: string, number: number, dueDate: string | null) {
  return { key: `${projectKey}-${String(number)}`, projectKey, number, dueDate };
}

describe('endOfWeek：一週從週一開始，這一週的最後一天是週日', () => {
  it.each([
    [MONDAY, '2026-11-22'],
    [WEDNESDAY, '2026-11-22'],
    ['2026-11-21', '2026-11-22'],
    [SUNDAY, '2026-11-22'],
    ['2026-11-30', '2026-12-06'],
    ['2026-12-31', '2027-01-03'],
  ])('%s 那一週的週日是 %s', (todayDate, expected) => {
    // Arrange / Act
    const end = endOfWeek(todayDate, 'monday');

    // Assert
    expect(end).toBe(expected);
  });
});

describe('endOfWeek：一週從週日開始，這一週的最後一天是週六', () => {
  it.each([
    [SUNDAY, '2026-11-28'],
    [MONDAY, '2026-11-21'],
    [WEDNESDAY, '2026-11-21'],
    ['2026-11-21', '2026-11-21'],
    ['2026-12-31', '2027-01-02'],
  ])('%s 那一週的週六是 %s', (todayDate, expected) => {
    // Arrange / Act
    const end = endOfWeek(todayDate, 'sunday');

    // Assert
    expect(end).toBe(expected);
  });
});

describe('startOfWeek 與 dayOfWeekIndex', () => {
  it('週一開始：週日是第 6 天、這一週從上個週一起；週日開始：週日是第 0 天、就是這一週的第一天', () => {
    // Arrange / Act / Assert
    expect(dayOfWeekIndex(SUNDAY, 'monday')).toBe(6);
    expect(startOfWeek(SUNDAY, 'monday')).toBe('2026-11-16');
    expect(dayOfWeekIndex(SUNDAY, 'sunday')).toBe(0);
    expect(startOfWeek(SUNDAY, 'sunday')).toBe(SUNDAY);
    expect(startOfWeek('2026-11-21', 'sunday')).toBe('2026-11-15');
  });
});

describe('dueGroupOf 照偏好：週日算哪一週', () => {
  it('今天是週三：週六在「這一週」，下個週日在「之後」（週日開始）；週一開始時下個週日在「這一週」', () => {
    // Arrange / Act / Assert
    expect(dueGroupOf('2026-11-21', WEDNESDAY, 'sunday')).toBe('thisWeek');
    expect(dueGroupOf(SUNDAY, WEDNESDAY, 'sunday')).toBe('later');
    expect(dueGroupOf(SUNDAY, WEDNESDAY, 'monday')).toBe('thisWeek');
  });

  it('今天是週日：週日開始時整個星期都在「這一週」，週一開始時只有今天', () => {
    // Arrange / Act / Assert
    expect(dueGroupOf('2026-11-28', SUNDAY, 'sunday')).toBe('thisWeek');
    expect(dueGroupOf('2026-11-28', SUNDAY, 'monday')).toBe('later');
  });
});

describe('groupByDue 照偏好', () => {
  it('同樣的項目，週日開始與週一開始分到不同的組', () => {
    // Arrange
    const items = [entry('WEB', 1, SUNDAY)];

    // Act
    const sundayStart = groupByDue(items, WEDNESDAY, 'sunday').map((group) => group.group);
    const mondayStart = groupByDue(items, WEDNESDAY, 'monday').map((group) => group.group);

    // Assert
    expect(sundayStart).toEqual(['later']);
    expect(mondayStart).toEqual(['thisWeek']);
  });
});

describe('dueGroupOf：照到期日離今天多近分組', () => {
  it.each([
    ['2026-11-17', 'overdue'],
    ['2026-10-01', 'overdue'],
    [WEDNESDAY, 'today'],
    ['2026-11-19', 'thisWeek'],
    [SUNDAY, 'thisWeek'],
    ['2026-11-23', 'later'],
    ['2027-03-01', 'later'],
    [null, 'none'],
  ] as const)('今天是週三，到期日 %s 是 %s', (dueDate, expected) => {
    // Arrange / Act
    const group = dueGroupOf(dueDate, WEDNESDAY, 'monday');

    // Assert
    expect(group).toBe(expected);
  });

  it('今天是週日：這一週只剩今天，明天（週一）就是之後', () => {
    // Arrange / Act
    const tomorrow = dueGroupOf('2026-11-23', SUNDAY, 'monday');
    const todayGroup = dueGroupOf(SUNDAY, SUNDAY, 'monday');

    // Assert
    expect(todayGroup).toBe('today');
    expect(tomorrow).toBe('later');
  });

  it('今天是週一：上週日已經過期，這週日還在這一週', () => {
    // Arrange / Act
    const lastSunday = dueGroupOf('2026-11-15', MONDAY, 'monday');
    const thisSunday = dueGroupOf(SUNDAY, MONDAY, 'monday');

    // Assert
    expect(lastSunday).toBe('overdue');
    expect(thisSunday).toBe('thisWeek');
  });
});

describe('compareDueThenKey：先照到期日，再照編號', () => {
  it('到期日早的在前，沒有到期日的在最後；同一天照專案代號再照號碼（數字大小，不是字串）', () => {
    // Arrange
    const items = [
      entry('WEB', 10, '2026-11-20'),
      entry('WEB', 2, '2026-11-20'),
      entry('APP', 9, '2026-11-20'),
      entry('WEB', 1, null),
      entry('WEB', 4, '2026-11-19'),
    ];

    // Act
    const keys = items.toSorted(compareDueThenKey).map((item) => item.key);

    // Assert
    expect(keys).toEqual(['WEB-4', 'APP-9', 'WEB-2', 'WEB-10', 'WEB-1']);
  });
});

describe('groupByDue', () => {
  it('組照固定的順序（已過期、今天、這一週、之後、沒有到期日），空的組不出現，組內照到期日再照編號', () => {
    // Arrange
    const items = [
      entry('WEB', 8, '2026-12-01'),
      entry('WEB', 5, null),
      entry('WEB', 3, '2026-11-16'),
      entry('APP', 7, '2026-11-16'),
      entry('WEB', 1, WEDNESDAY),
      entry('APP', 2, '2026-11-10'),
    ];

    // Act
    const groups = groupByDue(items, WEDNESDAY, 'monday');

    // Assert
    expect(groups.map((group) => [group.group, group.items.map((item) => item.key)])).toEqual([
      ['overdue', ['APP-2', 'APP-7', 'WEB-3']],
      ['today', ['WEB-1']],
      ['later', ['WEB-8']],
      ['none', ['WEB-5']],
    ]);
  });

  it('沒有項目時沒有任何一組', () => {
    // Arrange / Act
    const groups = groupByDue([], WEDNESDAY, 'monday');

    // Assert
    expect(groups).toEqual([]);
  });

  it('不改傳進來的陣列', () => {
    // Arrange
    const items = [entry('WEB', 2, null), entry('WEB', 1, '2026-11-17')];
    const before = items.map((item) => item.key);

    // Act
    groupByDue(items, WEDNESDAY, 'monday');

    // Assert
    expect(items.map((item) => item.key)).toEqual(before);
  });

  it('DUE_GROUPS 是組的順序', () => {
    // Arrange / Act / Assert
    expect(DUE_GROUPS).toEqual(['overdue', 'today', 'thisWeek', 'later', 'none']);
  });
});
