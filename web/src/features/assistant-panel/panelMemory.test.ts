import { afterEach, describe, expect, it } from 'vitest';
import { clearPanelMemory, PANEL_PROJECT_STORAGE_KEY, readStoredProject, storeProject } from './panelMemory';

afterEach(() => {
  localStorage.clear();
});

describe('面板記住的專案', () => {
  it('什麼都沒記時是 undefined（還沒選）', () => {
    expect(readStoredProject()).toBeUndefined();
  });

  it('記了專案代號就讀回代號', () => {
    storeProject('WEB');
    expect(readStoredProject()).toBe('WEB');
  });

  it('「不選專案」記成 null，和還沒選分得開', () => {
    storeProject(null);
    expect(readStoredProject()).toBeNull();
    expect(localStorage.getItem(PANEL_PROJECT_STORAGE_KEY)).not.toBeNull();
  });

  it('登出時清掉，回到還沒選', () => {
    storeProject(null);
    clearPanelMemory();
    expect(readStoredProject()).toBeUndefined();
  });
});
