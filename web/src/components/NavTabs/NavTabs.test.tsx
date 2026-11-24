import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderInRouter } from '../../test/renderInRouter';
import { NavTabs } from './NavTabs';

describe('NavTabs', () => {
  it('是有名稱的 nav 裡的連結，目前的那個標 aria-current="page"', () => {
    // Arrange
    renderInRouter(
      <NavTabs
        label="檢視"
        tabs={[
          { key: 'table', label: '表格', to: '/projects/AGY', isCurrent: true },
          { key: 'board', label: '看板', to: '/projects/AGY?view=board', isCurrent: false },
        ]}
      />,
    );

    // Act
    const nav = screen.getByRole('navigation', { name: '檢視' });

    // Assert
    expect(within(nav).getByRole('link', { name: '表格' })).toHaveAttribute('aria-current', 'page');
    expect(within(nav).getByRole('link', { name: '看板' })).not.toHaveAttribute('aria-current');
    expect(within(nav).getByRole('link', { name: '看板' })).toHaveAttribute('href', '/projects/AGY?view=board');
  });

  it('計數看得到數字，報讀軟體唸完整的說法；名稱仍然只有分頁的名字', () => {
    // Arrange
    renderInRouter(
      <NavTabs
        label="設定的區塊"
        tabs={[
          {
            key: 'workflows',
            label: '流程',
            to: '/projects/AGY/settings/workflows',
            isCurrent: true,
            counts: [
              { value: 2, label: '2 項還沒送出的修改', tone: 'neutral' },
              { value: 1, label: '1 個要修正的問題', tone: 'danger' },
            ],
          },
          { key: 'roles', label: '角色', to: '/projects/AGY/settings/roles', isCurrent: false },
        ]}
      />,
    );

    // Act
    const workflows = screen.getByRole('link', { name: '流程' });

    // Assert
    expect(workflows).toHaveTextContent('流程21');
    expect(workflows).toHaveAccessibleDescription('2 項還沒送出的修改、1 個要修正的問題');
    expect(screen.getByRole('link', { name: '角色' })).not.toHaveAccessibleDescription();
  });

  it('「要修正」的危險計數除了顏色還有錯誤的圖示；還沒送出的修改是中性的、沒有圖示', () => {
    // Arrange
    renderInRouter(
      <NavTabs
        label="設定的區塊"
        tabs={[
          {
            key: 'fields',
            label: '欄位',
            to: '/projects/AGY/settings/fields',
            isCurrent: true,
            counts: [
              { value: 4, label: '4 項還沒送出的修改', tone: 'neutral' },
              { value: 3, label: '3 個要修正的問題', tone: 'danger' },
            ],
          },
        ]}
      />,
    );

    // Act
    const link = screen.getByRole('link', { name: '欄位' });

    // Assert
    const danger = within(link).getByText('3');
    expect(danger.firstElementChild).toHaveAttribute('data-icon', 'error');
    expect(danger).toHaveAttribute('aria-hidden', 'true');
    expect(within(link).getByText('4').firstElementChild).toBeNull();
  });
});
