import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Item } from '../../api/types';
import { created, serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveDeleteItem, serveItem, serveItems, servePatch } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';

const first = smkItem({ number: 1, title: '首頁設計稿', dueDate: '2026-11-20' });
const second = smkItem({ number: 2, title: '聯絡表單' });
const third = smkItem({ number: 3, title: '上線檢查' });
const ITEMS = [first, second, third];
const MODE_KEY = 'shell.itemPanelMode';

afterEach(() => {
  vi.unstubAllGlobals();
});

function serveProject(items: readonly Item[] = ITEMS) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItems('SMK', items);
  items.forEach((item) => {
    serveItem(detailOf(item, `${item.title}的說明`));
    serveActivity(item.key, [created()]);
  });
}

async function openTable(path = '/projects/SMK') {
  const view = renderApp(path);
  const table = await screen.findByRole('table', { name: '項目' });
  return { ...view, table };
}

function keyLink(table: HTMLElement, item: Item): HTMLElement {
  return within(table).getByRole('link', { name: item.title });
}

async function findModal(item: Item): Promise<HTMLElement> {
  return screen.findByRole('dialog', { name: `任務 ${item.key}` });
}

async function openModal(item: Item = first) {
  localStorage.setItem(MODE_KEY, 'modal');
  serveProject();
  const view = await openTable();
  await view.user.click(keyLink(view.table, item));
  const modal = await findModal(item);
  return { ...view, modal };
}

describe('項目面板：切換成浮動視窗', () => {
  it('預設是側邊面板：切換按鈕 aria-pressed 是 false，不是對話框', async () => {
    // Arrange
    serveProject();
    const { user, table } = await openTable();

    // Act
    await user.click(keyLink(table, first));

    // Assert
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-1' });
    expect(within(panel).getByRole('button', { name: '浮動視窗' })).toHaveAttribute('aria-pressed', 'false');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('切換按鈕的說明寫按了會怎樣；圖示兩種樣子都一樣（是浮動視窗，不跟著目前的樣子換）', async () => {
    // Arrange
    serveProject();
    const { user, table } = await openTable();
    await user.click(keyLink(table, first));
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-1' });
    const sideButton = within(panel).getByRole('button', { name: '浮動視窗' });
    const sideIcon = sideButton.querySelector('svg')?.innerHTML;

    // Act
    await user.click(sideButton);

    // Assert
    const modal = await findModal(first);
    const modalButton = within(modal).getByRole('button', { name: '浮動視窗' });
    expect(sideButton).toHaveAttribute('title', '改用置中的浮動視窗');
    expect(modalButton).toHaveAttribute('title', '改回側邊面板');
    expect(modalButton.querySelector('svg')?.innerHTML).toBe(sideIcon);
  });

  it('按「浮動視窗」換成 modal，記在 localStorage，焦點到標題、網址不變、內容還在', async () => {
    // Arrange
    serveProject();
    const { user, router, table } = await openTable();
    await user.click(keyLink(table, first));
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-1' });
    await within(panel).findByText('首頁設計稿的說明');

    // Act
    await user.click(within(panel).getByRole('button', { name: '浮動視窗' }));

    // Assert
    const modal = await findModal(first);
    expect(modal).toHaveAttribute('aria-modal', 'true');
    expect(screen.queryByRole('complementary', { name: '項目 SMK-1' })).not.toBeInTheDocument();
    expect(within(modal).getByRole('button', { name: '浮動視窗' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(modal).getByRole('heading', { level: 2, name: '任務 SMK-1' })).toHaveFocus();
    expect(await within(modal).findByText('首頁設計稿的說明')).toBeInTheDocument();
    expect(localStorage.getItem(MODE_KEY)).toBe('modal');
    expect(currentPath(router)).toBe('/projects/SMK?item=SMK-1');
  });

  it('再按一次換回側邊面板，焦點到標題', async () => {
    // Arrange
    const { user, modal } = await openModal();

    // Act
    await user.click(within(modal).getByRole('button', { name: '浮動視窗' }));

    // Assert
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-1' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(within(panel).getByRole('heading', { level: 2, name: '任務 SMK-1' })).toHaveFocus();
    expect(localStorage.getItem(MODE_KEY)).toBe('side');
  });

  it('存好的偏好是 modal：下次打開直接是浮動視窗', async () => {
    // Arrange / Act
    await openModal();

    // Assert
    expect(screen.queryByRole('complementary', { name: '項目 SMK-1' })).not.toBeInTheDocument();
  });

  it('localStorage 讀寫都丟例外時照樣能切換', async () => {
    // Arrange
    serveProject();
    const { user, table } = await openTable();
    await user.click(keyLink(table, first));
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-1' });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    // Act
    await user.click(within(panel).getByRole('button', { name: '浮動視窗' }));

    // Assert
    expect(await findModal(first)).toBeInTheDocument();
    vi.restoreAllMocks();
  });

  it('窄螢幕不開面板，直接進整頁，沒有這顆按鈕', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === COMPACT_QUERY,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }));
    localStorage.setItem(MODE_KEY, 'modal');
    serveProject();

    // Act
    renderApp('/projects/SMK?item=SMK-1');

    // Assert
    await screen.findByRole('heading', { level: 1, name: '首頁設計稿' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '浮動視窗' })).not.toBeInTheDocument();
  });
});

