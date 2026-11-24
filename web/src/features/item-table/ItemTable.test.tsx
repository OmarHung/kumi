import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, signedInAs } from '../../test/api';
import {
  agyDetail,
  agyItem,
  exampleMember,
  exampleOwner,
  smkDetail,
  smkItem,
} from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const fullTask = smkItem({
  number: 1,
  title: '首頁設計稿',
  state: 'doing',
  stateCategory: 'in_progress',
  priority: 'high',
  startDate: '2026-11-20',
  dueDate: '2026-12-01',
  estimate: 2.5,
  fields: {
    category: 'bug',
    tags: ['frontend', 'docs'],
    review_date: '2026-11-30',
    story_points: 3,
    budget: 1500,
    blocked: true,
    reference_url: 'https://example.com/spec',
    reviewer: exampleMember.personId,
    summary: '首頁的新版型',
    notes: '第一行\n第二行',
  },
});

async function findItemTable() {
  return screen.findByRole('table', { name: '項目' });
}

function headersOf(table: HTMLElement): (string | null)[] {
  // 多選的勾選框欄（表頭是「選取全部」的勾選框）不算欄名：它的測試在 features/bulk-edit。
  return within(table)
    .getAllByRole('columnheader')
    .filter((header) => within(header).queryByRole('checkbox') === null)
    .map((header) => header.textContent);
}

