import { describe, expect, it } from 'vitest';
import {
  entryReturnTo,
  isSameSettingsPage,
  keepPanelItem,
  panelItemOf,
  pathWithoutPanelItem,
  projectKeyOfItemKey,
  returnToProject,
  searchWithPanelItem,
  settingsPlaceOf,
  settingsSectionPath,
} from './itemLinks';

describe('網址上的項目面板', () => {
  it.each([
    ['有 item 參數', '?view=board&item=AGY-3', 'AGY-3'],
    ['沒有', '?view=board', null],
    ['空白的不算', '?item=%20', null],
  ] as const)('%s', (_name, search, expected) => {
    // Act & Assert
    expect(panelItemOf(new URLSearchParams(search))).toBe(expected);
  });

  it.each([
    ['加在其他參數後面，原本的寫法不動', '?view=board&q=首頁', 'AGY-3', '?view=board&q=首頁&item=AGY-3'],
    ['換掉原本的項目', '?item=AGY-3&view=board', 'AGY-4', '?view=board&item=AGY-4'],
    ['沒有其他參數', '', 'AGY-3', '?item=AGY-3'],
    ['拿掉', '?view=board&item=AGY-3&q=首頁', null, '?view=board&q=首頁'],
    ['拿掉之後什麼都沒有', '?item=AGY-3', null, ''],
    ['編號照網址編碼', '', 'A B-1', '?item=A%20B-1'],
  ] as const)('%s', (_name, search, itemKey, expected) => {
    // Act & Assert
    expect(searchWithPanelItem(search, itemKey)).toBe(expected);
  });

  it('換條件時面板留著', () => {
    // Arrange
    const next = new URLSearchParams([['view', 'board'], ['q', '首頁']]);

    // Act & Assert
    expect(keepPanelItem(next, new URLSearchParams('?view=board&item=AGY-3')).toString()).toBe(
      'view=board&q=%E9%A6%96%E9%A0%81&item=AGY-3',
    );
    expect(keepPanelItem(next, new URLSearchParams('?view=board')).toString()).toBe('view=board&q=%E9%A6%96%E9%A0%81');
  });

  it('連到整頁時記下的網址不帶面板', () => {
    // Act & Assert
    expect(pathWithoutPanelItem('/projects/AGY', '?view=board&item=AGY-3')).toBe('/projects/AGY?view=board');
    expect(pathWithoutPanelItem('/my-work', '?item=AGY-3')).toBe('/my-work');
  });

  it.each([
    ['一般的編號', 'AGY-3', 'AGY'],
    ['代號裡有連字號', 'A-B-12', 'A-B'],
    ['小寫的寫法', 'agy-3', 'AGY'],
    ['沒有連字號', 'AGY', 'AGY'],
  ] as const)('從編號猜專案代號：%s', (_name, itemKey, expected) => {
    // Act & Assert
    expect(projectKeyOfItemKey(itemKey)).toBe(expected);
  });
});

describe('連進來時記下的專案頁', () => {
  it.each([
    ['這個專案的看板', { returnTo: '/projects/AGY?view=board' }, '/projects/AGY?view=board'],
    ['這個專案的表格', { returnTo: '/projects/AGY' }, '/projects/AGY'],
    ['我的工作不是專案頁', { returnTo: '/my-work' }, null],
    ['沒有記下', undefined, null],
  ] as const)('%s', (_name, state, expected) => {
    // Act & Assert
    expect(entryReturnTo('AGY', state)).toBe(expected);
  });
});

describe('刪除項目之後回到哪裡', () => {
  it.each([
    ['從看板來的回到看板，條件照舊', { returnTo: '/projects/AGY?view=board&q=首頁' }, '/projects/AGY?view=board&q=首頁'],
    ['從表格來的回到表格', { returnTo: '/projects/AGY' }, '/projects/AGY'],
    ['不知道從哪裡來的回到表格', null, '/projects/AGY'],
    ['別的專案的網址不算', { returnTo: '/projects/AGYX?view=board' }, '/projects/AGY'],
    ['項目頁自己不算（已經刪掉了）', { returnTo: '/projects/AGY/items/AGY-7' }, '/projects/AGY'],
    ['形狀不對的不算', { returnTo: 42 }, '/projects/AGY'],
  ] as const)('%s', (_name, state, expected) => {
    // Act & Assert
    expect(returnToProject('AGY', state)).toBe(expected);
  });
});

describe('設定頁的區塊網址', () => {
  it('每個區塊一個網址', () => {
    // Act & Assert
    expect(settingsSectionPath('AGY', 'workflows')).toBe('/projects/AGY/settings/workflows');
    expect(settingsSectionPath('AGY', 'roles')).toBe('/projects/AGY/settings/roles');
  });

  it.each([
    ['設定頁本身（還沒轉到區塊）', '/projects/AGY/settings', { projectKey: 'AGY', section: null }],
    ['結尾多一條斜線也算', '/projects/AGY/settings/', { projectKey: 'AGY', section: null }],
    ['區塊', '/projects/AGY/settings/types', { projectKey: 'AGY', section: 'types' }],
    ['代號照網址解碼', '/projects/A%20B/settings/fields', { projectKey: 'A B', section: 'fields' }],
    ['不認得的區塊不是設定頁', '/projects/AGY/settings/views', null],
    ['區塊底下再多一層不是設定頁', '/projects/AGY/settings/types/extra', null],
    ['專案頁不是設定頁', '/projects/AGY', null],
    ['變更集不是設定頁', '/projects/AGY/changesets', null],
    ['代號的編碼壞掉不算', '/projects/%E0%A4%A/settings/types', null],
  ] as const)('%s', (_name, pathname, expected) => {
    // Act & Assert
    expect(settingsPlaceOf(pathname)).toEqual(expected);
  });

  it.each([
    ['同一個專案的兩個區塊', '/projects/AGY/settings/workflows', '/projects/AGY/settings/fields', true],
    ['區塊與設定頁本身', '/projects/AGY/settings/roles', '/projects/AGY/settings', true],
    ['別的專案的設定頁', '/projects/AGY/settings/types', '/projects/SMK/settings/types', false],
    ['離開設定頁', '/projects/AGY/settings/types', '/projects/AGY', false],
    ['到變更集', '/projects/AGY/settings/types', '/projects/AGY/changesets/3', false],
  ] as const)('%s', (_name, from, to, expected) => {
    // Act & Assert
    expect(isSameSettingsPage(from, to)).toBe(expected);
  });
});
