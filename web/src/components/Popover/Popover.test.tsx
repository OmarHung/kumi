import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubIntersectionObserver } from '../../test/intersectionObserver';
import { Popover } from './Popover';

function renderPopover(children: ReactNode = <input aria-label="只看我的" type="checkbox" />) {
  const user = userEvent.setup();
  const onOuterKeyDown = vi.fn();
  render(
    <div onKeyDown={onOuterKeyDown}>
      <Popover
        label="篩選"
        align="end"
        renderTrigger={(trigger) => (
          <button type="button" {...trigger}>
            篩選
          </button>
        )}
      >
        {children}
      </Popover>
      <h2>外面的標題</h2>
      <button type="button">後面</button>
    </div>,
  );
  return { user, onOuterKeyDown, trigger: screen.getByRole('button', { name: '篩選' }) };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Popover', () => {
  it('按鈕標 aria-expanded；打開後是有名稱的浮層，焦點到裡面第一個可以操作的元素', async () => {
    // Arrange
    const { user, trigger } = renderPopover();
    const closedState = trigger.getAttribute('aria-expanded');

    // Act
    await user.click(trigger);

    // Assert
    const panel = screen.getByRole('dialog', { name: '篩選' });
    expect(closedState).toBe('false');
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    expect(trigger).toHaveAttribute('aria-controls', panel.id);
    expect(screen.getByRole('checkbox', { name: '只看我的' })).toHaveFocus();
  });

  it('在裡面操作不會關上；Esc 關上、焦點回到按鈕，不往外傳', async () => {
    // Arrange
    const { user, trigger, onOuterKeyDown } = renderPopover();
    await user.click(trigger);
    await user.click(screen.getByRole('checkbox', { name: '只看我的' }));
    const isStillOpen = screen.queryByRole('dialog') !== null;

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(isStillOpen).toBe(true);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(onOuterKeyDown).not.toHaveBeenCalledWith(expect.objectContaining({ key: 'Escape' }));
  });

  it('點在外面關上，焦點不被拉回按鈕；再按一次按鈕也會關上', async () => {
    // Arrange
    const { user, trigger } = renderPopover();
    await user.click(trigger);

    // Act
    await user.click(screen.getByRole('heading', { name: '外面的標題' }));
    const closedByOutside = screen.queryByRole('dialog');
    const focusAfterOutside = document.activeElement;
    await user.click(trigger);
    await user.click(trigger);

    // Assert
    expect(closedByOutside).not.toBeInTheDocument();
    expect(focusAfterOutside).not.toBe(trigger);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
  });

  it('裡面沒有可以操作的元素時，焦點到浮層本身', async () => {
    // Arrange
    const { user, trigger } = renderPopover(<p>沒有可以篩選的條件。</p>);

    // Act
    await user.click(trigger);

    // Assert
    expect(screen.getByRole('dialog', { name: '篩選' })).toHaveFocus();
  });

  it('Tab 離開浮層就關上，焦點照常往下走', async () => {
    // Arrange
    const { user, trigger } = renderPopover();
    await user.click(trigger);

    // Act
    await user.tab();

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '後面' })).toHaveFocus();
  });

  it('按鈕被捲出看得到的範圍就關上，焦點回到按鈕', async () => {
    // Arrange
    const observer = stubIntersectionObserver();
    const { user, trigger } = renderPopover();
    await user.click(trigger);

    // Act
    observer.report(false);

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it('children 給函式時拿得到 close：在裡面選了值就收起來，焦點回到按鈕', async () => {
    // Arrange
    const user = userEvent.setup();
    render(
      <Popover
        label="到期日"
        renderTrigger={(trigger) => (
          <button type="button" {...trigger}>
            到期日
          </button>
        )}
      >
        {({ close }) => (
          <button type="button" onClick={close}>
            選好了
          </button>
        )}
      </Popover>,
    );
    const trigger = screen.getByRole('button', { name: '到期日' });
    await user.click(trigger);

    // Act
    await user.click(screen.getByRole('button', { name: '選好了' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });
});
