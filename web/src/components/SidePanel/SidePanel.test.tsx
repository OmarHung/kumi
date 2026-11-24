import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createRef, useState, type ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubIntersectionObserver } from '../../test/intersectionObserver';
import { Menu } from '../Menu/Menu';
import { SidePanel, type SidePanelProps } from './SidePanel';
import { SIDE_PANEL_BESIDE_QUERY, SidePanelLayout } from './SidePanelLayout';

afterEach(() => {
  vi.unstubAllGlobals();
});

function renderPanel(overrides: Partial<SidePanelProps> = {}, children: ReactNode = <button type="button">改狀態</button>) {
  const user = userEvent.setup();
  const onClose = vi.fn();
  render(
    <SidePanelLayout
      panel={
        <SidePanel label="項目 AGY-3" title="AGY-3" onClose={onClose} {...overrides}>
          {children}
        </SidePanel>
      }
    >
      <ul aria-label="項目">
        <li>
          <button type="button">AGY-2</button>
        </li>
      </ul>
    </SidePanelLayout>,
  );
  return { user, onClose, panel: screen.getByRole('complementary', { name: '項目 AGY-3' }) };
}

describe('SidePanel', () => {
  it('是有名稱的 complementary 區域：標題列有標題、呼叫端的動作與關閉按鈕', () => {
    // Arrange、Act
    const { panel } = renderPanel({ headerActions: <button type="button">展開成整頁</button> });

    // Assert
    expect(within(panel).getByRole('heading', { level: 2, name: 'AGY-3' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: '展開成整頁' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: '關閉' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: '改狀態' })).toBeInTheDocument();
  });

  it('不是對話框：沒有 dialog 角色、不鎖焦點，Tab 走得出去', async () => {
    // Arrange
    const { user, panel } = renderPanel();
    within(panel).getByRole('button', { name: '改狀態' }).focus();

    // Act
    await user.tab();

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(panel).not.toContainElement(document.activeElement as HTMLElement);
  });

  it('打開時焦點到標題（tabIndex -1，Tab 不會停在它上面）；headingRef 拿得到標題', () => {
    // Arrange
    const headingRef = createRef<HTMLHeadingElement>();

    // Act
    const { panel } = renderPanel({ headingRef });

    // Assert
    const heading = within(panel).getByRole('heading', { name: 'AGY-3' });
    expect(heading).toHaveFocus();
    expect(heading).toHaveAttribute('tabindex', '-1');
    expect(headingRef.current).toBe(heading);
  });

  it('headingRef 也可以是函式', () => {
    // Arrange
    const headingRef = vi.fn();

    // Act
    renderPanel({ headingRef });

    // Assert
    expect(headingRef).toHaveBeenCalledWith(screen.getByRole('heading', { name: 'AGY-3' }));
  });

  it('標題可以是元素（類型與等寬的編號）', () => {
    // Arrange、Act
    const { panel } = renderPanel({
      title: (
        <>
          任務 <code>AGY-3</code>
        </>
      ),
    });

    // Assert
    expect(within(panel).getByRole('heading', { name: '任務 AGY-3' })).toBeInTheDocument();
  });

  it('關閉按鈕呼叫 onClose；名稱可以換', async () => {
    // Arrange
    const { user, onClose, panel } = renderPanel({ closeLabel: '關閉 AGY-3' });

    // Act
    await user.click(within(panel).getByRole('button', { name: '關閉 AGY-3' }));

    // Assert
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('Esc 關閉：焦點在面板裡、在清單上都可以', async () => {
    // Arrange
    const { user, onClose } = renderPanel();

    // Act
    await user.keyboard('{Escape}');
    screen.getByRole('button', { name: 'AGY-2' }).focus();
    await user.keyboard('{Escape}');

    // Assert
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it('Esc 的順序：開著的選單先關上，再按一次才關面板', async () => {
    // Arrange
    stubIntersectionObserver();
    const menu = (
      <Menu
        menuLabel="AGY-3 的更多動作"
        items={[{ key: 'delete', label: '刪除', onSelect: vi.fn() }]}
        renderTrigger={(trigger) => (
          <button type="button" {...trigger}>
            更多
          </button>
        )}
      />
    );
    const { user, onClose, panel } = renderPanel({}, menu);
    await user.click(within(panel).getByRole('button', { name: '更多' }));

    // Act
    await user.keyboard('{Escape}');
    const closesAfterMenu = onClose.mock.calls.length;
    await user.keyboard('{Escape}');

    // Assert
    expect(closesAfterMenu).toBe(0);
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('編輯中的格子（Esc 先取消編輯）與輸入框裡的 Esc 不關面板', () => {
    // Arrange
    const cell = (
      <>
        <button
          type="button"
          onKeyDown={(event) => {
            event.preventDefault();
          }}
        >
          標題：首頁設計稿
        </button>
        <input aria-label="留言" />
      </>
    );
    const { onClose, panel } = renderPanel({}, cell);

    // Act
    fireEvent.keyDown(within(panel).getByRole('button', { name: '標題：首頁設計稿' }), { key: 'Escape' });
    fireEvent.keyDown(within(panel).getByRole('textbox', { name: '留言' }), { key: 'Escape' });

    // Assert
    expect(onClose).not.toHaveBeenCalled();
  });

  it('卸下之後 Esc 不再呼叫', () => {
    // Arrange
    const onClose = vi.fn();
    const { unmount } = render(
      <SidePanel label="項目 AGY-3" title="AGY-3" onClose={onClose}>
        內容
      </SidePanel>,
    );

    // Act
    unmount();
    fireEvent.keyDown(document.body, { key: 'Escape' });

    // Assert
    expect(onClose).not.toHaveBeenCalled();
  });
});

function Workspace() {
  const [isOpen, setIsOpen] = useState(false);
  return (
    <SidePanelLayout
      layout="page"
      panel={
        isOpen && (
          <SidePanel
            label="項目 AGY-1"
            title="AGY-1"
            onClose={() => {
              setIsOpen(false);
            }}
          >
            內容
          </SidePanel>
        )
      }
    >
      <label>
        搜尋
        <input defaultValue="" />
      </label>
      <button
        type="button"
        onClick={() => {
          setIsOpen(true);
        }}
      >
        AGY-1
      </button>
    </SidePanelLayout>
  );
}

describe('SidePanelLayout', () => {
  it('沒有面板時只有清單；打開、關上面板時清單不重新掛載（打的字、焦點都留著）', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<Workspace />);
    const search = screen.getByRole('textbox', { name: '搜尋' });
    await user.type(search, '設計');
    const hadPanel = screen.queryByRole('complementary') !== null;

    // Act
    await user.click(screen.getByRole('button', { name: 'AGY-1' }));
    const isOpen = screen.queryByRole('complementary', { name: '項目 AGY-1' }) !== null;
    await user.keyboard('{Escape}');

    // Assert
    expect(hadPanel).toBe(false);
    expect(isOpen).toBe(true);
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: '搜尋' })).toBe(search);
    expect(search).toHaveValue('設計');
  });

  /** 讓元素在畫面上的位置是這個範圍（jsdom 沒有版面）。 */
  function placeAt(element: Element, left: number, width: number) {
    vi.spyOn(element, 'getBoundingClientRect').mockReturnValue(new DOMRect(left, 0, width, 40));
  }

  function renderCovering(onCoveredFocus: () => void) {
    render(
      <SidePanelLayout
        onCoveredFocus={onCoveredFocus}
        panel={
          <SidePanel label="項目 AGY-3" title="AGY-3" onClose={() => undefined}>
            內容
          </SidePanel>
        }
      >
        <button type="button">AGY-1</button>
        <button type="button">到期日</button>
      </SidePanelLayout>,
    );
    placeAt(screen.getByRole('complementary', { name: '項目 AGY-3' }), 600, 448);
    placeAt(screen.getByRole('button', { name: 'AGY-1' }), 0, 100);
    placeAt(screen.getByRole('button', { name: '到期日' }), 700, 100);
  }

  it('面板蓋在清單上時，焦點走到被面板蓋住的地方：通知呼叫端（收起面板），焦點才看得到', () => {
    // Arrange
    const onCoveredFocus = vi.fn();
    renderCovering(onCoveredFocus);

    // Act
    screen.getByRole('button', { name: 'AGY-1' }).focus();
    const beforeCovered = onCoveredFocus.mock.calls.length;
    screen.getByRole('button', { name: '到期日' }).focus();

    // Assert
    expect(beforeCovered).toBe(0);
    expect(onCoveredFocus).toHaveBeenCalledTimes(1);
  });

  it('面板排成右邊一欄（80rem 以上）時不蓋住清單，不必收起', () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === SIDE_PANEL_BESIDE_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    const onCoveredFocus = vi.fn();
    renderCovering(onCoveredFocus);

    // Act
    screen.getByRole('button', { name: '到期日' }).focus();

    // Assert
    expect(onCoveredFocus).not.toHaveBeenCalled();
  });
});
