import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useRef, useState } from 'react';
import { describe, expect, it } from 'vitest';
import { Floating } from './Floating';

/** 按了才掛上浮層（掛上就是打開）；按鈕的寬度在打開之前設好（jsdom 沒有版面，offsetWidth 一律是 0）。 */
function Harness({ matchAnchorWidth }: { readonly matchAnchorWidth?: boolean }) {
  const anchorRef = useRef<HTMLButtonElement>(null);
  const [isOpen, setIsOpen] = useState(false);
  return (
    <>
      <button
        ref={anchorRef}
        type="button"
        onClick={() => {
          setIsOpen(true);
        }}
      >
        打開
      </button>
      {isOpen && (
        <Floating anchorRef={anchorRef} align="start" matchAnchorWidth={matchAnchorWidth}>
          <p>清單</p>
        </Floating>
      )}
    </>
  );
}

async function openWithAnchorWidth(width: number, matchAnchorWidth?: boolean) {
  const user = userEvent.setup();
  render(<Harness matchAnchorWidth={matchAnchorWidth} />);
  const anchor = screen.getByRole('button', { name: '打開' });
  Object.defineProperty(anchor, 'offsetWidth', { value: width });
  await user.click(anchor);
  return screen.getByText('清單').parentElement;
}

describe('Floating', () => {
  it('matchAnchorWidth：至少和按鈕一樣寬（寬度執行時才量得到）', async () => {
    // Arrange
    const floating = await openWithAnchorWidth(180, true);

    // Act
    const minWidth = floating?.style.minWidth;

    // Assert
    expect(minWidth).toBe('180px');
  });

  it('沒有 matchAnchorWidth 時寬度照內容', async () => {
    // Arrange
    const floating = await openWithAnchorWidth(180);

    // Act
    const minWidth = floating?.style.minWidth;

    // Assert
    expect(minWidth).toBe('');
  });
});

describe('Floating 的選項', () => {
  it('onPositioned：座標（含 maxHeight）套上之後才呼叫，下拉在這時把反白的選項捲到看得到', async () => {
    // Arrange
    const user = userEvent.setup();
    const maxHeights: string[] = [];
    function Positioned() {
      const anchorRef = useRef<HTMLButtonElement>(null);
      const floatingRef = useRef<HTMLDivElement>(null);
      const [isOpen, setIsOpen] = useState(false);
      return (
        <>
          <button
            ref={anchorRef}
            type="button"
            onClick={() => {
              setIsOpen(true);
            }}
          >
            打開
          </button>
          {isOpen && (
            <Floating
              anchorRef={anchorRef}
              align="start"
              onPositioned={() => {
                maxHeights.push(floatingRef.current?.parentElement?.style.maxHeight ?? '');
              }}
            >
              <div ref={floatingRef}>清單</div>
            </Floating>
          )}
        </>
      );
    }
    render(<Positioned />);

    // Act
    await user.click(screen.getByRole('button', { name: '打開' }));

    // Assert
    expect(maxHeights.length).toBeGreaterThan(0);
    expect(maxHeights.every((value) => value.endsWith('px'))).toBe(true);
  });

  it('keepsAnchorFocus：在浮層上按下滑鼠不改變焦點；輸入框除外（篩選框要點得進去）', () => {
    // Arrange
    function Kept() {
      const anchorRef = useRef<HTMLButtonElement>(null);
      return (
        <>
          <button ref={anchorRef} type="button">
            按鈕
          </button>
          <Floating anchorRef={anchorRef} align="start" keepsAnchorFocus>
            <p>說明</p>
            <input aria-label="篩選" />
          </Floating>
        </>
      );
    }
    render(<Kept />);
    const surface = screen.getByText('說明').parentElement;
    if (surface === null) throw new Error('沒有浮層');

    // Act
    const isPointerKept = fireEvent.pointerDown(surface);
    const isMouseKept = fireEvent.mouseDown(screen.getByText('說明'));
    const isInputKept = fireEvent.mouseDown(screen.getByRole('textbox', { name: '篩選' }));

    // Assert
    expect(isPointerKept).toBe(false);
    expect(isMouseKept).toBe(false);
    expect(isInputKept).toBe(true);
  });
});
