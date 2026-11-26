import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { PRODUCT_NAME } from '../../config/product';
import { serveActivity } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner, makeItem, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, servePatch } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { editorText, findEditor, typeInEditor } from '../../test/richText';
import { server } from '../../test/server';

const task = detailOf(
  smkItem({
    number: 7,
    title: '首頁設計稿',
    state: 'doing',
    stateCategory: 'in_progress',
    priority: 'high',
    startDate: '2026-11-10',
    dueDate: '2026-11-20',
    estimate: 3,
    fields: { category: 'bug', summary: '舊的摘要' },
  }),
  '第一段說明',
);

function serveTaskPage(permissions = smkDetail().myPermissions) {
  signedInAs(exampleOwner, { details: [smkDetail({ myPermissions: permissions })] });
  serveItem(task);
  serveActivity(task.key, []);
}

function openPage(path = '/projects/SMK/items/SMK-7', permissions = smkDetail().myPermissions) {
  serveTaskPage(permissions);
  return renderApp(path);
}

describe('項目頁：載入', () => {
  it('顯示標題、所在的專案與每個欄位，分頁標題是編號加標題', async () => {
    // Arrange & Act
    openPage();

    // Assert
    const heading = await screen.findByRole('heading', { level: 1 });
    expect(heading).toHaveTextContent('首頁設計稿');
    const breadcrumb = screen.getByRole('navigation', { name: '位置' });
    expect(within(breadcrumb).getByRole('link', { name: '冒煙測試' })).toHaveAttribute('href', '/projects/SMK');
    expect(within(breadcrumb).getByText('SMK-7')).toBeInTheDocument();
    const fields = screen.getByRole('region', { name: '欄位' });
    expect(await within(fields).findByRole('button', { name: '狀態：進行中' })).toBeInTheDocument();
    expect(within(fields).getByText('任務')).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '負責人：範例使用者' })).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '優先度：高' })).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '開始日：2026/11/10' })).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '到期日：2026/11/20' })).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '估計：3' })).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '分類：錯誤' })).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '摘要：舊的摘要' })).toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '備註：未填' })).toBeInTheDocument();
    await waitFor(() => {
      expect(document.title).toBe(`SMK-7 首頁設計稿 · ${PRODUCT_NAME}`);
    });
  });

  it('類型不能有上層項目時不顯示上層項目欄位', async () => {
    // Arrange & Act
    openPage();

    // Assert
    const fields = await screen.findByRole('region', { name: '欄位' });
    expect(within(fields).queryByText('上層項目')).not.toBeInTheDocument();
  });

  it('項目不存在或看不到時顯示找不到，可以回到專案', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    server.use(http.get('/v1/items/SMK-99', () => failure(404, 'item.not_found', '找不到這個項目。')));
    const { user, router } = renderApp('/projects/SMK/items/SMK-99');

    // Act
    expect(await screen.findByRole('heading', { level: 1, name: '找不到這個項目' })).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: '回到專案' }));

    // Assert
    expect(currentPath(router)).toBe('/projects/SMK');
  });

  it('沒有權限看時說明沒有權限', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    server.use(http.get('/v1/items/SMK-7', () => failure(403, 'work.forbidden', '沒有權限。')));

    // Act
    renderApp('/projects/SMK/items/SMK-7');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '沒有權限看這個項目' })).toBeInTheDocument();
    expect(screen.getByText('403')).toBeInTheDocument();
  });

  it('項目讀得到、專案卻讀不到時顯示找不到這個專案', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(detailOf(makeItem({ projectKey: 'NOPE', number: 1 })));

    // Act
    renderApp('/projects/NOPE/items/NOPE-1');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '找不到這個專案' })).toBeInTheDocument();
  });

  it('其他錯誤顯示訊息並可以重試', async () => {
    // Arrange
    serveTaskPage();
    server.use(http.get('/v1/items/SMK-7', () => failure(503, 'server.not_ready', 'starting'), { once: true }));
    const { user } = renderApp('/projects/SMK/items/SMK-7');

    // Act
    const alert = await screen.findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(alert).toHaveTextContent('項目載入失敗');
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('首頁設計稿');
  });

  it('網址裡的專案和項目對不上時換到項目所在專案的網址', async () => {
    // Arrange & Act
    const { router } = openPage('/projects/OTHER/items/SMK-7');

    // Assert
    await screen.findByRole('heading', { level: 1 });
    expect(currentPath(router)).toBe('/projects/SMK/items/SMK-7');
  });
});

