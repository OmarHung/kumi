import { beforeEach, describe, expect, it } from 'vitest';
import { clearSignedInStorage, PRESERVED_LOCAL_KEYS } from './signOutStorage';

function storedKeys(): readonly string[] {
  return Array.from({ length: localStorage.length }, (_, index) => localStorage.key(index) ?? '').sort();
}

describe('登出時清掉的瀏覽器儲存', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it('只留下純顯示偏好，帶專案、空間、連線代號的與不認得的鍵都清掉', () => {
    // Arrange
    localStorage.setItem('shell.theme', 'dark');
    localStorage.setItem('shell.sidebar', 'collapsed');
    localStorage.setItem('my-work.widths', '{}');
    localStorage.setItem('project.view.SMK', 'board');
    localStorage.setItem('item-table.columns.SMK', '{}');
    localStorage.setItem('item-table.widths.SMK', '{}');
    localStorage.setItem('knowledge.tree.expanded.HR', '[]');
    localStorage.setItem('assistant.selection', '{"connectionId":"c1"}');
    localStorage.setItem('assistant.panelProject', 'SMK');
    localStorage.setItem('jump.recentItems', '[]');
    localStorage.setItem('some.future.key', 'x');

    // Act
    clearSignedInStorage();

    // Assert
    expect(storedKeys()).toEqual(['my-work.widths', 'shell.sidebar', 'shell.theme']);
  });

  it('sessionStorage 整個清空', () => {
    // Arrange
    sessionStorage.setItem('draft.comment.SMK-1', '打到一半');
    sessionStorage.setItem('item.returnFocus', '[]');

    // Act
    clearSignedInStorage();

    // Assert
    expect(sessionStorage.length).toBe(0);
  });

  it('保留清單裡沒有任何帶代號的前綴', () => {
    // Assert
    expect([...PRESERVED_LOCAL_KEYS].filter((key) => key.endsWith('.'))).toEqual([]);
    expect(PRESERVED_LOCAL_KEYS.has('assistant.panelProject')).toBe(false);
  });
});
