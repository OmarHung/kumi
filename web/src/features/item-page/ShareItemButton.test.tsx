import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ShareItemButton } from './ShareItemButton';

const ITEM = { key: 'MNT-3', title: '備份排程檢查', projectKey: 'MNT' };
const URL_OF_ITEM = `${window.location.origin}/projects/MNT/items/MNT-3`;

/** 觸控裝置（pointer: coarse）。 */
function stubCoarsePointer() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === '(pointer: coarse)',
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

function stubShare(share: (data: ShareData) => Promise<void>) {
  const mock = vi.fn(share);
  Object.defineProperty(navigator, 'share', { configurable: true, value: mock });
  return mock;
}

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  Reflect.deleteProperty(navigator, 'share');
});

describe('ShareItemButton：分享項目（docs/DESIGN.md 8.3「分享項目」）', () => {
  it('點了把項目的連結（完整的網址）放進剪貼簿，唸出已複製；按鈕名稱照舊是「分享」', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<ShareItemButton item={ITEM} isCompact />);

    // Act
    await user.click(screen.getByRole('button', { name: '分享' }));

    // Assert
    expect(await navigator.clipboard.readText()).toBe(URL_OF_ITEM);
    expect(screen.getByRole('status')).toHaveTextContent('已複製 MNT-3 的連結。');
    expect(screen.getByRole('button', { name: '分享' })).toBeInTheDocument();
  });

  it('複製好了在按鈕旁邊浮出看得到的「已複製連結」（報讀軟體由 status 唸，提示本身不重複唸）', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<ShareItemButton item={ITEM} isCompact />);

    // Act
    await user.click(screen.getByRole('button', { name: '分享' }));

    // Assert
    const hint = await screen.findByText('已複製連結');
    expect(hint).toBeVisible();
    expect(hint.closest('[aria-hidden="true"]')).not.toBeNull();
  });

  it('整頁的樣子是帶字的按鈕，做的事一樣', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<ShareItemButton item={ITEM} />);

    // Act
    await user.click(screen.getByRole('button', { name: '分享' }));

    // Assert
    expect(screen.getByRole('button', { name: '分享' })).toHaveTextContent('分享');
    expect(await navigator.clipboard.readText()).toBe(URL_OF_ITEM);
  });

  it('過一下打勾的樣子收回去，唸過的那一句清掉', async () => {
    // Arrange
    const user = userEvent.setup({ advanceTimers: (ms) => vi.advanceTimersByTime(ms) });
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(<ShareItemButton item={ITEM} isCompact />);
    await user.click(screen.getByRole('button', { name: '分享' }));
    // 寫進剪貼簿是非同步的：等提示出來再看按鈕，機器慢的時候點完的當下狀態還沒換。
    await screen.findByText('已複製連結');
    expect(screen.getByRole('button', { name: '分享' })).toHaveAttribute('data-copied', 'true');

    // Act
    act(() => {
      vi.advanceTimersByTime(3000);
    });

    // Assert
    expect(screen.getByRole('button', { name: '分享' })).not.toHaveAttribute('data-copied');
    expect(screen.getByRole('status')).toHaveTextContent('');
    expect(screen.queryByText('已複製連結')).not.toBeInTheDocument();
  });

  it('觸控裝置有系統的分享時，打開系統的分享（標題是編號與標題），不複製', async () => {
    // Arrange
    stubCoarsePointer();
    const share = stubShare(() => Promise.resolve());
    const user = userEvent.setup();
    const copy = vi.spyOn(navigator.clipboard, 'writeText');
    render(<ShareItemButton item={ITEM} isCompact />);

    // Act
    await user.click(screen.getByRole('button', { name: '分享' }));

    // Assert：系統的分享自己會說，不再浮出提示
    expect(share).toHaveBeenCalledWith({ title: 'MNT-3 備份排程檢查', url: URL_OF_ITEM });
    expect(copy).not.toHaveBeenCalled();
    expect(screen.queryByText('已複製連結')).not.toBeInTheDocument();
  });

  it('在系統的分享裡按取消：什麼都不說', async () => {
    // Arrange
    stubCoarsePointer();
    stubShare(() => Promise.reject(new DOMException('取消', 'AbortError')));
    const user = userEvent.setup();
    render(<ShareItemButton item={ITEM} isCompact />);

    // Act
    await user.click(screen.getByRole('button', { name: '分享' }));

    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('桌機有系統的分享也是複製連結（桌機的人要的是貼到別的地方）', async () => {
    // Arrange
    const share = stubShare(() => Promise.resolve());
    const user = userEvent.setup();
    render(<ShareItemButton item={ITEM} isCompact />);

    // Act
    await user.click(screen.getByRole('button', { name: '分享' }));

    // Assert
    expect(share).not.toHaveBeenCalled();
    expect(await navigator.clipboard.readText()).toBe(URL_OF_ITEM);
  });

  it('剪貼簿不能用時說一聲', async () => {
    // Arrange
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));
    render(<ShareItemButton item={ITEM} isCompact />);

    // Act
    await user.click(screen.getByRole('button', { name: '分享' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('無法複製連結，請再試一次。');
    expect(screen.getByText('無法複製連結')).toBeVisible();
    expect(screen.queryByText('已複製連結')).not.toBeInTheDocument();
  });
});
