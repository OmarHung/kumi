import { fireEvent, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { smkDetail } from '../../test/fixtures';
import { renderInRouter } from '../../test/renderInRouter';
import { CreatedStatus } from './CreatedStatus';
import { OpenItemProvider, type OpenItem } from './OpenItemContext';
import { abilitiesOf, ProjectTableProvider } from './ProjectTableContext';

function renderStatus(openItem: OpenItem | null, created = { key: 'SMK-2', isOutsideConditions: false }) {
  const detail = smkDetail();
  return renderInRouter(
    <ProjectTableProvider value={{ detail, can: abilitiesOf(detail), todayDate: '2026-11-16', onOpenMembers: () => undefined }}>
      <OpenItemProvider value={openItem}>
        <CreatedStatus created={created} />
      </OpenItemProvider>
    </ProjectTableProvider>,
    '/projects/SMK',
  );
}

describe('「已新增」與「開啟」', () => {
  it('沒有新增時 status 掛著、是空的，也沒有「開啟」', () => {
    // Arrange & Act
    const detail = smkDetail();
    renderInRouter(
      <ProjectTableProvider value={{ detail, can: abilitiesOf(detail), todayDate: '2026-11-16', onOpenMembers: () => undefined }}>
        <CreatedStatus created={null} />
      </ProjectTableProvider>,
    );

    // Assert
    expect(screen.getByRole('status')).toBeEmptyDOMElement();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('沒有提供開啟的方式時（預設），照連結換到整頁的項目頁', async () => {
    // Arrange
    const { user, router } = renderStatus(null);

    // Act
    await user.click(screen.getByRole('link', { name: '開啟 SMK-2' }));

    // Assert
    expect(router.state.location.pathname).toBe('/projects/SMK/items/SMK-2');
  });

  it('有提供開啟的方式（項目面板）時，一般的點擊交給它、不換頁', async () => {
    // Arrange
    const openItem = vi.fn<OpenItem>();
    const { user, router } = renderStatus(openItem);

    // Act
    await user.click(screen.getByRole('link', { name: '開啟 SMK-2' }));

    // Assert
    expect(openItem).toHaveBeenCalledWith('SMK-2');
    expect(router.state.location.pathname).toBe('/projects/SMK');
  });

  it('按著 ⌘／Ctrl 點照連結走，不交給面板', () => {
    // Arrange
    const openItem = vi.fn<OpenItem>();
    renderStatus(openItem);

    // Act
    const isDefaultAllowed = fireEvent.click(screen.getByRole('link', { name: '開啟 SMK-2' }), { ctrlKey: true });

    // Assert
    expect(openItem).not.toHaveBeenCalled();
    expect(isDefaultAllowed).toBe(true);
  });

  it('連結帶著目前的網址（刪除之後回得來），不符合條件時訊息多一句', () => {
    // Arrange & Act
    renderStatus(null, { key: 'SMK-2', isOutsideConditions: true });

    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('已新增 SMK-2。它不符合目前的條件，清除條件之後可以看到。');
    expect(screen.getByRole('link', { name: '開啟 SMK-2' })).toHaveAttribute('href', '/projects/SMK/items/SMK-2');
  });
});
