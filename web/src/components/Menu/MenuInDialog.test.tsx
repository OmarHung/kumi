import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState, type ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { Dialog } from '../Dialog/Dialog';
import { Popover } from '../Popover/Popover';
import { Menu, type MenuItem } from './Menu';

const ITEMS: MenuItem[] = [
  { key: 'type', label: '改類型', onSelect: () => undefined },
  { key: 'delete', label: '刪除', onSelect: () => undefined },
];

function WithDialog({ children }: { readonly children: ReactNode }) {
  const [isOpen, setIsOpen] = useState(true);
  return (
    <>
      <button type="button">對話框外面</button>
      {isOpen && (
        <Dialog
          title="成員"
          onClose={() => {
            setIsOpen(false);
          }}
        >
          <button type="button">第一顆</button>
          {children}
        </Dialog>
      )}
    </>
  );
}

const menu = (
  <Menu
    menuLabel="更多動作"
    items={ITEMS}
    renderTrigger={(trigger) => (
      <button type="button" {...trigger}>
        更多
      </button>
    )}
  />
);

describe('對話框裡的選單', () => {
  it('Esc 只關選單，對話框還在，焦點回到按鈕', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<WithDialog>{menu}</WithDialog>);
    await user.click(screen.getByRole('button', { name: '更多' }));

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: '成員' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '更多' })).toHaveFocus();
  });

  it('按鈕是對話框裡最後一個時，選單開著按 Tab：選單關上，焦點繞回對話框的第一個，不會跑出對話框', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<WithDialog>{menu}</WithDialog>);
    await user.click(screen.getByRole('button', { name: '更多' }));

    // Act
    await user.tab();

    // Assert
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '第一顆' })).toHaveFocus();
  });

  it('對照：選單沒開時，在最後一顆按鈕上按 Tab 也是繞回第一顆', async () => {
    // Arrange
    const user = userEvent.setup();
    render(<WithDialog>{menu}</WithDialog>);
    screen.getByRole('button', { name: '更多' }).focus();

    // Act
    await user.tab();

    // Assert
    expect(screen.getByRole('button', { name: '第一顆' })).toHaveFocus();
  });
});

describe('對話框裡的浮層', () => {
  it('Esc 只關浮層，對話框還在', async () => {
    // Arrange
    const user = userEvent.setup();
    render(
      <WithDialog>
        <Popover
          label="篩選"
          renderTrigger={(trigger) => (
            <button type="button" {...trigger}>
              篩選
            </button>
          )}
        >
          <input type="checkbox" aria-label="只看我的" />
        </Popover>
      </WithDialog>,
    );
    await user.click(screen.getByRole('button', { name: '篩選' }));

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.queryByRole('dialog', { name: '篩選' })).not.toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: '成員' })).toBeInTheDocument();
  });
});
