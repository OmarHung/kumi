import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createRef, type ComponentProps } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubIntersectionObserver } from '../../test/intersectionObserver';
import { Menu, type MenuItem } from './Menu';

function items(onSelect: (key: string) => void): MenuItem[] {
  const select = (key: string) => () => {
    onSelect(key);
  };
  return [
    { key: 'edit', label: '改類型', icon: 'edit', onSelect: select('edit') },
    { key: 'copy', label: '複製連結', onSelect: select('copy') },
    { key: 'archive', label: '封存', disabledReason: '已經完成的項目不能封存', onSelect: select('archive') },
    { key: 'delete', label: '刪除', tone: 'danger', startsGroup: true, onSelect: select('delete') },
  ];
}

afterEach(() => {
  vi.unstubAllGlobals();
});

type MenuOverrides = Partial<ComponentProps<typeof Menu>>;

function renderMenu(overrides: MenuOverrides = {}) {
  const user = userEvent.setup();
  const onSelect = vi.fn();
  const onOuterKeyDown = vi.fn();
  render(
    // 模擬放在對話框裡：Esc 不能往外傳，否則對話框也會一起關上。
    <div onKeyDown={onOuterKeyDown}>
      <section aria-label="卡片">
        <Menu
          menuLabel="AGY-3 的更多動作"
          items={items(onSelect)}
          renderTrigger={(trigger) => (
            <button type="button" {...trigger}>
              更多
            </button>
          )}
          {...overrides}
        />
      </section>
      <button type="button">下一個</button>
    </div>,
  );
  const trigger = screen.getByRole('button', { name: '更多' });
  return { user, onSelect, onOuterKeyDown, trigger };
}