describe('項目面板：浮動視窗', () => {
  it('是 modal：焦點在標題上，背後的頁面 inert，Tab 繞圈不跑出去', async () => {
    // Arrange
    const { user, modal, container } = await openModal();
    await within(modal).findByText('首頁設計稿的說明');

    // Assert
    expect(within(modal).getByRole('heading', { level: 2, name: '任務 SMK-1' })).toHaveFocus();
    expect(container).toHaveAttribute('inert');

    // Act
    for (let step = 0; step < 60; step += 1) {
      await user.tab();
      expect(modal).toContainElement(document.activeElement as HTMLElement);
    }
    await user.tab({ shift: true });

    // Assert
    expect(modal).toContainElement(document.activeElement as HTMLElement);
  });

  it('Esc 關上：網址回到原本的、背後解除 inert、焦點回到清單上那一筆', async () => {
    // Arrange
    const { user, router, table, container } = await openModal();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(currentPath(router)).toBe('/projects/SMK');
    expect(container).not.toHaveAttribute('inert');
    expect(keyLink(table, first)).toHaveFocus();
  });

  it('編輯中的格子按 Esc 先取消編輯，再按一次才關上', async () => {
    // Arrange
    const { user, modal } = await openModal();
    const fields = await within(modal).findByRole('region', { name: '欄位' });
    await user.click(within(fields).getByRole('button', { name: '到期日：2026/11/20' }));

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(screen.getByRole('dialog', { name: '任務 SMK-1' })).toBe(modal);
    await user.keyboard('{Escape}');
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('編輯中的格子點遮罩：先送出再關上（值不會丟）', async () => {
    // Arrange
    const { user, table, modal } = await openModal();
    servePatch(detailOf(first, '首頁設計稿的說明'));
    const fields = await within(modal).findByRole('region', { name: '欄位' });
    await user.click(within(fields).getByRole('button', { name: '到期日：2026/11/20' }));
    const input = within(fields).getByLabelText('到期日');
    await user.clear(input);
    await user.type(input, '2026-12-24');

    // Act
    await user.click(modal.parentElement as HTMLElement);

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(await within(table).findByRole('button', { name: '到期日：2026/12/24' })).toBeInTheDocument();
  });

  it('視窗裡後來才開的選單不是 inert（只有打開時已經在的內容才是）', async () => {
    // Arrange
    const { user, modal } = await openModal();

    // Act
    await user.click(await within(modal).findByRole('button', { name: 'SMK-1 的更多動作' }));

    // Assert
    const menuItem = screen.getByRole('menuitem', { name: '刪除' });
    expect(menuItem.closest('[inert]')).toBeNull();
  });

  it('按關閉、點遮罩都關上，焦點回到清單上那一筆', async () => {
    // Arrange
    const { user, router, table, modal } = await openModal();

    // Act
    await user.click(within(modal).getByRole('button', { name: '關閉' }));

    // Assert
    await waitFor(() => {
      expect(keyLink(table, first)).toHaveFocus();
    });
    expect(currentPath(router)).toBe('/projects/SMK');

    // Act
    await user.click(keyLink(table, second));
    const next = await findModal(second);
    await user.click(next.parentElement as HTMLElement);

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(currentPath(router)).toBe('/projects/SMK');
    expect(keyLink(table, second)).toHaveFocus();
  });

  it('在視窗裡按下、拖到遮罩放開（選字）不關上；點視窗裡面也不關', async () => {
    // Arrange
    const { user, modal } = await openModal();
    const backdrop = modal.parentElement as HTMLElement;

    // Act
    await user.pointer([{ keys: '[MouseLeft>]', target: modal }, { target: backdrop }, { keys: '[/MouseLeft]' }]);
    await user.click(modal);

    // Assert
    expect(screen.getByRole('dialog', { name: '任務 SMK-1' })).toBe(modal);
  });

  it('上一個、下一個（按鈕與 Alt+↓／↑）：視窗不重新掛上，網址換掉不多一筆紀錄', async () => {
    // Arrange
    const { user, router, modal } = await openModal();
    await within(modal).findByText('首頁設計稿的說明');

    // Act
    await user.click(within(modal).getByRole('button', { name: '下一個項目' }));
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');

    // Assert
    expect(await within(modal).findByText('上線檢查的說明')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: '任務 SMK-3' })).toBe(modal);
    expect(currentPath(router)).toBe('/projects/SMK?item=SMK-3');
    await user.keyboard('{Alt>}{ArrowUp}{/Alt}');
    expect(await within(modal).findByText('聯絡表單的說明')).toBeInTheDocument();
    await router.navigate(-1);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(currentPath(router)).toBe('/projects/SMK');
  });

  it('瀏覽器的上一頁關上視窗；網址直接帶著 item 時一打開就是視窗', async () => {
    // Arrange
    const { router } = await openModal();

    // Act
    await router.navigate(-1);

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    await router.navigate('/projects/SMK?item=SMK-2');
    expect(await findModal(second)).toBeInTheDocument();
  });

  it('展開成整頁的連結還在，指向整頁的項目頁', async () => {
    // Arrange / Act
    const { modal } = await openModal();

    // Assert
    expect(within(modal).getByRole('link', { name: '展開成整頁' })).toHaveAttribute('href', '/projects/SMK/items/SMK-1');
  });

  it('刪除之後關上視窗、留在清單，清單上方有提示', async () => {
    // Arrange
    const { user, modal } = await openModal();
    await user.click(await within(modal).findByRole('button', { name: 'SMK-1 的更多動作' }));
    serveItems('SMK', [second, third]);
    serveDeleteItem(first);

    // Act
    await user.click(screen.getByRole('menuitem', { name: '刪除' }));

    // Assert
    expect(await screen.findByText('已刪除 SMK-1「首頁設計稿」。')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('網址直接帶著 item 時一打開就是視窗；換掉 item 是同一個視窗換內容', async () => {
    // Arrange
    localStorage.setItem(MODE_KEY, 'modal');
    serveProject();
    const { router } = await openTable('/projects/SMK?item=SMK-2');
    const modal = await findModal(second);

    // Act
    await router.navigate('/projects/SMK?item=SMK-3', { replace: true });

    // Assert
    expect(await screen.findByRole('dialog', { name: '任務 SMK-3' })).toBe(modal);
  });
});

describe('項目面板：浮動視窗與疊在上面的對話框', () => {
  const agyFirst = agyItem({ number: 1, title: '首頁設計稿' });

  async function openDeleteDialog() {
    localStorage.setItem(MODE_KEY, 'modal');
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const items = [agyFirst, agyItem({ number: 2, title: '聯絡表單' })];
    serveItems('AGY', items);
    items.forEach((item) => {
      serveItem(detailOf(item));
      serveActivity(item.key, [created()]);
    });
    const view = renderApp('/projects/AGY');
    const table = await screen.findByRole('table', { name: '項目' });
    await view.user.click(within(table).getByRole('link', { name: '首頁設計稿' }));
    const modal = await screen.findByRole('dialog', { name: /AGY-1$/ });
    await view.user.click(await within(modal).findByRole('button', { name: 'AGY-1 的更多動作' }));
    await view.user.click(screen.getByRole('menuitem', { name: '刪除' }));
    const dialog = await screen.findByRole('dialog', { name: '刪除 AGY-1？' });
    return { ...view, modal, dialog };
  }

  it('Alt+↓ 與 Esc 只作用在對話框，視窗留著、網址不變', async () => {
    // Arrange
    const { user, router, modal, dialog } = await openDeleteDialog();

    // Act
    await user.keyboard('{Alt>}{ArrowDown}{/Alt}');
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(dialog).not.toBeInTheDocument();
    });
    expect(screen.getByRole('dialog', { name: /AGY-1$/ })).toBe(modal);
    expect(currentPath(router)).toBe('/projects/AGY?item=AGY-1');
  });

  it('對話框開著時焦點鎖在對話框裡，關掉之後視窗還在、背後仍然 inert', async () => {
    // Arrange
    const { user, modal, dialog, container } = await openDeleteDialog();

    // Act
    for (let step = 0; step < 8; step += 1) {
      await user.tab();
      expect(dialog).toContainElement(document.activeElement as HTMLElement);
    }
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(dialog).not.toBeInTheDocument();
    });
    expect(modal).toBeInTheDocument();
    expect(container).toHaveAttribute('inert');
  });
});
