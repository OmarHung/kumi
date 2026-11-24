import { describe, expect, it } from 'vitest';
import { clampPanelSize, toastPlacement } from './panelSize';

const DEFAULT = { width: 400, height: 600 };

describe('toastPlacement：面板開著時新通知的提示讓到哪裡', () => {
  it('左邊放得下一則提示：讓到面板左邊', () => {
    expect(toastPlacement(DEFAULT, { width: 1440, height: 900 }, false)).toBe('left');
  });

  it('48～64rem 之間左邊放不下：疊在面板上面', () => {
    expect(toastPlacement(DEFAULT, { width: 800, height: 900 }, false)).toBe('above');
  });

  it('面板拉大、左邊放不下：改疊在上面或放到上緣，不被推出畫面', () => {
    expect(toastPlacement({ width: 1000, height: 600 }, { width: 1440, height: 900 }, false)).toBe('above');
    expect(toastPlacement({ width: 1000, height: 840 }, { width: 1440, height: 900 }, false)).toBe('top');
  });

  it('全螢幕的面板：放到畫面上緣', () => {
    expect(toastPlacement(DEFAULT, { width: 390, height: 844 }, true)).toBe('top');
  });
});

describe('clampPanelSize', () => {
  it('不小過 320×400，不大過畫面減邊距', () => {
    expect(clampPanelSize({ width: 10, height: 10 }, { width: 1440, height: 900 })).toEqual({ width: 320, height: 400 });
    expect(clampPanelSize({ width: 9999, height: 9999 }, { width: 1440, height: 900 })).toEqual({ width: 1392, height: 852 });
  });
});
