import { afterEach, describe, expect, it, vi } from 'vitest';
import { revealInScrollParent, scrollTargetIntoView } from './scrollToTarget';

interface Box {
  readonly top: number;
  readonly height: number;
}

/** jsdom 沒有版面：位置與高度用 stub 給。 */
function place(element: HTMLElement, box: Box, scroll?: { readonly scrollHeight: number; readonly scrollTop: number }): void {
  element.getBoundingClientRect = () => ({ top: box.top, height: box.height }) as DOMRect;
  Object.defineProperty(element, 'offsetHeight', { configurable: true, value: box.height });
  Object.defineProperty(element, 'clientHeight', { configurable: true, value: box.height });
  if (scroll !== undefined) {
    Object.defineProperty(element, 'scrollHeight', { configurable: true, value: scroll.scrollHeight });
    element.scrollTop = scroll.scrollTop;
  }
}

function setup(containerScrollHeight: number, entryHeight: number) {
  const container = document.createElement('div');
  container.style.overflowY = 'auto';
  const entry = document.createElement('li');
  entry.style.scrollMarginBlockStart = '60px';
  container.append(entry);
  document.body.append(container);
  place(container, { top: 100, height: 400 }, { scrollHeight: containerScrollHeight, scrollTop: 50 });
  place(entry, { top: 700, height: entryHeight });
  const containerScroll = vi.fn();
  container.scrollTo = containerScroll;
  const entryScroll = vi.fn();
  entry.scrollIntoView = entryScroll;
  return { container, entry, containerScroll, entryScroll };
}

afterEach(() => {
  document.body.replaceChildren();
});

describe('scrollTargetIntoView', () => {
  it('在會捲動的容器（面板）裡：只捲那個容器、把目標置中，不用 scrollIntoView（它會連帶捲整頁）', () => {
    // Arrange
    const { entry, containerScroll, entryScroll } = setup(2000, 100);

    // Act
    scrollTargetIntoView(entry, 'smooth');

    // Assert：目標在容器內容裡的位置是 700 - 100 + 50 = 650，置中要減掉 (400 - 100) / 2。
    expect(containerScroll).toHaveBeenCalledWith({ top: 500, behavior: 'smooth' });
    expect(entryScroll).not.toHaveBeenCalled();
  });

  it('目標比容器的可見高度還高：對齊開頭，留出 scroll-margin（黏住的標題列不蓋住開頭）', () => {
    // Arrange
    const { entry, containerScroll } = setup(2000, 600);

    // Act
    scrollTargetIntoView(entry, 'auto');

    // Assert
    expect(containerScroll).toHaveBeenCalledWith({ top: 590, behavior: 'auto' });
  });

  it('overflow 是 auto 但內容沒有超出（不會捲）的祖先不算：退回 scrollIntoView 捲整頁', () => {
    // Arrange
    const { entry, containerScroll, entryScroll } = setup(400, 100);

    // Act
    scrollTargetIntoView(entry, 'smooth');

    // Assert
    expect(containerScroll).not.toHaveBeenCalled();
    expect(entryScroll).toHaveBeenCalledWith({ block: 'center', behavior: 'smooth' });
  });
});

describe('revealInScrollParent（展開的留言框整個露出來）', () => {
  it('下面被面板的底切掉：只捲面板，捲到框的底剛好在可見範圍的底', () => {
    // Arrange：可見範圍 100～500，框在 420～620（下面切掉 120）。
    const { entry, containerScroll, entryScroll } = setup(2000, 200);
    place(entry, { top: 420, height: 200 });

    // Act
    revealInScrollParent(entry, 'smooth');

    // Assert：原本捲到 50，要多捲 620 - 500 = 120。
    expect(containerScroll).toHaveBeenCalledWith({ top: 170, behavior: 'smooth' });
    expect(entryScroll).not.toHaveBeenCalled();
  });

  it('已經整個看得到：不捲', () => {
    // Arrange
    const { entry, containerScroll } = setup(2000, 200);
    place(entry, { top: 200, height: 200 });

    // Act
    revealInScrollParent(entry, 'auto');

    // Assert
    expect(containerScroll).not.toHaveBeenCalled();
  });

  it('框比可見範圍還高：對齊開頭（留出 scroll-margin），不捲過頭把開頭捲走', () => {
    // Arrange
    const { entry, containerScroll } = setup(2000, 600);
    place(entry, { top: 300, height: 600 });

    // Act
    revealInScrollParent(entry, 'auto');

    // Assert：開頭在內容裡的 300 - 100 + 50 = 250，減掉 scroll-margin 60。
    expect(containerScroll).toHaveBeenCalledWith({ top: 190, behavior: 'auto' });
  });

  it('不在會捲動的容器裡（整頁的項目頁）：用 scrollIntoView 的 nearest', () => {
    // Arrange
    const { container, entry, entryScroll } = setup(400, 200);
    container.style.overflowY = 'visible';

    // Act
    revealInScrollParent(entry, 'smooth');

    // Assert
    expect(entryScroll).toHaveBeenCalledWith({ block: 'nearest', behavior: 'smooth' });
  });

  it('下面留出 scroll-margin-block-end（右下角的 AI 助理按鈕不蓋住「送出留言」）', () => {
    // Arrange：可見範圍 100～500，框在 300～480（看得到，但離底只有 20）。
    const { entry, containerScroll } = setup(2000, 180);
    place(entry, { top: 300, height: 180 });
    entry.style.scrollMarginBlockEnd = '64px';

    // Act
    revealInScrollParent(entry, 'auto');

    // Assert：底在內容裡的 300 - 100 + 50 + 180 = 430，加上 64 要在可見範圍的底（50 + 400 = 450）之內：多捲 44。
    expect(containerScroll).toHaveBeenCalledWith({ top: 94, behavior: 'auto' });
  });
});