describe('Menu', () => {
  it('按鈕是 menu button；打開後焦點在第一個選項，選單就在按鈕所在的區塊裡', async () => {
    // Arrange
    const { user, trigger } = renderMenu();

    // Act
    await user.click(trigger);

    // Assert
    const menu = within(screen.getByRole('region', { name: '卡片' })).getByRole('menu', { name: 'AGY-3 的更多動作' });
    expect(trigger).toHaveAttribute('aria-haspopup', 'menu');
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    expect(trigger).toHaveAttribute('aria-controls', menu.id);
    expect(within(menu).getByRole('menuitem', { name: '改類型' })).toHaveFocus();
    expect(within(menu).getByRole('separator')).toBeInTheDocument();
  });

  it('方向鍵、Home、End 移動，頭尾相接；不能用的也停得到並說明原因', async () => {
    // Arrange
    const { user, trigger } = renderMenu();
    await user.click(trigger);
    const menu = screen.getByRole('menu');

    // Act
    await user.keyboard('{ArrowDown}{ArrowDown}');
    const archive = within(menu).getByRole('menuitem', { name: '封存' });
    const archiveFocused = document.activeElement === archive;
    await user.keyboard('{ArrowDown}{ArrowDown}');
    const wrapped = document.activeElement;
    await user.keyboard('{End}');
    const end = document.activeElement;
    await user.keyboard('{Home}{ArrowUp}');

    // Assert
    expect(archiveFocused).toBe(true);
    expect(archive).toHaveAttribute('aria-disabled', 'true');
    expect(archive).toHaveAccessibleDescription('已經完成的項目不能封存');
    expect(archive).toHaveTextContent('封存已經完成的項目不能封存');
    expect(wrapped).toBe(within(menu).getByRole('menuitem', { name: '改類型' }));
    expect(end).toBe(within(menu).getByRole('menuitem', { name: '刪除' }));
    expect(within(menu).getByRole('menuitem', { name: '刪除' })).toHaveFocus();
  });

  it('選了之後關上、焦點回到按鈕，再執行動作', async () => {
    // Arrange
    const { user, trigger, onSelect } = renderMenu();
    await user.click(trigger);

    // Act
    await user.keyboard('{ArrowDown}{Enter}');

    // Assert
    expect(onSelect).toHaveBeenCalledWith('copy');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
  });

  it('滑過的項目就是焦點所在：用滑鼠打開、滑過第三項後按 Enter，執行的是滑過的那一項（不是一打開就有焦點的第一項）', async () => {
    // Arrange
    const picked = vi.fn();
    const { user, trigger } = renderMenu({
      items: ['改類型', '複製連結', '在新分頁打開'].map((label) => ({
        key: label,
        label,
        onSelect: () => {
          picked(label);
        },
      })),
    });
    await user.click(trigger);
    const third = within(screen.getByRole('menu')).getByRole('menuitem', { name: '在新分頁打開' });

    // Act
    await user.hover(third);
    const thirdFocused = document.activeElement === third;
    await user.keyboard('{Enter}');

    // Assert
    expect(thirdFocused).toBe(true);
    expect(picked).toHaveBeenCalledExactlyOnceWith('在新分頁打開');
  });

  it('滑過不能用的項目也把焦點移過去（方向鍵停得到的，滑鼠也一樣），按 Enter 沒有反應', async () => {
    // Arrange
    const { user, trigger, onSelect } = renderMenu();
    await user.click(trigger);
    const archive = within(screen.getByRole('menu')).getByRole('menuitem', { name: '封存' });

    // Act
    await user.hover(archive);
    await user.keyboard('{Enter}');

    // Assert
    expect(archive).toHaveFocus();
    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole('menu')).toBeInTheDocument();
  });

  it('不能用的選項點了沒有反應，選單留著', async () => {
    // Arrange
    const { user, trigger, onSelect } = renderMenu();
    await user.click(trigger);

    // Act
    await user.click(screen.getByRole('menuitem', { name: '封存' }));

    // Assert
    expect(onSelect).not.toHaveBeenCalled();
    expect(screen.getByRole('menu')).toBeInTheDocument();
  });

  it('Esc 關上、焦點回到按鈕，不往外傳', async () => {
    // Arrange
    const { user, trigger, onOuterKeyDown } = renderMenu();
    await user.click(trigger);
    onOuterKeyDown.mockClear();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(onOuterKeyDown).not.toHaveBeenCalled();
  });

  it('Tab 關上，焦點照常往下走', async () => {
    // Arrange
    const { user, trigger } = renderMenu();
    await user.click(trigger);

    // Act
    await user.tab();

    // Assert
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '下一個' })).toHaveFocus();
  });

  it('點在外面就關上；再按一次按鈕也會關上', async () => {
    // Arrange
    const { user, trigger } = renderMenu();
    await user.click(trigger);
    await user.click(screen.getByRole('button', { name: '下一個' }));
    const closedByOutside = screen.queryByRole('menu');

    // Act
    await user.click(trigger);
    await user.click(trigger);

    // Assert
    expect(closedByOutside).not.toBeInTheDocument();
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('在按鈕上按下方向鍵打開：↓ 到第一個、↑ 到最後一個', async () => {
    // Arrange
    const { user, trigger } = renderMenu();
    trigger.focus();

    // Act
    await user.keyboard('{ArrowUp}');
    const lastFocused = document.activeElement?.textContent;
    await user.keyboard('{Escape}{ArrowDown}');

    // Assert
    expect(lastFocused).toBe('刪除');
    expect(screen.getByRole('menuitem', { name: '改類型' })).toHaveFocus();
  });

  it('停用時按鈕標 aria-disabled，打不開', async () => {
    // Arrange
    const { user, trigger } = renderMenu({ isDisabled: true });

    // Act
    await user.click(trigger);
    trigger.focus();
    await user.keyboard('{ArrowDown}');

    // Assert
    expect(trigger).toHaveAttribute('aria-disabled', 'true');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('呼叫端給的 triggerRef 指向按鈕', async () => {
    // Arrange
    const triggerRef = createRef<HTMLButtonElement>();
    const { user, trigger } = renderMenu({ triggerRef });

    // Act
    await user.click(trigger);
    await user.keyboard('{Escape}');

    // Assert
    expect(triggerRef.current).toBe(trigger);
    expect(trigger).toHaveFocus();
  });

  it('焦點移到選單外面的元素就關上，焦點不被搬回按鈕', async () => {
    // Arrange
    const { user, trigger } = renderMenu();
    await user.click(trigger);
    const next = screen.getByRole('button', { name: '下一個' });

    // Act
    act(() => {
      next.focus();
    });

    // Assert
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(next).toHaveFocus();
  });

  it('焦點只是離開視窗（沒有移到別的元素）時不關', async () => {
    // Arrange
    const { user, trigger } = renderMenu();
    await user.click(trigger);

    // Act
    act(() => {
      screen.getByRole('menuitem', { name: '改類型' }).blur();
    });

    // Assert
    expect(screen.getByRole('menu')).toBeInTheDocument();
  });

  it('按鈕還看得到時不關；被捲出看得到的範圍就關上，焦點回到按鈕', async () => {
    // Arrange
    const observer = stubIntersectionObserver();
    const { user, trigger } = renderMenu();
    await user.click(trigger);
    observer.report(true);
    const isOpenWhileVisible = screen.queryByRole('menu') !== null;

    // Act
    observer.report(false);

    // Assert
    expect(isOpenWhileVisible).toBe(true);
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(observer.observing).toBe(0);
  });

  it('有 isChecked 的項目是 menuitemradio、帶 aria-checked，方向鍵照樣在所有項目間走', async () => {
    // Arrange
    const { user, trigger } = renderMenu({
      items: [
        { key: 'a', label: '淺色', isChecked: false, onSelect: vi.fn() },
        { key: 'b', label: '深色', isChecked: true, onSelect: vi.fn() },
        { key: 'c', label: '設定', onSelect: vi.fn() },
      ],
    });

    // Act
    await user.click(trigger);
    await user.keyboard('{ArrowDown}{ArrowDown}');

    // Assert
    const menu = screen.getByRole('menu');
    expect(within(menu).getByRole('menuitemradio', { name: '深色' })).toHaveAttribute('aria-checked', 'true');
    expect(within(menu).getByRole('menuitemradio', { name: '淺色' })).toHaveAttribute('aria-checked', 'false');
    expect(within(menu).getByRole('menuitem', { name: '設定' })).toHaveFocus();
  });
});
