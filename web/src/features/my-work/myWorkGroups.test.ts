import { describe, expect, it } from 'vitest';
import { makeItem } from '../../test/fixtures';
import { groupingOf, groupingSearch, groupMyWork } from './myWorkGroups';

const WEDNESDAY = '2026-11-18';
const NAMES: Readonly<Record<string, string>> = { WEB: '官網改版', APP: '會員 App' };

function nameOf(projectKey: string): string | undefined {
  return NAMES[projectKey];
}

const overdueWeb = makeItem({ number: 3, dueDate: '2026-11-16' });
const todayApp = makeItem({ projectKey: 'APP', number: 2, dueDate: WEDNESDAY });
const undatedWeb = makeItem({ number: 5 });
const laterWeb = makeItem({ number: 1, dueDate: '2026-12-01' });

function summary(groups: ReturnType<typeof groupMyWork>) {
  return groups.map((group) => [group.label, group.items.map((item) => item.key)]);
}

describe('groupMyWork', () => {
  it('依急迫程度：組名是已過期、今天、這一週、之後、沒有到期日，空的組不出現', () => {
    // Arrange / Act
    const groups = groupMyWork([undatedWeb, laterWeb, todayApp, overdueWeb], 'urgency', WEDNESDAY, 'monday', nameOf);

    // Assert
    expect(summary(groups)).toEqual([
      ['已過期', ['WEB-3']],
      ['今天', ['APP-2']],
      ['之後', ['WEB-1']],
      ['沒有到期日', ['WEB-5']],
    ]);
    expect(groups.map((group) => group.key)).toEqual(['overdue', 'today', 'later', 'none']);
  });

  it('一週從週日開始：下個週日是「之後」；一週從週一開始它還在「這一週」', () => {
    // Arrange
    const sunday = makeItem({ number: 9, dueDate: '2026-11-22' });

    // Act
    const sundayStart = groupMyWork([sunday], 'urgency', WEDNESDAY, 'sunday', nameOf);
    const mondayStart = groupMyWork([sunday], 'urgency', WEDNESDAY, 'monday', nameOf);

    // Assert
    expect(sundayStart.map((group) => group.key)).toEqual(['later']);
    expect(mondayStart.map((group) => group.key)).toEqual(['thisWeek']);
  });

  it('依專案：照專案代號排，組名是專案名稱（還沒讀到用代號），組內照到期日再照編號', () => {
    // Arrange
    const other = makeItem({ projectKey: 'ZZZ', number: 1 });

    // Act
    const groups = groupMyWork([undatedWeb, laterWeb, todayApp, overdueWeb, other], 'project', WEDNESDAY, 'monday', nameOf);

    // Assert
    expect(summary(groups)).toEqual([
      ['會員 App', ['APP-2']],
      ['官網改版', ['WEB-3', 'WEB-1', 'WEB-5']],
      ['ZZZ', ['ZZZ-1']],
    ]);
    expect(groups.map((group) => group.key)).toEqual(['project:APP', 'project:WEB', 'project:ZZZ']);
  });
});

describe('網址的 group 參數', () => {
  it('group=project 是依專案；沒有或不認得的值是依急迫程度（預設）', () => {
    // Arrange / Act / Assert
    expect(groupingOf(new URLSearchParams('group=project'))).toBe('project');
    expect(groupingOf(new URLSearchParams(''))).toBe('urgency');
    expect(groupingOf(new URLSearchParams('group=whatever'))).toBe('urgency');
  });

  it('換分組時其他參數（開著的面板）留著；預設的分組不寫進網址', () => {
    // Arrange
    const current = new URLSearchParams('item=WEB-3');

    // Act
    const toProject = groupingSearch(current, 'project');
    const back = groupingSearch(new URLSearchParams(toProject), 'urgency');

    // Assert
    expect(toProject).toBe('?item=WEB-3&group=project');
    expect(back).toBe('?item=WEB-3');
    expect(groupingSearch(new URLSearchParams('group=project'), 'urgency')).toBe('');
  });
});
