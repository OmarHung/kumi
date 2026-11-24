import { fireEvent, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { makeAiOptions, serveAiOptions, serveConversations } from '../../test/assistant';
import { LIST_WIDTH_STORAGE_KEY } from './listWidth';

const REM = 16;

async function openPage() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveAiOptions(makeAiOptions());
  serveConversations('SMK', []);
  const app = renderApp('/projects/SMK/assistant');
  const handle = await screen.findByRole('separator', { name: '調整對話列表寬度' });
  return { ...app, handle };
}

describe('整頁 AI 助理：對話列表的寬度', () => {
  it('分隔線是 Window Splitter：預設 16rem，範圍 12rem 到 32rem，控制對話清單', async () => {
    // Arrange / Act
    const { handle } = await openPage();

    // Assert
    expect(handle).toHaveAttribute('aria-orientation', 'vertical');
    expect(handle).toHaveAttribute('aria-valuenow', String(16 * REM));
    expect(handle).toHaveAttribute('aria-valuemin', String(12 * REM));
    expect(handle).toHaveAttribute('aria-valuemax', String(32 * REM));
    expect(handle).toHaveAttribute('aria-valuetext', '256 像素');
    expect(screen.getByRole('complementary', { name: '對話清單' })).toHaveAttribute('id', handle.getAttribute('aria-controls'));
  });

  it('→ 加寬 16px、Shift 64px、← 變窄；記在瀏覽器；不超出範圍', async () => {
    // Arrange
    const { user, handle } = await openPage();
    handle.focus();

    // Act
    await user.keyboard('{ArrowRight}{Shift>}{ArrowRight}{/Shift}{ArrowLeft}');

    // Assert
    expect(handle).toHaveAttribute('aria-valuenow', String(16 * REM + 16 + 64 - 16));
    expect(localStorage.getItem(LIST_WIDTH_STORAGE_KEY)).toBe(String(16 * REM + 64));
    await user.keyboard('{Home}');
    expect(handle).toHaveAttribute('aria-valuenow', String(12 * REM));
    await user.keyboard('{ArrowLeft}');
    expect(handle).toHaveAttribute('aria-valuenow', String(12 * REM));
    await user.keyboard('{End}');
    expect(handle).toHaveAttribute('aria-valuenow', String(32 * REM));
  });

  it('Enter 與點兩下回到預設並拿掉記錄', async () => {
    // Arrange
    localStorage.setItem(LIST_WIDTH_STORAGE_KEY, String(20 * REM));
    const { user, handle } = await openPage();
    expect(handle).toHaveAttribute('aria-valuenow', String(20 * REM));

    // Act
    handle.focus();
    await user.keyboard('{Enter}');

    // Assert
    expect(handle).toHaveAttribute('aria-valuenow', String(16 * REM));
    expect(localStorage.getItem(LIST_WIDTH_STORAGE_KEY)).toBeNull();
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 340 });
    fireEvent.pointerUp(handle, { pointerId: 1 });
    expect(handle).toHaveAttribute('aria-valuenow', String(16 * REM + 40));
    fireEvent.doubleClick(handle);
    expect(handle).toHaveAttribute('aria-valuenow', String(16 * REM));
    expect(localStorage.getItem(LIST_WIDTH_STORAGE_KEY)).toBeNull();
  });

  it('滑鼠往右拖加寬，放開才記住；拖到一半按 Esc 回到原本', async () => {
    // Arrange
    const { handle } = await openPage();

    // Act
    fireEvent.pointerDown(handle, { pointerId: 1, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 1, clientX: 350 });

    // Assert
    expect(handle).toHaveAttribute('aria-valuenow', String(16 * REM + 50));
    expect(localStorage.getItem(LIST_WIDTH_STORAGE_KEY)).toBeNull();
    fireEvent.pointerUp(handle, { pointerId: 1 });
    expect(localStorage.getItem(LIST_WIDTH_STORAGE_KEY)).toBe(String(16 * REM + 50));

    // Act: 再拖一次、按 Esc
    fireEvent.pointerDown(handle, { pointerId: 2, button: 0, clientX: 300 });
    fireEvent.pointerMove(handle, { pointerId: 2, clientX: 200 });
    fireEvent.keyDown(document, { key: 'Escape' });

    // Assert
    expect(handle).toHaveAttribute('aria-valuenow', String(16 * REM + 50));
  });

  it('下次進來用記住的寬度', async () => {
    // Arrange
    localStorage.setItem(LIST_WIDTH_STORAGE_KEY, String(18 * REM));

    // Act
    const { handle } = await openPage();

    // Assert
    expect(handle).toHaveAttribute('aria-valuenow', String(18 * REM));
  });

  it('窄螢幕：沒有分隔線', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: true,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveAiOptions(makeAiOptions());
    serveConversations('SMK', []);

    // Act
    renderApp('/projects/SMK/assistant');
    await screen.findByRole('button', { name: /對話/ });
    vi.unstubAllGlobals();

    // Assert
    expect(screen.queryByRole('separator', { name: '調整對話列表寬度' })).not.toBeInTheDocument();
  });
});
