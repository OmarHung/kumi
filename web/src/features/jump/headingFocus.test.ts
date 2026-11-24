import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { watchForHeading } from './headingFocus';

/** MutationObserver 的通知是 microtask：等它跑完。 */
async function flushMutations() {
  await Promise.resolve();
  await Promise.resolve();
}

function addHeading(root: HTMLElement, text = '我的工作'): HTMLHeadingElement {
  const heading = document.createElement('h1');
  heading.textContent = text;
  root.append(heading);
  return heading;
}

function spyOnScrollTo() {
  return vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
}

describe('watchForHeading', () => {
  let root: HTMLElement;

  let scrollTo: ReturnType<typeof spyOnScrollTo>;

  beforeEach(() => {
    scrollTo = spyOnScrollTo();
    root = document.createElement('main');
    document.body.append(root);
  });

  afterEach(() => {
    root.remove();
  });

  it('標題已經在：馬上把焦點放上去並捲回最上面；沒有 tabIndex 的補上 -1', () => {
    // Arrange
    const heading = addHeading(root);

    // Act
    watchForHeading(root);

    // Assert
    expect(heading).toHaveFocus();
    expect(heading).toHaveAttribute('tabindex', '-1');
    expect(scrollTo).toHaveBeenCalledWith({ top: 0 });
  });

  it('標題原本就有 tabIndex 時不改', () => {
    // Arrange
    const heading = addHeading(root);
    heading.tabIndex = 0;

    // Act
    watchForHeading(root);

    // Assert
    expect(heading).toHaveAttribute('tabindex', '0');
    expect(heading).toHaveFocus();
  });

  it('標題晚一點才出現（還在載入）：出現時才放焦點', async () => {
    // Arrange
    watchForHeading(root);

    // Act
    const heading = addHeading(root);
    await flushMutations();

    // Assert
    expect(heading).toHaveFocus();
  });

  it('等的期間使用者按了鍵或點了東西：不搶焦點', async () => {
    // Arrange
    watchForHeading(root);
    const other = document.createElement('button');
    document.body.append(other);
    other.focus();

    // Act
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab' }));
    addHeading(root);
    await flushMutations();

    // Assert
    expect(other).toHaveFocus();
    other.remove();
  });

  it('點了東西也一樣不搶', async () => {
    // Arrange
    watchForHeading(root);

    // Act
    document.dispatchEvent(new Event('pointerdown'));
    const heading = addHeading(root);
    await flushMutations();

    // Assert
    expect(heading).not.toHaveFocus();
  });

  it('等太久就放棄', async () => {
    // Arrange
    vi.useFakeTimers();
    watchForHeading(root, 1000);

    // Act
    vi.advanceTimersByTime(1000);
    const heading = addHeading(root);
    await flushMutations();
    vi.useRealTimers();

    // Assert
    expect(heading).not.toHaveFocus();
  });

  it('呼叫回傳的函式就不再等', async () => {
    // Arrange
    const stop = watchForHeading(root);

    // Act
    stop();
    const heading = addHeading(root);
    await flushMutations();

    // Assert
    expect(heading).not.toHaveFocus();
  });
});
