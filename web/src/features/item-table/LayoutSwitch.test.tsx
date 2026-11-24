import { act, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';

/** 可以改變寬度的螢幕：change(true) 變窄、change(false) 變寬，通知每一個聽眾（和 SidebarCollapse.test.tsx 同一個做法）。 */
function stubResizableScreen(isInitiallyCompact: boolean) {
  let isCompact = isInitiallyCompact;
  const listeners = new Set<() => void>();
  vi.stubGlobal('matchMedia', (query: string) => ({
    get matches() {
      return query === COMPACT_QUERY && isCompact;
    },
    media: query,
    addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
    removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
  }));
  return {
    change(next: boolean) {
      isCompact = next;
      act(() => {
        listeners.forEach((listener) => {
          listener();
        });
      });
    },
  };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('寬窄切換時，新增到一半的內容留著', () => {
  it('窄螢幕的「新增項目」對話框開著時視窗變寬：對話框照樣開著，打的標題還在', async () => {
    // Arrange
    const screenSize = stubResizableScreen(true);
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1 })]);
    const { user } = renderApp('/projects/SMK');
    await user.click(await screen.findByRole('button', { name: '新增項目' }));
    await user.type(within(screen.getByRole('dialog', { name: '新增項目' })).getByRole('textbox', { name: '標題' }), '寫到一半');

    // Act
    screenSize.change(false);

    // Assert
    const dialog = screen.getByRole('dialog', { name: '新增項目' });
    expect(within(dialog).getByRole('textbox', { name: '標題' })).toHaveValue('寫到一半');
    expect(await screen.findByRole('table', { name: '項目' })).toBeInTheDocument();
  });

  it('寬螢幕的快速新增打到一半時視窗變窄：打開「新增項目」，類型與標題都還在', async () => {
    // Arrange
    const screenSize = stubResizableScreen(false);
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [agyItem({ number: 1 })]);
    const { user } = renderApp('/projects/AGY');
    const quickAdd = await screen.findByRole('form', { name: '新增項目' });
    await chooseOption(user, within(quickAdd).getByRole('combobox', { name: '類型' }), '任務');
    await user.type(within(quickAdd).getByRole('textbox', { name: '標題' }), '寫到一半');

    // Act
    screenSize.change(true);
    await user.click(await screen.findByRole('button', { name: '新增項目' }));

    // Assert
    const dialog = screen.getByRole('dialog', { name: '新增項目' });
    expect(within(dialog).getByRole('combobox', { name: '類型' })).toHaveTextContent('任務');
    expect(within(dialog).getByRole('textbox', { name: '標題' })).toHaveValue('寫到一半');
  });
});

describe('「清除條件」按了就不見：焦點有去處', () => {
  it('寬螢幕：焦點到搜尋框', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [agyItem({ number: 1 })]);
    const { user } = renderApp('/projects/AGY?type=requirement');
    await screen.findByRole('table', { name: '項目' });
    const filters = screen.getByRole('group', { name: '篩選項目' });

    // Act
    await user.click(within(filters).getByRole('button', { name: '清除條件' }));

    // Assert
    expect(within(filters).queryByRole('button', { name: '清除條件' })).not.toBeInTheDocument();
    expect(within(screen.getByRole('search')).getByRole('searchbox', { name: '搜尋' })).toHaveFocus();
  });

  it('窄螢幕：焦點到「篩選」按鈕', async () => {
    // Arrange
    stubResizableScreen(true);
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [agyItem({ number: 1 })]);
    const { user } = renderApp('/projects/AGY?type=requirement');
    await screen.findByRole('list', { name: '項目' });
    const toggle = screen.getByRole('button', { name: '篩選' });
    await user.click(toggle);

    // Act
    await user.click(within(screen.getByRole('dialog', { name: '篩選' })).getByRole('button', { name: '清除條件' }));

    // Assert
    expect(toggle).toHaveFocus();
    expect(toggle).not.toHaveAccessibleDescription();
  });

  it('沒有符合條件的項目時的「清除條件」：焦點到搜尋框', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', []);
    const { user } = renderApp('/projects/AGY?q=找不到的字');
    expect(await screen.findByText('沒有符合條件的項目')).toBeInTheDocument();

    // Act
    const empty = screen.getByText('沒有符合條件的項目').parentElement as HTMLElement;
    await user.click(within(empty).getByRole('button', { name: '清除條件' }));

    // Assert
    expect(within(screen.getByRole('search')).getByRole('searchbox', { name: '搜尋' })).toHaveFocus();
  });
});
