import { describe, expect, it } from 'vitest';
import { appOverview, emptyOverview, opsOverview, overviewOf, webOverview } from '../../test/overview';
import { overviewLinks } from './overviewLinks';
import { overviewScopeOf, overviewSortOf, sortOverviews, withOverviewView } from './overviewView';

const keysOf = (sort: Parameters<typeof sortOverviews>[1], projects = [opsOverview, webOverview, appOverview, emptyOverview]) =>
  sortOverviews(projects, sort).map((project) => project.projectKey);

describe('總覽的網址與排序', () => {
  it('範圍與排序不認得的值當成預設', () => {
    // Arrange / Act / Assert
    expect(overviewScopeOf(new URLSearchParams('scope=everyone'))).toBe('managed');
    expect(overviewScopeOf(new URLSearchParams('scope=visible'))).toBe('visible');
    expect(overviewSortOf(new URLSearchParams('sort=banana'))).toBe('risk');
    expect(overviewSortOf(new URLSearchParams('sort=key'))).toBe('key');
  });

  it('寫回網址：預設值不寫，另一個參數照舊帶著', () => {
    // Arrange
    const params = new URLSearchParams('scope=visible&sort=key');

    // Act / Assert
    expect(withOverviewView(params, { scope: 'managed', sort: 'key' }).toString()).toBe('sort=key');
    expect(withOverviewView(params, { scope: 'visible', sort: 'risk' }).toString()).toBe('scope=visible');
    expect(withOverviewView(params, { scope: 'managed', sort: 'risk' }).toString()).toBe('');
  });

  it('依風險排：已過期加停滯多的在前，同分依代號', () => {
    // Arrange / Act / Assert
    expect(keysOf('risk')).toEqual(['APP', 'WEB', 'NEW', 'OPS']);
  });

  it('依最近更新排：沒有更新時間的在最後；不動原本的陣列', () => {
    // Arrange
    const projects = [opsOverview, appOverview, webOverview];

    // Act
    const sorted = keysOf('updated', projects);

    // Assert
    expect(sorted).toEqual(['WEB', 'APP', 'OPS']);
    expect(projects.map((project) => project.projectKey)).toEqual(['OPS', 'APP', 'WEB']);
  });

  it('沒有東西可排時不出錯', () => {
    // Arrange / Act / Assert
    expect(sortOverviews([], 'updated')).toEqual([]);
    expect(sortOverviews([overviewOf('A', 'a')], 'risk')).toHaveLength(1);
  });
});

describe('總覽的連結', () => {
  it('專案代號有特殊字元時編碼', () => {
    // Arrange / Act / Assert
    expect(overviewLinks.project('A B')).toBe('/projects/A%20B');
  });
});
