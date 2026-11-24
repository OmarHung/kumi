import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { findEditor } from '../../test/richText';
import { EditorHarness } from '../../test/richTextHarness';

/** jsdom 把沒打開的 [popover] 當成看不見（所以下面查詢要 hidden: true）。Floating 載入時就決定要不要用 top layer：jsdom 沒有 showPopover，要在 import 之前補上，才驗得到「先打開、再放焦點」。 */
const timeline = vi.hoisted(() => {
  const events: string[] = [];
  const proto = HTMLElement.prototype as unknown as Record<string, unknown>;
  proto.showPopover = function showPopover(this: HTMLElement) {
    events.push('showPopover');
  };
  const originalMatches = Object.getOwnPropertyDescriptor(Element.prototype, 'matches')?.value as (this: Element, selector: string) => boolean;
  Object.defineProperty(Element.prototype, 'matches', {
    configurable: true,
    value(this: Element, selector: string) {
      return selector === ':popover-open' ? false : originalMatches.call(this, selector);
    },
  });
  return events;
});

describe('連結小框的焦點', () => {
  it('小框真的打開（showPopover）之後，焦點才放進網址欄', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<EditorHarness />);
    const editor = await findEditor('說明');
    await user.click(editor);
    timeline.length = 0;
    const originalFocus = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'focus')?.value as (this: HTMLElement, options?: FocusOptions) => void;
    const focus = vi.spyOn(HTMLInputElement.prototype, 'focus').mockImplementation(function focus(this: HTMLInputElement, options?: FocusOptions) {
      timeline.push(`focus:${this.id}`);
      originalFocus.call(this, options);
    });

    // Act
    await user.keyboard('{Control>}k{/Control}');
    await screen.findByRole('dialog', { name: '連結', hidden: true });

    // Assert
    const order = timeline.filter((entry) => entry === 'showPopover' || entry.startsWith('focus:'));
    expect(order[0]).toBe('showPopover');
    expect(order.some((entry) => entry.startsWith('focus:'))).toBe(true);
    focus.mockRestore();
    expect(screen.getByRole('textbox', { name: '網址', hidden: true })).toHaveFocus();
  });
});
