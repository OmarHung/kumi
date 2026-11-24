import { describe, expect, it } from 'vitest';
import { isPlainLeftClick } from './plainClick';

function click(init: MouseEventInit, isPrevented = false): MouseEvent {
  const event = new MouseEvent('click', { cancelable: true, ...init });
  if (isPrevented) event.preventDefault();
  return event;
}

describe('isPlainLeftClick', () => {
  it('左鍵、沒有按修飾鍵：攔下來開面板', () => {
    // Arrange、Act
    const result = isPlainLeftClick(click({ button: 0 }));

    // Assert
    expect(result).toBe(true);
  });

  it('按著 ⌘、Ctrl、Shift、Alt，或不是左鍵，或已經被處理過：照瀏覽器原本的行為走 href', () => {
    // Arrange
    const events = [
      click({ button: 0, metaKey: true }),
      click({ button: 0, ctrlKey: true }),
      click({ button: 0, shiftKey: true }),
      click({ button: 0, altKey: true }),
      click({ button: 1 }),
      click({ button: 0 }, true),
    ];

    // Act
    const results = events.map((event) => isPlainLeftClick(event));

    // Assert
    expect(results).toEqual([false, false, false, false, false, false]);
  });
});