describe('項目頁：網址的編號', () => {
  it('網址的編號是別的寫法時換成正規的網址，不再多讀一次；改好的標題顯示在頁面上', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    server.use(http.get('/v1/items/smk-07', () => ok(task)));
    serveActivity(task.key, []);
    const patches = servePatch(task);
    const { user, router } = renderApp('/projects/SMK/items/smk-07');
    const heading = await screen.findByRole('heading', { level: 1 });
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/items/SMK-7');
    });
    expect(router.state.historyAction).toBe('REPLACE');

    // Act
    await user.click(within(heading).getByRole('button', { name: '標題：首頁設計稿' }));
    const input = within(heading).getByRole('textbox', { name: '標題' });
    await user.clear(input);
    await user.type(input, '首頁設計稿第二版{Enter}');

    // Assert
    expect(await within(heading).findByRole('button', { name: '標題：首頁設計稿第二版' })).toBeInTheDocument();
    expect(patches).toHaveLength(1);
  });
});

describe('項目頁：背景重抓失敗', () => {
  const STALE_ITEM = '無法更新這個項目的最新資料，畫面上可能是舊的。';

  it('已經有資料時重抓失敗，畫面與寫到一半的說明都留著，另外提示可以重試', async () => {
    // Arrange
    const { user, queryClient } = openPage();
    const region = await screen.findByRole('region', { name: '說明' });
    await user.click(within(region).getByRole('button', { name: '編輯說明' }));
    const editor = await findEditor('說明', region);
    await typeInEditor(user, editor, '，寫到一半');
    server.use(http.get('/v1/items/SMK-7', () => failure(503, 'server.not_ready', '服務正在啟動。')));

    // Act
    await queryClient.refetchQueries({ queryKey: ['item', 'SMK-7'], exact: true });

    // Assert
    expect(await screen.findByText(STALE_ITEM)).toBeInTheDocument();
    expect(screen.queryByText('項目載入失敗')).not.toBeInTheDocument();
    expect(editorText(editor)).toBe('第一段說明，寫到一半');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('首頁設計稿');
  });

  it('按提示裡的「重試」，讀得到之後提示消失，草稿還在', async () => {
    // Arrange
    const { user, queryClient } = openPage();
    const region = await screen.findByRole('region', { name: '說明' });
    await user.click(within(region).getByRole('button', { name: '編輯說明' }));
    const editor = await findEditor('說明', region);
    await typeInEditor(user, editor, '，寫到一半');
    server.use(http.get('/v1/items/SMK-7', () => failure(503, 'server.not_ready', '服務正在啟動。'), { once: true }));
    await queryClient.refetchQueries({ queryKey: ['item', 'SMK-7'], exact: true });
    const notice = (await screen.findByText(STALE_ITEM)).closest('div');

    // Act
    await user.click(within(notice as HTMLElement).getByRole('button', { name: '重試' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByText(STALE_ITEM)).not.toBeInTheDocument();
    });
    expect(editorText(editor)).toBe('第一段說明，寫到一半');
  });

  it('專案內容重抓失敗時也留著畫面，提示專案的資料可能是舊的', async () => {
    // Arrange
    const { queryClient } = openPage();
    await screen.findByRole('heading', { level: 1 });
    server.use(http.get('/v1/projects/SMK', () => failure(503, 'server.not_ready', '服務正在啟動。')));

    // Act
    await queryClient.refetchQueries({ queryKey: ['project', 'SMK'], exact: true });

    // Assert
    expect(await screen.findByText('無法更新專案的最新資料（欄位、成員與權限），畫面上可能是舊的。')).toBeInTheDocument();
    expect(screen.queryByText('專案載入失敗')).not.toBeInTheDocument();
    expect(screen.getByRole('region', { name: '欄位' })).toBeInTheDocument();
  });
});

describe('項目頁：權限', () => {
  it('只能看的人看得到值，但沒有可以編輯的地方', async () => {
    // Arrange & Act
    openPage(undefined, ['item.read', 'blueprint.read']);

    // Assert
    const heading = await screen.findByRole('heading', { level: 1 });
    expect(within(heading).queryByRole('button')).not.toBeInTheDocument();
    const fields = screen.getByRole('region', { name: '欄位' });
    expect(await within(fields).findByText('進行中')).toBeInTheDocument();
    expect(within(fields).queryByRole('button')).not.toBeInTheDocument();
    expect(within(fields).getByText('負責人').nextElementSibling).toHaveTextContent('範例使用者');
    const description = screen.getByRole('region', { name: '說明' });
    expect(within(description).getByText('第一段說明')).toBeInTheDocument();
    expect(within(description).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: '留言' })).not.toBeInTheDocument();
  });
});
