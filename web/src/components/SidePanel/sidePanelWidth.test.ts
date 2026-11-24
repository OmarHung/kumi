import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  clampSidePanelWidth,
  measureSidePanel,
  readSidePanelWidth,
  saveSidePanelWidth,
  SIDE_PANEL_WIDTH_STORAGE_KEY,
  sidePanelBounds,
} from './sidePanelWidth';

const REM = 16;

describe('面板寬度的偏好', () => {
  it('沒有設定時是 null（用預設的寬度）', () => {
    // Arrange & Act
    const width = readSidePanelWidth();

    // Assert
    expect(width).toBeNull();
  });

  it('記在 shell.sidePanelWidth；存 null 就拿掉設定', () => {
    // Arrange & Act
    saveSidePanelWidth(520);
    const stored = localStorage.getItem(SIDE_PANEL_WIDTH_STORAGE_KEY);
    const readBack = readSidePanelWidth();
    saveSidePanelWidth(null);

    // Assert
    expect(SIDE_PANEL_WIDTH_STORAGE_KEY).toBe('shell.sidePanelWidth');
    expect(stored).toBe('520');
    expect(readBack).toBe(520);
    expect(localStorage.getItem(SIDE_PANEL_WIDTH_STORAGE_KEY)).toBeNull();
  });

  it.each(['abc', '-5', '0', 'Infinity', ''])('不認得的值（%s）當成沒有設定', (value) => {
    // Arrange
    localStorage.setItem(SIDE_PANEL_WIDTH_STORAGE_KEY, value);

    // Act & Assert
    expect(readSidePanelWidth()).toBeNull();
  });

  it('讀寫失敗時當成沒有設定，不丟出錯誤', () => {
    // Arrange
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act & Assert
    expect(readSidePanelWidth()).toBeNull();
    expect(() => {
      saveSidePanelWidth(480);
      saveSidePanelWidth(null);
    }).not.toThrow();
  });
});

describe('面板寬度的範圍', () => {
  const MOUSE = { isBeside: true, isCoarse: false };
  const OVERLAY = { isBeside: false, isCoarse: false };

  it('最窄 25rem；排成右邊一欄時最寬是清單那一塊的寬度減掉清單至少要留的 30rem', () => {
    // Arrange & Act
    const bounds = sidePanelBounds({ layoutWidth: 1200, gutter: 24, rem: REM }, MOUSE);

    // Assert
    expect(bounds).toEqual({ min: 25 * REM, max: 1200 - 30 * REM });
  });

  it('觸控時最窄 32rem：標題列的八顆按鈕是 44px，25rem 放不下類型與編號', () => {
    // Arrange & Act
    const bounds = sidePanelBounds({ layoutWidth: 1200, gutter: 24, rem: REM }, { isBeside: true, isCoarse: true });

    // Assert
    expect(bounds.min).toBe(32 * REM);
  });

  it('蓋在清單上（80rem 以下）時最寬是內容區（清單那一塊加左右的留白）減掉 16rem', () => {
    // Arrange & Act
    const bounds = sidePanelBounds({ layoutWidth: 752, gutter: 24, rem: REM }, OVERLAY);

    // Assert
    expect(bounds).toEqual({ min: 25 * REM, max: 752 + 2 * 24 - 16 * REM });
  });

  it('清單太窄時最寬不會比最窄還窄', () => {
    // Arrange & Act
    const beside = sidePanelBounds({ layoutWidth: 600, gutter: 24, rem: REM }, MOUSE);
    const overlay = sidePanelBounds({ layoutWidth: 400, gutter: 24, rem: REM }, OVERLAY);

    // Assert
    expect(beside.max).toBe(beside.min);
    expect(overlay.max).toBe(overlay.min);
  });

  it('量不到清單的寬度（沒有版面）時不設最寬', () => {
    // Arrange & Act
    const bounds = sidePanelBounds({ layoutWidth: 0, gutter: 0, rem: REM }, MOUSE);

    // Assert
    expect(bounds).toEqual({ min: 25 * REM, max: null });
  });

  it('跟著根元素的字級：放大字級時範圍一起變大', () => {
    // Arrange & Act
    const bounds = sidePanelBounds({ layoutWidth: 2000, gutter: 30, rem: 20 }, MOUSE);

    // Assert
    expect(bounds).toEqual({ min: 25 * 20, max: 2000 - 30 * 20 });
  });

  it('夾回範圍內並取整數的像素', () => {
    // Arrange
    const bounds = { min: 352, max: 720 };

    // Act & Assert
    expect(clampSidePanelWidth(100, bounds)).toBe(352);
    expect(clampSidePanelWidth(900, bounds)).toBe(720);
    expect(clampSidePanelWidth(500.6, bounds)).toBe(501);
    expect(clampSidePanelWidth(5000, { min: 352, max: null })).toBe(5000);
  });
});

describe('量清單那一塊', () => {
  afterEach(() => {
    document.documentElement.removeAttribute('style');
  });

  /** 清單那一塊在畫面上的位置；jsdom 的畫面高 768px。 */
  function layoutAt(top: number, width: number, height = 2000): HTMLElement {
    const layout = document.createElement('div');
    layout.style.columnGap = '24px';
    vi.spyOn(layout, 'getBoundingClientRect').mockReturnValue(new DOMRect(0, top, width, height));
    return layout;
  }

  it('預設寬度、頁列的高度照根元素上的變數換成像素：rem 乘上根元素的字級', () => {
    // Arrange
    document.documentElement.style.fontSize = '20px';
    document.documentElement.style.setProperty('--side-panel-width', '30rem');
    document.documentElement.style.setProperty('--page-bar-height', '3rem');

    // Act
    const measured = measureSidePanel(layoutAt(160, 1000));

    // Assert
    expect(measured).toEqual({
      layoutWidth: 1000,
      gutter: 24,
      rem: 20,
      defaultWidth: 600,
      topOffset: 160 - 60,
      bottomOffset: 0,
    });
  });

  it('變數寫成 px 時照用', () => {
    // Arrange
    document.documentElement.style.setProperty('--side-panel-width', '500px');
    document.documentElement.style.setProperty('--page-bar-height', '40px');

    // Act
    const measured = measureSidePanel(layoutAt(100, 1000));

    // Assert
    expect(measured.defaultWidth).toBe(500);
    expect(measured.topOffset).toBe(60);
  });

  it('讀不到或不認得的值：字級當成 16px，預設寬度 28rem、頁列 3rem', () => {
    // Arrange
    document.documentElement.style.setProperty('--side-panel-width', 'wide');

    // Act
    const measured = measureSidePanel(layoutAt(108, 1000));

    // Assert
    expect(measured.rem).toBe(REM);
    expect(measured.defaultWidth).toBe(28 * REM);
    expect(measured.topOffset).toBe(108 - 3 * REM);
  });

  it('清單那一塊已經捲到頁列底下時，上方沒有偏移', () => {
    // Arrange & Act
    const measured = measureSidePanel(layoutAt(-200, 1000));

    // Assert
    expect(measured.topOffset).toBe(0);
  });

  it('捲到清單那一塊的底下時，下方的偏移是畫面底下露出來、不屬於清單那一塊的高度', () => {
    // Arrange & Act：清單那一塊的底在 700px，畫面高 768px。
    const measured = measureSidePanel(layoutAt(-500, 1000, 1200));

    // Assert
    expect(measured.bottomOffset).toBe(768 - 700);
  });
});
