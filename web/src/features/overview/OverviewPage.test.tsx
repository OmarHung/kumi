import { act, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ok } from '../../test/api';
import { server } from '../../test/server';
import { saveProjectView } from '../projects/projectViewPreference';
import { signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { appOverview, emptyOverview, opsOverview, serveOverview, webOverview } from '../../test/overview';
import { currentPath, renderApp } from '../../test/renderApp';

async function findRow(projectKey: string): Promise<HTMLElement> {
  const table = await screen.findByRole('table', { name: '專案總覽' });
  return within(table).getByRole('row', { name: new RegExp(`^${projectKey} `) });
}

function rowOrder(): string[] {
  const table = screen.getByRole('table', { name: '專案總覽' });
  return within(table)
    .getAllByRole('rowheader')
    .map((header) => header.textContent.slice(0, 3));
}

describe('專案總覽', () => {
  it('左側導覽在「我的工作」下面有「專案總覽」，點了到這一頁', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([webOverview]);
    const { user } = renderApp('/my-work');

    // Act
    const nav = await screen.findByRole('navigation', { name: '主要導覽' });
    const links = within(nav).getAllByRole('link').map((link) => link.textContent);
    await user.click(within(nav).getByRole('link', { name: '專案總覽' }));

    // Assert
    expect(links.indexOf('專案總覽')).toBe(links.findIndex((text) => text.startsWith('我的工作')) + 1);
    expect(await screen.findByRole('heading', { level: 1, name: '專案總覽' })).toBeInTheDocument();
  });

  it('預設只看我管理的：送 scope=managed，每個專案一列，有代號、名稱與各個數字', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const requests = serveOverview([webOverview]);

    // Act
    renderApp('/overview');

    // Assert
    const row = await findRow('WEB');
    expect(within(row).getByRole('link', { name: 'WEB 官網改版' })).toHaveAttribute('href', '/projects/WEB');
    expect(within(row).getByRole('link', { name: 'WEB 還沒結束 7 個' })).toBeInTheDocument();
    expect(within(row).getByRole('link', { name: 'WEB 已過期 2 個' })).toBeInTheDocument();
    expect(within(row).getByRole('link', { name: 'WEB 7 天內到期 1 個' })).toBeInTheDocument();
    expect(within(row).getByRole('link', { name: 'WEB 沒有負責人 2 個' })).toBeInTheDocument();
    expect(within(row).getByRole('link', { name: 'WEB 停滯 1 個' })).toBeInTheDocument();
    expect(requests.at(-1)?.searchParams.get('scope')).toBe('managed');
  });

  it('數字點下去到專案的表格並帶著對應的篩選', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([webOverview]);
    renderApp('/overview');
    const row = await findRow('WEB');

    // Assert
    const hrefOf = (name: string) => within(row).getByRole('link', { name }).getAttribute('href');
    expect(hrefOf('WEB 還沒結束 7 個')).toBe('/projects/WEB?open=1');
    expect(hrefOf('WEB 已過期 2 個')).toBe('/projects/WEB?due=overdue');
    expect(hrefOf('WEB 7 天內到期 1 個')).toBe('/projects/WEB?due=soon');
    expect(hrefOf('WEB 沒有負責人 2 個')).toBe('/projects/WEB?assignee=unassigned&open=1');
    expect(hrefOf('WEB 停滯 1 個')).toBe('/projects/WEB?stale=1&open=1');
    expect(hrefOf('WEB 這期週期 第 12 週')).toBe('/projects/WEB?cycle=cycle-1');
    expect(hrefOf('WEB 下一個里程碑 正式上線')).toBe('/projects/WEB?milestone=milestone-1');
  });

  it('依狀態分段的橫條：一段一個狀態，連到那個狀態的清單', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([webOverview]);
    renderApp('/overview');
    const row = await findRow('WEB');

    // Act
    const bar = within(row).getByRole('list', { name: 'WEB 各狀態的項目數，共 13 個' });
    const segments = within(bar).getAllByRole('link');

    // Assert
    expect(segments.map((segment) => segment.getAttribute('aria-label'))).toEqual(['待辦 3', '進行中 4', '完成 6']);
    expect(segments.map((segment) => segment.getAttribute('href'))).toEqual([
      '/projects/WEB?state=todo',
      '/projects/WEB?state=doing',
      '/projects/WEB?state=done',
    ]);
  });

  it('數字是 0 時淡化、不是連結；最近完成只顯示、不是連結', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([webOverview, opsOverview]);
    renderApp('/overview');

    // Act
    const ops = await findRow('OPS');
    const web = await findRow('WEB');

    // Assert
    expect(within(ops).queryByRole('link', { name: /OPS .* \d+ 個/ })).not.toBeInTheDocument();
    expect(within(ops).getAllByText('0')).toHaveLength(6);
    expect(within(web).queryByRole('link', { name: /近 7 天完成/ })).not.toBeInTheDocument();
    expect(within(web).getByText('3')).toBeInTheDocument();
  });

  it('這期週期與下一個里程碑：名稱、日期與完成進度；沒有時說沒有', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([webOverview, emptyOverview]);
    renderApp('/overview');

    // Act
    const web = await findRow('WEB');
    const empty = await findRow('NEW');

    // Assert
    expect(within(web).getByText('2026/11/23 – 2026/12/06')).toBeInTheDocument();
    expect(within(web).getByText('完成 3／8')).toBeInTheDocument();
    expect(within(web).getByText('到期 2026/12/15')).toBeInTheDocument();
    expect(within(web).getByText('完成 6／9・取消 1')).toBeInTheDocument();
    expect(within(empty).getByText('沒有進行中的週期')).toBeInTheDocument();
    expect(within(empty).getByText('沒有即將到期的里程碑')).toBeInTheDocument();
    expect(within(empty).getByText('沒有項目')).toBeInTheDocument();
    // 最近更新寫在專案名稱下面；沒有項目的專案沒有更新時間，乾脆不寫那一行（不寫「更新於 還沒有更新」）。
    expect(within(empty).queryByText(/更新於/)).not.toBeInTheDocument();
    expect(within(web).getByText(/更新於/)).toBeInTheDocument();
  });

  it('只看得到自己項目的專案標明數字只算自己的', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([webOverview, appOverview]);
    renderApp('/overview');

    // Act
    const app = await findRow('APP');
    const web = await findRow('WEB');

    // Assert
    expect(within(app).getByText('只算你負責或建立的項目')).toBeInTheDocument();
    expect(within(web).queryByText('只算你負責或建立的項目')).not.toBeInTheDocument();
  });

  it('預設照已過期加停滯由多到少排；可以改依代號或最近更新，記在網址', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([opsOverview, appOverview, webOverview]);
    const { user, router } = renderApp('/overview');
    await findRow('WEB');

    // Assert 預設：APP 停滯 3、WEB 已過期 2 加停滯 1 同為 3 → 同分依代號，OPS 是 0
    expect(rowOrder()).toEqual(['APP', 'WEB', 'OPS']);

    // Act 依代號
    await user.click(screen.getByRole('combobox', { name: '排序' }));
    await user.click(await screen.findByRole('option', { name: '專案代號' }));

    // Assert
    await waitFor(() => { expect(rowOrder()).toEqual(['APP', 'OPS', 'WEB']); });
    expect(currentPath(router)).toBe('/overview?sort=key');

    // Act 依最近更新：WEB（11/26）、APP（10/01）、OPS 沒有更新時間排最後
    await user.click(screen.getByRole('combobox', { name: '排序' }));
    await user.click(await screen.findByRole('option', { name: '最近更新' }));

    // Assert
    await waitFor(() => { expect(rowOrder()).toEqual(['WEB', 'APP', 'OPS']); });
    expect(currentPath(router)).toBe('/overview?sort=updated');
  });

  it('切到「全部看得到的」：送 scope=visible，網址記下來；切回去就拿掉', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const requests = serveOverview([webOverview], [webOverview, appOverview]);
    const { user, router } = renderApp('/overview');
    await findRow('WEB');

    // Act
    await user.click(screen.getByRole('link', { name: '全部看得到的' }));

    // Assert
    await findRow('APP');
    expect(requests.at(-1)?.searchParams.get('scope')).toBe('visible');
    expect(currentPath(router)).toBe('/overview?scope=visible');
    expect(screen.getByRole('link', { name: '全部看得到的' })).toHaveAttribute('aria-current', 'page');

    // Act
    await user.click(screen.getByRole('link', { name: '只看我管理的' }));

    // Assert
    await waitFor(() => { expect(currentPath(router)).toBe('/overview'); });
  });

  it('沒有管理的專案時提示切到全部看得到的', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([], [webOverview]);
    const { user } = renderApp('/overview');

    // Act
    expect(await screen.findByText('你還沒有管理的專案')).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: '看全部看得到的專案' }));

    // Assert
    await findRow('WEB');
  });

  it('全部看得到的也沒有專案時，說明要請管理員加入', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([], []);

    // Act
    renderApp('/overview?scope=visible');

    // Assert
    expect(await screen.findByText('還沒有看得到的專案')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '看全部看得到的專案' })).not.toBeInTheDocument();
  });

  it('表格下面說明每個數字的算法，和篩選用同一組天數', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([webOverview]);
    renderApp('/overview');

    // Act
    const notes = await screen.findByRole('region', { name: '數字的算法' });

    // Assert
    expect(within(notes).getByText(/到期日在今天到 6 天後/)).toBeInTheDocument();
    expect(within(notes).getByText(/14 天沒有更新/)).toBeInTheDocument();
  });

  it('專案名稱照上次用的版面開，數字的連結照舊開表格', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([webOverview]);
    saveProjectView('WEB', 'board');
    renderApp('/overview');

    // Act
    const row = await findRow('WEB');

    // Assert
    expect(within(row).getByRole('link', { name: 'WEB 官網改版' })).toHaveAttribute('href', '/projects/WEB?view=board');
    expect(within(row).getByRole('link', { name: 'WEB 還沒結束 7 個' })).toHaveAttribute('href', '/projects/WEB?open=1');
    saveProjectView('WEB', 'table');
  });

  it('切換範圍時，新資料回來之前舊的列還在、標成忙碌，不閃成骨架', async () => {
    // Arrange：visible 的回應卡住，由測試決定何時放行。
    signedInAs(exampleOwner);
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/v1/overview/projects', async ({ request }) => {
        if (new URL(request.url).searchParams.get('scope') === 'visible') await gate;
        return ok((new URL(request.url).searchParams.get('scope') === 'visible' ? [webOverview, appOverview] : [webOverview]) as never);
      }),
    );
    const { user } = renderApp('/overview');
    await findRow('WEB');

    // Act
    await user.click(screen.getByRole('link', { name: '全部看得到的' }));

    // Assert
    expect(screen.getByRole('table', { name: '專案總覽' })).toBeInTheDocument();
    expect(screen.queryByText('正在載入專案總覽…')).not.toBeInTheDocument();
    expect(screen.getByRole('table', { name: '專案總覽' }).closest('[aria-busy]')).toHaveAttribute('aria-busy', 'true');

    // Act
    release();

    // Assert
    await waitFor(() => { expect(screen.getByRole('table', { name: '專案總覽' })).toHaveTextContent('行動應用'); });
  });
});

describe('專案總覽的「最近更新」', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('停在頁面上不動，相對時間每分鐘跟著更新', async () => {
    // Arrange：只假裝 Date 與 setInterval（總覽重抓、MSW 用的其他計時器照舊）。
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(new Date('2026-11-26T08:00:30Z'));
    signedInAs(exampleOwner);
    serveOverview([webOverview]);
    renderApp('/overview');
    const row = await findRow('WEB');
    expect(within(row).getByText('剛剛')).toBeInTheDocument();

    // Act
    await act(async () => {
      vi.setSystemTime(new Date('2026-11-26T08:04:30Z'));
      await vi.advanceTimersByTimeAsync(60_000);
    });

    // Assert
    await waitFor(() => { expect(within(row).getByText('5 分鐘前')).toBeInTheDocument(); });
  });
});
