import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { orgTemplate, serveDescriptionTemplates } from '../../test/projectDescription';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const ops = orgTemplate({ id: 'op-1', name: '維運案子', isDefault: false });
const intranet = orgTemplate({ id: 'op-2', name: '內部系統', body: '## 內部\n', isDefault: true });

async function openList(templates = [ops, intranet]) {
  signedInAs(exampleOwner);
  const served = serveDescriptionTemplates(templates);
  const view = renderApp('/description-templates');
  const table = await screen.findByRole('table', { name: '說明格式' });
  return { ...view, served, table };
}

describe('說明格式頁：清單', () => {
  it('設定中心的次導覽：「說明格式」在組織那一組，只有組織管理員看得到，目前所在標 aria-current', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveDescriptionTemplates([ops]);
    const owner = renderApp('/description-templates');

    // Act
    const nav = await screen.findByRole('navigation', { name: '設定' });
    const link = await within(nav).findByRole('link', { name: '說明格式' });
    const isCurrent = link.getAttribute('aria-current');
    owner.unmount();
    signedInAs(exampleMember);
    renderApp('/account/notifications');
    // 次導覽畫完（「通知」連結在了）才斷言沒有那個連結，不然只是還沒畫出來。
    const memberNav = await screen.findByRole('navigation', { name: '設定' });
    await within(memberNav).findByRole('link', { name: '通知' });

    // Assert
    expect(link).toHaveAttribute('href', '/description-templates');
    expect(isCurrent).toBe('page');
    expect(within(memberNav).queryByRole('link', { name: '說明格式' })).not.toBeInTheDocument();
  });

  it('頁列的位置是「設定」，標題是「說明格式」；新增格式在頁列右邊', async () => {
    // Arrange / Act
    await openList();

    // Assert
    const trail = screen.getByRole('navigation', { name: '位置' });
    expect(within(trail).getByText('設定')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '說明格式' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '新增格式' })).toHaveAttribute('href', '/description-templates/new');
  });

  it('不是管理員直接打網址：說沒有權限、給回去的路，不顯示清單', async () => {
    // Arrange
    signedInAs(exampleMember);
    serveDescriptionTemplates([ops]);

    // Act
    renderApp('/description-templates');

    // Assert
    expect(await screen.findByText('沒有權限')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到我的工作' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '新增格式' })).not.toBeInTheDocument();
  });

  it('列出組織的與內建的格式，預設與內建各有標籤，說明一行寫在頁列下面', async () => {
    // Arrange / Act
    const { table } = await openList();

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: '說明格式' })).toBeInTheDocument();
    expect(screen.getByText(/改了不會影響已經寫好的說明/)).toBeInTheDocument();
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => within(row).getByText(/維運案子|內部系統|網站案子/).textContent)).toEqual(['內部系統', '維運案子', '網站案子']);
    const intranetRow = within(table).getByRole('row', { name: /內部系統/ });
    expect(within(intranetRow).getByText('預設')).toBeInTheDocument();
    expect(within(intranetRow).queryByText('內建')).not.toBeInTheDocument();
    const builtInRow = within(table).getByRole('row', { name: /網站案子/ });
    expect(within(builtInRow).getByText('內建')).toBeInTheDocument();
    expect(within(builtInRow).queryByText('預設')).not.toBeInTheDocument();
  });

  it('組織的格式有「編輯」與「更多」；預設那一份的「更多」沒有「設成預設」，內建的只有「複製一份來改」', async () => {
    // Arrange
    const { user, table } = await openList();

    // Act
    const opsRow = within(table).getByRole('row', { name: /維運案子/ });
    await user.click(within(opsRow).getByRole('button', { name: '維運案子 的動作' }));
    const withDefault = within(await screen.findByRole('menu')).getAllByRole('menuitem').map((item) => item.textContent);
    await user.keyboard('{Escape}');
    const intranetRow = within(table).getByRole('row', { name: /內部系統/ });
    await user.click(within(intranetRow).getByRole('button', { name: '內部系統 的動作' }));
    const asDefault = within(await screen.findByRole('menu')).getAllByRole('menuitem').map((item) => item.textContent);
    await user.keyboard('{Escape}');

    // Assert
    expect(within(opsRow).getByRole('link', { name: '編輯' })).toHaveAttribute('href', '/description-templates/op-1');
    expect(withDefault).toEqual(['設成預設', '刪除']);
    expect(asDefault).toEqual(['刪除']);
    const builtInRow = within(table).getByRole('row', { name: /網站案子/ });
    expect(within(builtInRow).getByRole('link', { name: '複製一份來改' })).toHaveAttribute('href', '/description-templates/new?from=web_project');
    expect(within(builtInRow).queryByRole('button')).not.toBeInTheDocument();
  });

  it('「新增格式」到新增頁', async () => {
    // Arrange
    const { user, router } = await openList();

    // Act
    await user.click(screen.getByRole('link', { name: '新增格式' }));

    // Assert
    expect(currentPath(router)).toBe('/description-templates/new');
  });

  it('設成預設：送出原本的名稱與內容，清單的預設換成這一份，頁面上方說一聲', async () => {
    // Arrange
    const { user, table, served } = await openList();

    // Act
    const opsRow = within(table).getByRole('row', { name: /維運案子/ });
    await user.click(within(opsRow).getByRole('button', { name: '維運案子 的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '設成預設' }));

    // Assert
    expect(await screen.findByText('已把「維運案子」設成預設。')).toBeInTheDocument();
    expect(served.writes.map((write) => write.body)).toEqual([{ name: '維運案子', body: ops.body, isDefault: true }]);
    await waitFor(() => {
      expect(within(screen.getByRole('row', { name: /維運案子/ })).getByText('預設')).toBeInTheDocument();
    });
    expect(within(screen.getByRole('row', { name: /內部系統/ })).queryByText('預設')).not.toBeInTheDocument();
  });

  it('刪除：先確認，確認後從清單消失；取消什麼都不做', async () => {
    // Arrange
    const { user, table, served } = await openList();
    const opsRow = within(table).getByRole('row', { name: /維運案子/ });

    // Act
    await user.click(within(opsRow).getByRole('button', { name: '維運案子 的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    const dialog = await screen.findByRole('dialog', { name: '刪除「維運案子」' });
    await user.click(within(dialog).getByRole('button', { name: '取消' }));
    const writesAfterCancel = served.writes.length;
    await user.click(within(opsRow).getByRole('button', { name: '維運案子 的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    await user.click(within(await screen.findByRole('dialog', { name: '刪除「維運案子」' })).getByRole('button', { name: '刪除' }));

    // Assert
    expect(writesAfterCancel).toBe(0);
    const notice = await screen.findByText('已刪除「維運案子」。');
    expect(notice.closest('[role="status"]')).toHaveFocus();
    await waitFor(() => {
      expect(screen.queryByRole('row', { name: /維運案子/ })).not.toBeInTheDocument();
    });
    expect(served.writes).toHaveLength(1);
  });

  it('刪除失敗：對話框留著，把伺服器的說明寫在裡面', async () => {
    // Arrange
    const { user, table, served } = await openList();
    served.rejectNext({ status: 404, code: 'description_template.not_found', message: '找不到這個說明格式。' });

    // Act
    const opsRow = within(table).getByRole('row', { name: /維運案子/ });
    await user.click(within(opsRow).getByRole('button', { name: '維運案子 的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    const dialog = await screen.findByRole('dialog', { name: '刪除「維運案子」' });
    await user.click(within(dialog).getByRole('button', { name: '刪除' }));

    // Assert
    expect(await within(dialog).findByText('找不到這個說明格式。')).toBeInTheDocument();
  });

  it('設成預設失敗：寫在頁面上方的提示，清單照舊', async () => {
    // Arrange
    const { user, table, served } = await openList();
    served.rejectNext({ status: 403, code: 'work.forbidden', message: '只有組織的管理員可以管理說明格式。' });

    // Act
    const opsRow = within(table).getByRole('row', { name: /維運案子/ });
    await user.click(within(opsRow).getByRole('button', { name: '維運案子 的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '設成預設' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('沒有把「維運案子」設成預設：只有組織的管理員可以管理說明格式。');
  });

  it('清單讀不到：說讀不到並給「重試」', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.get('/v1/description-templates', () => failure(500, 'server.error', '伺服器出錯了。')));

    // Act
    renderApp('/description-templates');

    // Assert
    expect(await screen.findByRole('button', { name: '重試' })).toBeInTheDocument();
  });

  it('有請求還在送：所有列的「更多」先停用，送完才恢復', async () => {
    // Arrange
    const { user, table } = await openList();
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => { release = resolve; });
    server.use(
      http.put('/v1/description-templates/:id', async () => {
        await gate;
        return ok(ops);
      }),
    );
    const opsRow = within(table).getByRole('row', { name: /維運案子/ });

    // Act
    await user.click(within(opsRow).getByRole('button', { name: '維運案子 的動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '設成預設' }));

    // Assert
    const others = within(table).getByRole('button', { name: '內部系統 的動作' });
    await waitFor(() => { expect(others).toHaveAttribute('aria-disabled', 'true'); });
    expect(within(opsRow).getByRole('button', { name: '維運案子 的動作' })).toHaveAttribute('aria-disabled', 'true');
    release();
    await waitFor(() => { expect(others).not.toHaveAttribute('aria-disabled', 'true'); });
  });
});
