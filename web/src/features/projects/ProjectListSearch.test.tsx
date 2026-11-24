import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ProjectSearchHit } from '../../api/types';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { ok, signedInAs } from '../../test/api';
import { appProject, exampleOwner, webProject } from '../../test/fixtures';
import { serveProjectSearch } from '../../test/projectSearch';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

/** 等搜尋結果出現：整套並行跑時比預設的 1 秒慢。 */
const SLOW = { timeout: 3000 } as const;
const HOST_SNIPPET = '主機：web01.example.com';
const archivedApp = { ...appProject, archivedAt: '2026-11-18T08:00:00Z' };

function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

/** 伺服器的專案搜尋要在 signedInAs 之後設（後設的先生效），所以結果由這裡一起給。 */
async function projectsPage(path = '/projects', hits: readonly ProjectSearchHit[] = []) {
  signedInAs(exampleOwner, { projects: [webProject, appProject] });
  const requests = serveProjectSearch(hits);
  const view = renderApp(path);
  await screen.findByRole('heading', { level: 1, name: '專案' });
  return { ...view, requests };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('專案清單：搜尋', () => {
  it('頁列下面有搜尋框，提示說明可以搜說明；沒輸入時照舊列全部的專案、不送搜尋', async () => {
    // Act
    const { requests } = await projectsPage();

    // Assert
    const search = await screen.findByRole('searchbox', { name: '搜尋專案' });
    expect(search).toHaveAttribute('placeholder', '搜尋代號、名稱或說明，例如主機名稱');
    expect(await screen.findByRole('table', { name: '專案' }, SLOW)).toBeInTheDocument();
    expect(requests).toHaveLength(0);
  });

  it('輸入之後網址記下 q，改列伺服器的結果，多一欄「說明裡的」顯示摘錄', async () => {
    // Arrange
    const { user, router, requests } = await projectsPage('/projects', [
      { project: webProject, snippet: HOST_SNIPPET },
      { project: appProject, snippet: null },
    ]);

    // Act
    await user.click(await screen.findByRole('searchbox', { name: '搜尋專案' }));
    await user.paste('example');

    // Assert
    const table = await screen.findByRole('table', { name: '符合的專案' }, SLOW);
    const webRow = within(table).getByRole('row', { name: /WEB/ });
    expect(within(table).getByRole('columnheader', { name: '說明裡的' })).toBeInTheDocument();
    expect(within(webRow).getByText(HOST_SNIPPET)).toBeInTheDocument();
    expect(within(webRow).getByRole('link', { name: '官網改版' })).toHaveAttribute('href', '/projects/WEB');
    expect(within(table).getAllByRole('row')).toHaveLength(2);
    expect(currentPath(router)).toBe('/projects?q=example');
    expect([requests.at(-1)?.searchParams.get('q'), requests.at(-1)?.searchParams.get('limit')]).toEqual(['example', '20']);
  });

  it('網址帶 q 直接打開：搜尋框有字、結果是伺服器的（含封存的，多一欄封存於）', async () => {
    // Arrange
    // Act
    await projectsPage('/projects?q=example', [
      { project: webProject, snippet: HOST_SNIPPET },
      { project: archivedApp, snippet: '備註：example 的舊站' },
    ]);

    // Assert
    expect(await screen.findByRole('searchbox', { name: '搜尋專案' })).toHaveValue('example');
    const table = await screen.findByRole('table', { name: '符合的專案' }, SLOW);
    expect(within(table).getByRole('columnheader', { name: '封存於' })).toBeInTheDocument();
    expect(within(within(table).getByRole('row', { name: /APP/ })).getByText('2026/11/18')).toBeInTheDocument();
    expect(within(within(table).getByRole('row', { name: /WEB/ })).queryByText('2026/11/18')).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 2, name: '已封存的專案' })).not.toBeInTheDocument();
  });

  it('沒有結果：寫「找不到符合的專案。」', async () => {
    // Act
    await projectsPage('/projects?q=nothing');

    // Assert
    expect(await screen.findByText('找不到符合的專案。', undefined, SLOW)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('清空搜尋框：回到原本的清單（網址的 q 拿掉）', async () => {
    // Arrange
    const { user, router } = await projectsPage('/projects?q=example', [{ project: webProject, snippet: HOST_SNIPPET }]);
    await screen.findByRole('table', { name: '符合的專案' }, SLOW);

    // Act
    await user.clear(screen.getByRole('searchbox', { name: '搜尋專案' }));

    // Assert
    expect(await screen.findByRole('table', { name: '專案' }, SLOW)).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: '符合的專案' })).not.toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects');
  });

  it('搜尋失敗：結果的位置顯示錯誤，搜尋框還在，可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject, appProject] });
    server.use(http.get('/v1/search/projects', () => new Response(null, { status: 500 })));

    // Act
    renderApp('/projects?q=example');

    // Assert
    expect(await screen.findByRole('button', { name: '重試' }, SLOW)).toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: '搜尋專案' })).toBeInTheDocument();
  });

  it('伺服器只回前 20 筆：提示再輸入更多字縮小', async () => {
    // Arrange
    const many = Array.from({ length: 20 }, (_, index) => ({
      project: {
        ...webProject,
        id: `p-${String(index)}`,
        key: `P${String(index)}`,
        name: `範例 ${String(index)}`,
      },
      snippet: HOST_SNIPPET,
    }));

    // Act
    await projectsPage('/projects?q=example', many);

    // Assert
    expect(await screen.findByText('只列出前 20 個，可能還有更多；輸入更多字可以縮小範圍。', undefined, SLOW)).toBeInTheDocument();
  });

  it('窄螢幕：結果是清單，摘錄在第二行，封存的標封存於', async () => {
    // Arrange
    stubCompactScreen();

    // Act
    await projectsPage('/projects?q=example', [
      { project: webProject, snippet: HOST_SNIPPET },
      { project: archivedApp, snippet: '備註：example 的舊站' },
    ]);

    // Assert
    const list = await screen.findByRole('list', { name: '符合的專案' }, SLOW);
    const items = within(list).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent(`WEB 官網改版 說明裡的：${HOST_SNIPPET} 我的角色：管理員`);
    expect(within(items[0] as HTMLElement).getByRole('link')).toHaveAttribute('href', '/projects/WEB');
    expect(items[1]).toHaveTextContent('封存於');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('先輸入真正的字送出一次請求，再改成只有空白：回到原本的清單，不再送請求', async () => {
    // Arrange
    const { user, requests } = await projectsPage('/projects', [{ project: webProject, snippet: HOST_SNIPPET }]);
    const search = await screen.findByRole('searchbox', { name: '搜尋專案' });
    await user.click(search);
    await user.paste('example');
    await screen.findByRole('table', { name: '符合的專案' }, SLOW);
    expect(requests).toHaveLength(1);

    // Act
    await user.clear(search);
    await user.paste('   ');

    // Assert
    expect(await screen.findByRole('table', { name: '專案' }, SLOW)).toBeInTheDocument();
    expect(requests).toHaveLength(1);
  });

  it('改字時先留著舊的結果（變淡、aria-busy），不變成骨架；新的結果回來才換', async () => {
    // Arrange
    const { user } = await projectsPage('/projects?q=example', [
      { project: webProject, snippet: HOST_SNIPPET },
      { project: appProject, snippet: '網址：app.example.com' },
    ]);
    const first = await screen.findByRole('table', { name: '符合的專案' }, SLOW);
    expect(within(first).getAllByRole('row')).toHaveLength(3);
    let release: () => void = () => undefined;
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/v1/search/projects', async () => {
        await released;
        return ok([{ project: appProject, snippet: '網址：app.example.com' }]);
      }),
    );

    // Act
    const search = screen.getByRole('searchbox', { name: '搜尋專案' });
    await user.click(search);
    await user.paste(' app');
    await waitFor(() => {
      expect(screen.getByRole('table', { name: '符合的專案' }).closest('[aria-busy]')).toHaveAttribute('aria-busy', 'true');
    }, SLOW);

    // Assert
    expect(screen.queryByText('正在搜尋專案…')).not.toBeInTheDocument();
    release();
    await waitFor(() => {
      expect(within(screen.getByRole('table', { name: '符合的專案' })).getAllByRole('row')).toHaveLength(2);
    }, SLOW);
    expect(screen.getByRole('table', { name: '符合的專案' }).closest('[aria-busy]')).toHaveAttribute('aria-busy', 'false');
  });

  it('結果數用 aria-live 的區域說給報讀軟體：找到幾個、找不到', async () => {
    // Arrange
    const { user } = await projectsPage('/projects?q=example', [
      { project: webProject, snippet: HOST_SNIPPET },
      { project: appProject, snippet: '網址：app.example.com' },
    ]);
    const live = () => document.querySelector('main [aria-live="polite"]');

    // Act
    await screen.findByRole('table', { name: '符合的專案' }, SLOW);
    const withResults = live()?.textContent;
    const search = screen.getByRole('searchbox', { name: '搜尋專案' });
    await user.clear(search);
    await user.paste('nothing');
    await screen.findByText('找不到符合的專案。', undefined, SLOW);

    // Assert
    expect(withResults).toBe('找到 2 個專案');
    expect(live()?.textContent).toBe('找不到符合的專案');
  });

  it('搜尋框限制 200 字；網址的 q 太長就截到 200 字，有控制字元的拿掉，不送一定 422 的請求', async () => {
    // Arrange、Act
    const { requests } = await projectsPage(`/projects?q=${'a'.repeat(300)}`);

    // Assert
    expect(await screen.findByRole('searchbox', { name: '搜尋專案' })).toHaveAttribute('maxlength', '200');
    await waitFor(() => {
      expect(requests.at(-1)?.searchParams.get('q')).toHaveLength(200);
    }, SLOW);
  });

  it('網址的 q 只有控制字元：當作沒有輸入，列原本的清單、不送請求', async () => {
    // Arrange、Act
    const { requests } = await projectsPage('/projects?q=%0A%00');

    // Assert
    expect(await screen.findByRole('table', { name: '專案' }, SLOW)).toBeInTheDocument();
    expect(requests).toHaveLength(0);
  });
});
