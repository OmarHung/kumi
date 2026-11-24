import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, makeItem, webProject } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

const item = makeItem({ number: 3, title: '首頁改版需求', state: 'todo', stateCategory: 'not_started' });

/** 我的工作開著 WEB-3 的面板，焦點放回清單上的那一筆。 */
async function openPanel() {
  signedInAs(exampleOwner, { projects: [webProject], myWork: [item] });
  serveItem(detailOf(item, '說明'));
  serveActivity(item.key, []);
  serveItems('WEB', []);
  const { user } = renderApp('/my-work');
  const row = await screen.findByRole('row', { name: /WEB-3/ });
  await user.click(within(row).getByRole('link', { name: '首頁改版需求' }));
  const panel = await screen.findByRole('complementary', { name: '項目 WEB-3' });
  const link = within(row).getByRole('link', { name: '首頁改版需求' });
  link.focus();
  return { user, panel, link };
}

describe('我的工作：面板的把手（docs/DESIGN.md 8.3「項目面板」）', () => {
  it('Tab 的順序是清單 → 調整寬度的把手 → 面板', async () => {
    // Arrange
    const { user, panel } = await openPanel();

    // Act
    await user.tab();
    const afterList = document.activeElement;
    await user.tab();

    // Assert
    expect(afterList).toBe(screen.getByRole('separator', { name: '調整面板寬度' }));
    expect(panel).toContainElement(document.activeElement as HTMLElement);
  });

  it('焦點在把手上按 Esc：關上面板，焦點回到清單上那一筆', async () => {
    // Arrange
    const { user, link } = await openPanel();
    await user.tab();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('complementary', { name: '項目 WEB-3' })).not.toBeInTheDocument();
    });
    await waitFor(() => {
      expect(link).toHaveFocus();
    });
  });
});
