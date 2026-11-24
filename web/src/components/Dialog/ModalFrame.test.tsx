import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ModalFrame } from './ModalFrame';

function Frame(props: Partial<React.ComponentProps<typeof ModalFrame>>) {
  return (
    <ModalFrame labelledBy="title" {...props}>
      <h2 id="title">標題</h2>
      <button type="button">裡面</button>
    </ModalFrame>
  );
}

function backdropOf(): HTMLElement {
  return screen.getByRole('dialog').parentElement as HTMLElement;
}

describe('ModalFrame：背後的頁面 inert', () => {
  it('isBackgroundInert：body 底下原本的內容標 inert，之後才出現的不標，卸下時還原', () => {
    // Arrange
    const page = document.createElement('div');
    document.body.append(page);
    const { unmount } = render(<Frame isBackgroundInert />);
    const later = document.createElement('div');
    document.body.append(later);

    // Assert
    expect(page).toHaveAttribute('inert');
    expect(later).not.toHaveAttribute('inert');
    expect(backdropOf()).not.toHaveAttribute('inert');

    // Act
    unmount();

    // Assert
    expect(page).not.toHaveAttribute('inert');
    page.remove();
    later.remove();
  });

  it('預設不標 inert', () => {
    // Arrange
    const page = document.createElement('div');
    document.body.append(page);

    // Act
    render(<Frame />);

    // Assert
    expect(page).not.toHaveAttribute('inert');
    page.remove();
  });
});

describe('ModalFrame：點背景', () => {
  it('按下與放開都在背景才算', () => {
    // Arrange
    const onBackdropClick = vi.fn();
    render(<Frame onBackdropClick={onBackdropClick} />);
    const backdrop = backdropOf();

    // Act
    fireEvent.mouseDown(backdrop);
    fireEvent.mouseUp(backdrop);
    fireEvent.click(backdrop);

    // Assert
    expect(onBackdropClick).toHaveBeenCalledTimes(1);
  });

  it('按下在背景、放開在視窗裡面不算（click 發在共同祖先，目標是背景）', () => {
    // Arrange
    const onBackdropClick = vi.fn();
    render(<Frame onBackdropClick={onBackdropClick} />);
    const backdrop = backdropOf();

    // Act
    fireEvent.mouseDown(backdrop);
    fireEvent.mouseUp(screen.getByRole('button', { name: '裡面' }));
    fireEvent.click(backdrop);

    // Assert
    expect(onBackdropClick).not.toHaveBeenCalled();
  });

  it('按下在視窗裡面、放開在背景不算', () => {
    // Arrange
    const onBackdropClick = vi.fn();
    render(<Frame onBackdropClick={onBackdropClick} />);
    const backdrop = backdropOf();

    // Act
    fireEvent.mouseDown(screen.getByRole('button', { name: '裡面' }));
    fireEvent.mouseUp(backdrop);
    fireEvent.click(backdrop);

    // Assert
    expect(onBackdropClick).not.toHaveBeenCalled();
  });
});

describe('ModalFrame：鎖住背後的捲動', () => {
  it('locksBackgroundScroll：背景上的滾輪與觸控移動擋下，視窗裡面的照常', () => {
    // Arrange
    render(<Frame locksBackgroundScroll />);
    const backdrop = backdropOf();

    // Act
    const wheelOnBackdrop = new WheelEvent('wheel', { bubbles: true, cancelable: true });
    backdrop.dispatchEvent(wheelOnBackdrop);
    const touchOnBackdrop = new Event('touchmove', { bubbles: true, cancelable: true });
    backdrop.dispatchEvent(touchOnBackdrop);
    const wheelInside = new WheelEvent('wheel', { bubbles: true, cancelable: true });
    screen.getByRole('button', { name: '裡面' }).dispatchEvent(wheelInside);

    // Assert
    expect(wheelOnBackdrop.defaultPrevented).toBe(true);
    expect(touchOnBackdrop.defaultPrevented).toBe(true);
    expect(wheelInside.defaultPrevented).toBe(false);
  });

  it('預設不擋', () => {
    // Arrange
    render(<Frame />);

    // Act
    const wheel = new WheelEvent('wheel', { bubbles: true, cancelable: true });
    backdropOf().dispatchEvent(wheel);

    // Assert
    expect(wheel.defaultPrevented).toBe(false);
  });
});
