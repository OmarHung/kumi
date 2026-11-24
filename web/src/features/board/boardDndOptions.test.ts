import { afterEach, describe, expect, it, vi } from 'vitest';
import { boardAutoScroll, boardDropAnimation } from './boardDnd';

/** 只換掉讀設計變數的那一個方法：getComputedStyle 回的物件，其他屬性用不到。 */
function stubTokens(tokens: Readonly<Record<string, string>>) {
  vi.spyOn(window, 'getComputedStyle').mockReturnValue({
    getPropertyValue: (name: string) => tokens[name] ?? '',
  } as CSSStyleDeclaration);
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('拖曳的自動捲動', () => {
  it('只有看板（欄的橫向捲動容器）會自動捲：欄自己的直向捲動容器與頁面都不會', () => {
    // Arrange
    const lanes = document.createElement('div');
    const lane = document.createElement('div');
    lanes.append(lane);

    // Act
    const options = boardAutoScroll(false, lanes);

    // Assert
    expect(options.canScroll?.(lanes)).toBe(true);
    expect(options.canScroll?.(lane)).toBe(false);
    expect(options.canScroll?.(document.documentElement)).toBe(false);
  });

  it('看板還沒掛上時什麼都不捲', () => {
    // Arrange / Act
    const options = boardAutoScroll(false, null);

    // Assert
    expect(options.canScroll?.(document.documentElement)).toBe(false);
  });

  it('窄螢幕橫向的觸發範圍比寬螢幕寬（下一欄只露出一點邊）', () => {
    // Arrange / Act
    const wide = boardAutoScroll(false, null).threshold;
    const compact = boardAutoScroll(true, null).threshold;

    // Assert
    expect(compact?.x).toBeGreaterThan(wide?.x ?? 1);
  });
});

describe('放下時的動畫', () => {
  it('時間與曲線讀設計變數 --duration-normal 與 --ease-out', () => {
    // Arrange
    stubTokens({ '--duration-normal': ' 0.25s', '--ease-out': ' linear' });

    // Act
    const animation = boardDropAnimation();

    // Assert
    expect(animation).toEqual({ duration: 250, easing: 'linear' });
  });

  it('讀不到設計變數（測試環境沒有載入 CSS）時用同一組預設的數字', () => {
    // Arrange
    stubTokens({});

    // Act
    const animation = boardDropAnimation();

    // Assert
    expect(animation).toEqual({ duration: 200, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)' });
  });

  it('使用者要求減少動態時不滑', () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query === '(prefers-reduced-motion: reduce)' }));

    // Act
    const animation = boardDropAnimation();

    // Assert
    expect(animation).toBeNull();
  });
});