describe('專案的表格檢視', () => {
  it('列出核心欄位與每個自訂欄位；只有一種類型、沒有上層限制時不顯示類型與上層項目', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [fullTask]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    const table = await findItemTable();
    expect(headersOf(table)).toEqual([
      '編號',
      '標題',
      '狀態',
      '負責人',
      '優先度',
      '開始日',
      '到期日',
      '估計',
      '分類',
      '標籤',
      '檢查日',
      '點數',
      '預算',
      '卡住了',
      '參考連結',
      '審查者',
      '摘要',
      '備註',
    ]);
    const row = within(table).getByRole('row', { name: /SMK-1/ });
    expect(within(row).getByRole('rowheader')).toHaveTextContent('首頁設計稿');
    expect(within(row).getByRole('button', { name: '狀態：進行中' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '負責人：範例使用者' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '優先度：高' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '開始日：2026/11/20' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '估計：2.5' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '分類：錯誤' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '標籤：前端、文件' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '檢查日：2026/11/30' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '點數：3' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: /^預算：.*1,500/ })).toBeInTheDocument();
    expect(within(row).getByRole('checkbox', { name: '卡住了' })).toBeChecked();
    expect(within(row).getByRole('link', { name: '開啟參考連結' })).toHaveAttribute(
      'href',
      'https://example.com/spec',
    );
    expect(within(row).getByRole('button', { name: '審查者：範例成員' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: '摘要：首頁的新版型' })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: /^備註：第一行/ })).toBeInTheDocument();
  });

  it('多種類型時顯示類型欄；有類型設了上層限制時顯示上層項目欄', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [
      agyItem({ number: 1, title: '官網需求' }),
      agyItem({ number: 2, type: 'task', title: '切版', state: 'todo', parentKey: 'AGY-1' }),
    ]);

    // Act
    renderApp('/projects/AGY');

    // Assert
    const table = await findItemTable();
    expect(headersOf(table).slice(0, 3)).toEqual(['編號', '標題', '類型']);
    expect(headersOf(table)).toContain('上層項目');
    const task = within(table).getByRole('row', { name: /AGY-2/ });
    expect(within(task).getByText('任務')).toBeInTheDocument();
    expect(within(task).getByRole('button', { name: '上層項目：AGY-1' })).toBeInTheDocument();
  });

  it('類型沒有的欄位顯示不適用、不能編輯', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [
      agyItem({ number: 1, title: '官網需求' }),
      agyItem({ number: 2, type: 'task', title: '切版', state: 'todo' }),
    ]);

    // Act
    renderApp('/projects/AGY');

    // Assert
    const table = await findItemTable();
    const requirement = within(table).getByRole('row', { name: /AGY-1/ });
    const task = within(table).getByRole('row', { name: /AGY-2/ });
    expect(within(requirement).getByRole('button', { name: '報價金額：未填' })).toBeInTheDocument();
    expect(within(requirement).getAllByText('不適用').length).toBeGreaterThan(0);
    expect(within(task).queryByRole('button', { name: /^報價金額/ })).not.toBeInTheDocument();
    expect(within(task).getAllByText('不適用')).toHaveLength(4);
    expect(within(requirement).queryByRole('button', { name: /^上層項目/ })).not.toBeInTheDocument();
  });

  it('沒有項目時說明可以從哪裡新增', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', []);

    // Act
    renderApp('/projects/SMK');

    // Assert
    expect(await screen.findByText('這個專案還沒有項目')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: '項目' })).not.toBeInTheDocument();
  });

  it('篩選後沒有結果時說明，並可以清除條件', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', []);
    const { user, router } = renderApp('/projects/SMK?q=找不到的字');

    // Act
    expect(await screen.findByText('沒有符合條件的項目')).toBeInTheDocument();
    await user.click(within(screen.getByRole('main')).getAllByRole('button', { name: '清除條件' })[0] as HTMLElement);

    // Assert
    expect(router.state.location.search).toBe('');
    expect(await screen.findByText('這個專案還沒有項目')).toBeInTheDocument();
  });

  it('載入中顯示狀態，失敗時顯示訊息並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [fullTask]);
    server.use(
      http.get('/v1/projects/SMK/items', () => failure(500, 'server.internal_error', 'boom'), {
        once: true,
      }),
    );
    const { user } = renderApp('/projects/SMK');

    // Act
    expect(await screen.findByText('正在載入項目…')).toBeInTheDocument();
    const alert = await screen.findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(alert).toHaveTextContent('項目載入失敗');
    expect(await findItemTable()).toBeInTheDocument();
  });

  it('可以選擇顯示哪些欄，記在這個瀏覽器，下次打開還是一樣', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [fullTask]);
    const first = renderApp('/projects/SMK');
    await findItemTable();

    // Act
    await first.user.click(screen.getByRole('button', { name: '顯示的欄位' }));
    const picker = screen.getByRole('group', { name: '顯示的欄位' });
    await first.user.click(within(picker).getByRole('checkbox', { name: '備註' }));
    await first.user.click(within(picker).getByRole('checkbox', { name: '優先度' }));
    first.unmount();
    renderApp('/projects/SMK');

    // Assert
    const table = await findItemTable();
    expect(headersOf(table)).not.toContain('備註');
    expect(headersOf(table)).not.toContain('優先度');
    expect(headersOf(table)).toContain('摘要');
  });

  it('欄位選擇器按 Esc 或點外面就關上，Esc 時焦點回到按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [fullTask]);
    const { user } = renderApp('/projects/SMK');
    await findItemTable();
    const toggle = screen.getByRole('button', { name: '顯示的欄位' });

    // Act
    await user.click(toggle);
    await user.click(within(screen.getByRole('group', { name: '顯示的欄位' })).getByRole('checkbox', { name: '備註' }));
    await user.keyboard('{Escape}');
    const closedByEscape = screen.queryByRole('group', { name: '顯示的欄位' });
    const focusAfterEscape = document.activeElement;
    await user.click(toggle);
    await user.click(screen.getByRole('heading', { level: 1 }));

    // Assert
    expect(closedByEscape).not.toBeInTheDocument();
    expect(focusAfterEscape).toBe(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByText('隱藏了 1 欄')).toBeInTheDocument();
  });

  it('記住的欄位設定壞掉時當成沒有設定', async () => {
    // Arrange
    localStorage.setItem('item-table.columns.SMK', '{not json');
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [fullTask]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    expect(headersOf(await findItemTable())).toContain('備註');
  });
});
