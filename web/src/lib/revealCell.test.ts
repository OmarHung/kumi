import { within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { nearestDelta, revealCell, revealKindOf, scrollToRowStart } from './revealCell';

describe('revealKindOf：取得焦點的格子怎麼捲到看得到的地方', () => {
  it('一般的格子交給瀏覽器（scrollIntoView，照容器的 scroll-padding 讓出表頭與固定欄）', () => {
    expect(revealKindOf({ isHeader: false, isSticky: false })).toBe('native');
  });

  it('固定在左邊的格子（position: sticky）只修正直向，不產生橫向捲動', () => {
    expect(revealKindOf({ isHeader: false, isSticky: true })).toBe('vertical');
  });

  it('黏住的表頭不捲（一直看得到；用鍵盤走到表頭時另外把第一列捲進來）', () => {
    expect(revealKindOf({ isHeader: true, isSticky: true })).toBe('skip');
    expect(revealKindOf({ isHeader: true, isSticky: false })).toBe('skip');
  });
});

describe('nearestDelta：直向要捲多少才看得到', () => {
  it('已經在看得到的範圍裡就不捲', () => {
    expect(nearestDelta(100, 136, 40, 400)).toBe(0);
  });

  it('躲在黏住的表頭（扣掉 scroll-padding-top）上面：往上捲到剛好露出來', () => {
    expect(nearestDelta(20, 56, 40, 400)).toBe(-20);
  });

  it('在下面看不到：往下捲到剛好露出來', () => {
    expect(nearestDelta(420, 456, 40, 400)).toBe(56);
  });

  it('比看得到的範圍還高：對齊上緣', () => {
    expect(nearestDelta(300, 900, 40, 400)).toBe(260);
  });
});

/** 捲動區塊 > 表格 > 表頭一列、表身一列（第一格是固定欄）。jsdom 沒有版面與樣式，位置與計算後的樣式由測試給。 */
function buildTable() {
  const scroller = document.createElement('div');
  scroller.innerHTML = '<table><thead><tr><th>編號</th></tr></thead><tbody><tr><td><a href="#k">K-1</a></td><td>狀態</td></tr></tbody></table>';
  document.body.append(scroller);
  const table = within(scroller);
  const [keyCell, stateCell] = table.getAllByRole('cell');
  if (keyCell === undefined || stateCell === undefined) throw new Error('表格沒有建好');
  return { scroller, keyCell, stateCell, header: table.getByRole('columnheader'), keyLink: table.getByRole('link') };
}

/** 計算後的樣式換掉其中幾個屬性，其他照 jsdom 的。 */
function withStyle(style: CSSStyleDeclaration, overrides: Readonly<Record<string, string>>): CSSStyleDeclaration {
  return new Proxy(style, {
    get: (target, key): unknown => (typeof key === 'string' && key in overrides ? overrides[key] : Reflect.get(target, key)),
  });
}

function stubLayout(scroller: HTMLElement, sticky: Element) {
  const original = window.getComputedStyle.bind(window);
  vi.spyOn(window, 'getComputedStyle').mockImplementation((element) => {
    const style = original(element);
    if (element === scroller) {
      return withStyle(style, { overflowX: 'auto', overflowY: 'auto', scrollPaddingTop: '36px', scrollPaddingBottom: '0px' });
    }
    return element === sticky ? withStyle(style, { position: 'sticky' }) : style;
  });
  vi.spyOn(scroller, 'scrollHeight', 'get').mockReturnValue(2000);
  vi.spyOn(scroller, 'clientHeight', 'get').mockReturnValue(400);
  vi.spyOn(scroller, 'scrollWidth', 'get').mockReturnValue(2000);
  vi.spyOn(scroller, 'clientWidth', 'get').mockReturnValue(800);
  vi.spyOn(scroller, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ x: 0, y: 100, width: 800, height: 400 }));
}

afterEach(() => {
  document.body.replaceChildren();
  Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView');
});

describe('revealCell：真的去捲', () => {
  it('固定欄的格子只捲直向：躲在黏住的表頭底下時往上捲到剛好露出來，不呼叫 scrollIntoView', () => {
    // Arrange
    const { scroller, keyCell, keyLink } = buildTable();
    stubLayout(scroller, keyCell);
    vi.spyOn(keyCell, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ x: 0, y: 110, width: 100, height: 36 }));
    const scrollIntoView = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: scrollIntoView });
    const setScrollTop = vi.spyOn(Element.prototype, 'scrollTop', 'set');

    // Act
    revealCell(keyLink);

    // Assert：看得到的範圍從 100 + 36（表頭）開始，格子在 110，要往上捲 26
    expect(setScrollTop).toHaveBeenLastCalledWith(-26);
    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  it('一般的格子交給 scrollIntoView（nearest）；表頭不捲', () => {
    // Arrange
    const { scroller, keyCell, stateCell, header } = buildTable();
    stubLayout(scroller, keyCell);
    const scrollIntoView = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: scrollIntoView });

    // Act
    revealCell(stateCell);
    revealCell(header);

    // Assert
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
    expect(scrollIntoView.mock.contexts[0]).toBe(stateCell);
    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'nearest', inline: 'nearest' });
  });

  it('不在表格裡的元素（看板的卡片）就是元素自己 scrollIntoView', () => {
    // Arrange
    const card = document.createElement('a');
    card.href = '#card';
    document.body.append(card);
    const scrollIntoView = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: scrollIntoView });

    // Act
    revealCell(card);

    // Assert
    expect(scrollIntoView.mock.contexts[0]).toBe(card);
  });

  it('scrollToRowStart：把橫向捲得動的那一層捲回最左邊', () => {
    // Arrange
    const { scroller, keyCell } = buildTable();
    stubLayout(scroller, keyCell);
    const setScrollLeft = vi.spyOn(Element.prototype, 'scrollLeft', 'set');

    // Act
    scrollToRowStart(keyCell);

    // Assert
    expect(setScrollLeft).toHaveBeenLastCalledWith(0);
    expect(setScrollLeft.mock.contexts.at(-1)).toBe(scroller);
  });
});
