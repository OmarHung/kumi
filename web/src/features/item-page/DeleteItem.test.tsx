import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { InfiniteData } from '@tanstack/react-query';
import { queryKeys } from '../../api/queryKeys';
import type { ItemPage, MyWork } from '../../api/types';
import { created, serveActivity } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { agyDetail, agyItem, ALL_PROJECT_PERMISSIONS, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveBulkRestore } from '../../test/bulk';
import { detailOf, serveDeleteItem, serveItem, serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const task = smkItem({ number: 7, title: '首頁設計稿' });
const other = smkItem({ number: 8, title: '聯絡表單' });

const requirement = agyItem({ number: 1, title: '官網需求' });
const child = agyItem({ number: 5, type: 'task', title: '首頁切版', state: 'doing', stateCategory: 'in_progress', parentKey: 'AGY-1' });

function serveTask() {
  serveItem(detailOf(task));
  serveActivity(task.key, [created()]);
}

/** 「刪除」在頁列的「更多動作」選單裡：先打開選單再選（原本是標題下面的一顆按鈕）。 */
async function chooseDelete(user: ReturnType<typeof renderApp>['user']) {
  await user.click(await screen.findByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ }));
  await user.click(screen.getByRole('menuitem', { name: '刪除' }));
}

describe('項目頁：刪除', () => {
  it('從表格進來的：選了「刪除」就刪（沒有確認框），回到表格並說明，表格裡不再有它，焦點到頁面的標題', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [task, other]);
    serveTask();
    const { user, router } = renderApp('/projects/SMK');
    await user.click(within(await screen.findByRole('table', { name: '項目' })).getByRole('link', { name: '首頁設計稿' }));
    await screen.findByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ });
    serveItems('SMK', [other]);
    const deletes = serveDeleteItem(task);

    // Act
    await chooseDelete(user);

    // Assert
    expect(screen.queryByRole('dialog', { name: '刪除 SMK-7？' })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK');
    });
    const notice = await screen.findByText('已刪除 SMK-7「首頁設計稿」。');
    expect(notice).toHaveAttribute('role', 'status');
    expect(within(notice).getByRole('button', { name: '復原' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: '冒煙測試' })).toHaveFocus();
    const table = await screen.findByRole('table', { name: '項目' });
    expect(within(table).queryByRole('link', { name: '首頁設計稿' })).not.toBeInTheDocument();
    expect(within(table).getByRole('link', { name: '聯絡表單' })).toBeInTheDocument();
    expect(deletes).toHaveLength(1);
  });

  it('從看板進來的：回到看板，條件照舊', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const board = serveItemsByQuery('SMK', [task, other]);
    serveTask();
    const { user, router } = renderApp('/projects/SMK?view=board&q=首頁');
    await user.click(await screen.findByRole('link', { name: '首頁設計稿' }));
    await screen.findByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ });
    board.set([other]);
    serveDeleteItem(task);

    // Act
    await chooseDelete(user);

    // Assert
    await waitFor(() => {
      expect(router.state.location.search).toBe('?view=board&q=首頁');
    });
    expect(await screen.findByText('已刪除 SMK-7「首頁設計稿」。')).toBeInTheDocument();
    expect(currentPath(router).startsWith('/projects/SMK?')).toBe(true);
  });

  it('直接打開項目頁的：回到專案的表格；我的工作與上層的下層清單裡也拿掉它，之後重抓', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [child]);
    serveItem(detailOf(requirement));
    serveActivity(requirement.key, [created()]);
    serveItem(detailOf(child));
    serveActivity(child.key, [created()]);
    const { user, router, queryClient } = renderApp('/projects/AGY/items/AGY-1');
    await screen.findByRole('list', { name: '下層項目' });
    queryClient.setQueryData<MyWork>(queryKeys.myWork, { items: [child] });
    // F 輪（U1）：點下層清單現在是開面板；這個情境是「直接打開下層的項目頁」，所以直接換網址（原本是點下層清單的連結）。
    await router.navigate('/projects/AGY/items/AGY-5');
    await screen.findByRole('heading', { level: 1, name: /首頁切版/ });
    serveItems('AGY', []);
    serveDeleteItem(child);

    // Act
    await chooseDelete(user);

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/AGY');
    });
    const siblings = queryClient.getQueryData<InfiniteData<ItemPage>>(queryKeys.items('AGY', { parent: 'AGY-1', limit: 50 }));
    expect(siblings?.pages.flatMap((page) => page.items)).toEqual([]);
    expect(queryClient.getQueryState(queryKeys.items('AGY', { parent: 'AGY-1', limit: 50 }))?.isInvalidated).toBe(true);
    expect(queryClient.getQueryData<MyWork>(queryKeys.myWork)?.items).toEqual([]);
    expect(queryClient.getQueryState(queryKeys.myWork)?.isInvalidated).toBe(true);
    expect(queryClient.getQueryData(queryKeys.item('AGY-5'))).toBeUndefined();
  });

  it('已經知道有下層：說明要先刪除或移開，列出下層，沒有刪除的按鈕；關上後焦點回到「更多動作」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', [child]);
    serveItem(detailOf(requirement));
    serveActivity(requirement.key, [created()]);
    const { user } = renderApp('/projects/AGY/items/AGY-1');
    await screen.findByRole('list', { name: '下層項目' });

    // Act
    await chooseDelete(user);

    // Assert
    const dialog = screen.getByRole('dialog', { name: '刪除 AGY-1？' });
    expect(dialog).toHaveTextContent('還有 1 個下層項目：要先刪除它們，或把它們的上層項目換掉，才能刪除這個項目。');
    expect(within(within(dialog).getByRole('list', { name: '下層項目' })).getByRole('link', { name: 'AGY-5 首頁切版' })).toHaveAttribute(
      'href',
      '/projects/AGY/items/AGY-5',
    );
    expect(within(dialog).queryByRole('button', { name: '刪除' })).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: '關閉' }));
    expect(screen.getByRole('button', { name: 'AGY-1 的更多動作' })).toHaveFocus();
  });

  it('伺服器說還有下層（409）：才開對話框列出是哪些，並重新讀下層清單', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItems('AGY', []);
    serveItem(detailOf(requirement));
    serveActivity(requirement.key, [created()]);
    const { user, router } = renderApp('/projects/AGY/items/AGY-1');
    expect(await screen.findByText('還沒有下層項目。')).toBeInTheDocument();
    const hidden = agyItem({ number: 8, type: 'task', title: '別人剛加的', state: 'todo', parentKey: 'AGY-1' });
    const refetches = serveItems('AGY', [hidden]);
    serveDeleteItem(requirement, [hidden]);

    // Act
    await chooseDelete(user);

    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('還有下層項目：要先刪除它們，或把它們的上層項目換掉，才能刪除這個項目。');
    const blocked = screen.getByRole('dialog', { name: '刪除 AGY-1？' });
    expect(within(blocked).getByRole('link', { name: /AGY-8/ })).toHaveAttribute('href', '/projects/AGY/items/AGY-8');
    expect(within(blocked).queryByRole('button', { name: '刪除' })).not.toBeInTheDocument();
    // 換成「還有下層」的對話框之後，焦點還在對話框裡（第一個下層的連結）。
    expect(blocked).toContainElement(document.activeElement as HTMLElement);
    expect(currentPath(router)).toBe('/projects/AGY/items/AGY-1');
    expect(await within(screen.getByRole('region', { name: '下層項目' })).findByRole('link', { name: /別人剛加的/ })).toBeInTheDocument();
    expect(refetches.map((url) => url.searchParams.get('parent'))).toEqual(['AGY-1']);
  });

  it('其他失敗（例如權限剛被拿掉）：對話框說伺服器的訊息，關上後焦點回到「更多動作」，留在項目頁', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveTask();
    const { user, router } = renderApp('/projects/SMK/items/SMK-7');
    await screen.findByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ });
    server.use(http.delete('/v1/items/SMK-7', () => failure(403, 'work.forbidden', '你沒有刪除項目的權限。')));

    // Act
    await chooseDelete(user);

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '沒有刪除 SMK-7' });
    expect(within(dialog).getByRole('alert')).toHaveTextContent('你沒有刪除項目的權限。');
    await user.click(within(dialog).getByRole('button', { name: '關閉' }));
    expect(screen.getByRole('button', { name: 'SMK-7 的更多動作' })).toHaveFocus();
    expect(currentPath(router)).toBe('/projects/SMK/items/SMK-7');
  });

  it('伺服器說找不到（別人剛刪掉）：當成已經刪除，照成功的路徑回到專案頁', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [other]);
    serveTask();
    const { user, router } = renderApp('/projects/SMK/items/SMK-7');
    await screen.findByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ });
    server.use(http.delete('/v1/items/SMK-7', () => failure(404, 'item.not_found', '找不到這個項目。')));

    // Act
    await chooseDelete(user);

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK');
    });
    expect(await screen.findByText('SMK-7「首頁設計稿」已經被別人刪除了。')).toHaveAttribute('role', 'status');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('下層只載入了第一頁：說「至少 N 個下層項目」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const later = agyItem({ number: 9, type: 'task', title: '還沒載入的下層', state: 'todo', parentKey: 'AGY-1' });
    serveItems('AGY', [child], [later]);
    serveItem(detailOf(requirement));
    serveActivity(requirement.key, [created()]);
    const { user } = renderApp('/projects/AGY/items/AGY-1');
    await screen.findByRole('list', { name: '下層項目' });

    // Act
    await chooseDelete(user);

    // Assert
    expect(screen.getByRole('dialog', { name: '刪除 AGY-1？' })).toHaveTextContent(
      '還有至少 1 個下層項目：要先刪除它們，或把它們的上層項目換掉，才能刪除這個項目。',
    );
  });

  it('下層清單還在載入：不等它，直接送出，由伺服器判斷', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    let release = () => {};
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/v1/projects/AGY/items', async () => {
        await gate;
        return ok({ items: [], nextCursor: null });
      }),
    );
    serveItem(detailOf(requirement));
    serveActivity(requirement.key, [created()]);
    const deletes = serveDeleteItem(requirement);
    const { user } = renderApp('/projects/AGY/items/AGY-1');

    // Act
    await chooseDelete(user);

    // Assert
    await waitFor(() => {
      expect(deletes).toHaveLength(1);
    });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    release();
  });

  it('離開後的提示有「復原」：按了送出，成功後文字換成「已復原 …」並附連到項目的連結', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [task, other]);
    serveTask();
    const { user, router, queryClient } = renderApp('/projects/SMK/items/SMK-7');
    await screen.findByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ });
    serveItems('SMK', [other]);
    serveDeleteItem(task);
    await chooseDelete(user);
    const undo = await screen.findByRole('button', { name: '復原' });
    queryClient.setQueryData(queryKeys.deletedItems('SMK'), { items: [], nextCursor: null });
    const restores = serveBulkRestore('SMK', [task]);
    serveItems('SMK', [task, other]);

    // Act
    await user.click(undo);

    // Assert
    const restored = await screen.findByText('已復原 SMK-7「首頁設計稿」。');
    expect(restored.closest('[data-tone]')).toHaveAttribute('data-tone', 'success');
    expect(restored.closest('[data-tone]')).toHaveTextContent('開啟 SMK-7');
    expect(screen.getByRole('link', { name: '開啟 SMK-7' })).toHaveAttribute('href', '/projects/SMK/items/SMK-7');
    expect(screen.queryByRole('button', { name: '復原' })).not.toBeInTheDocument();
    expect(restores.map((request) => request.body)).toEqual([{ keys: ['SMK-7'] }]);
    await waitFor(() => {
      expect(restored.closest('[data-tone]')).toHaveFocus();
    });
    expect(currentPath(router)).toBe('/projects/SMK');
    expect(queryClient.getQueryState(queryKeys.deletedItems('SMK'))?.isInvalidated).toBe(true);
    expect(await within(await screen.findByRole('table', { name: '項目' })).findByRole('link', { name: '首頁設計稿' })).toBeInTheDocument();
  });

  it('復原失敗（上層也刪除了）：「已刪除」與「復原」照舊留著，下面多一行原因，焦點還在按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [task, other]);
    serveTask();
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    await screen.findByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ });
    serveItems('SMK', [other]);
    serveDeleteItem(task);
    await chooseDelete(user);
    const undo = await screen.findByRole('button', { name: '復原' });
    serveBulkRestore('SMK', [task], {
      failures: { 'SMK-7': { code: 'item.parent_deleted', message: '上層 SMK-1 也已刪除，先復原它。' } },
    });

    // Act
    await user.click(undo);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('上層 SMK-1 也已刪除，先復原它。');
    expect(screen.getByText(/已刪除 SMK-7「首頁設計稿」。/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '復原' })).toHaveFocus();
  });

  it('復原的請求整個失敗（網路、權限）：一樣留著「復原」並說明原因，再按一次可以重試成功', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [task, other]);
    serveTask();
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    await screen.findByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ });
    serveItems('SMK', [other]);
    serveDeleteItem(task);
    await chooseDelete(user);
    const undo = await screen.findByRole('button', { name: '復原' });
    server.use(http.post('/v1/projects/SMK/items/bulk-restore', () => failure(503, 'server.unavailable', '伺服器暫時無法處理，請稍後再試。')));

    // Act
    await user.click(undo);

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('伺服器暫時無法處理，請稍後再試。');

    // Arrange
    serveBulkRestore('SMK', [task]);
    serveItems('SMK', [task, other]);

    // Act
    await user.click(screen.getByRole('button', { name: '復原' }));

    // Assert
    expect(await screen.findByText('已復原 SMK-7「首頁設計稿」。')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('復原成功之後把提示與 undo 從瀏覽紀錄拿掉：重新整理不會又出現「已刪除」與「復原」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [task, other]);
    serveTask();
    const { user, router } = renderApp('/projects/SMK/items/SMK-7');
    await screen.findByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ });
    serveItems('SMK', [other]);
    serveDeleteItem(task);
    await chooseDelete(user);
    const undo = await screen.findByRole('button', { name: '復原' });
    // 記憶體路由不寫 window.history：照 React Router 的形狀放一份，驗證會被改掉。
    const location: { key: string; state: unknown } = router.state.location;
    window.history.replaceState({ key: location.key, idx: 1, usr: location.state }, '');
    serveBulkRestore('SMK', [task]);
    serveItems('SMK', [task, other]);

    // Act
    await user.click(undo);

    // Assert
    await screen.findByText('已復原 SMK-7「首頁設計稿」。');
    const entry = window.history.state as { key: string; usr: Record<string, unknown> };
    expect(entry.key).toBe(router.state.location.key);
    expect(entry.usr).not.toHaveProperty('undo');
    expect(entry.usr).not.toHaveProperty('notice');
    window.history.replaceState(null, '');
  });

  it('別人先刪掉的（404）：提示說「已經被別人刪除了」，沒有「復原」', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [other]);
    serveTask();
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    await screen.findByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ });
    server.use(http.delete('/v1/items/SMK-7', () => failure(404, 'item.not_found', '找不到這個項目。')));

    // Act
    await chooseDelete(user);

    // Assert
    expect(await screen.findByText('SMK-7「首頁設計稿」已經被別人刪除了。')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '復原' })).not.toBeInTheDocument();
  });

  it('沒有刪除項目的權限時沒有「刪除」', async () => {
    // Arrange
    const permissions = ALL_PROJECT_PERMISSIONS.filter((permission) => permission !== 'item.delete');
    signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: permissions })] });
    serveTask();

    // Act
    renderApp('/projects/SMK/items/SMK-7');

    // Assert
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('首頁設計稿');
    // 冒煙測試只有一種類型（不能改類型），不能刪除就沒有「更多動作」。
    expect(screen.queryByRole('button', { name: /^[A-Z]+-\d+ 的更多動作$/ })).not.toBeInTheDocument();
  });
});
