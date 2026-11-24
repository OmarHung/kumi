import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { serveActivity } from '../../test/activity';
import { ok, signedInAs } from '../../test/api';
import { agyDetail, agyItem, agyProject, exampleMember, exampleOwner, smkItem, smkProject, webProject } from '../../test/fixtures';
import { detailOf, serveItem, serveItems, serveSearch } from '../../test/items';
import { serveProjectSearch } from '../../test/projectSearch';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { recordRecentItem } from './recentItems';

const OPEN = '{Control>}k{/Control}';

async function openJump(user: ReturnType<typeof renderApp>['user']) {
  await user.keyboard(OPEN);
  const dialog = await screen.findByRole('dialog', { name: '搜尋或跳到' });
  return {
    dialog,
    input: within(dialog).getByRole('combobox', { name: '搜尋或跳到' }),
  };
}

async function myWorkPage() {
  signedInAs(exampleOwner, { projects: [agyProject, smkProject] });
  const view = renderApp('/my-work');
  await screen.findByRole('heading', { level: 1, name: '我的工作' });
  return view;
}

function optionNames(dialog: HTMLElement): string[] {
  return within(dialog)
    .queryAllByRole('option')
    .map((option) => option.textContent);
}

// 設定中心的頁面接在「頁面」組裡，旁邊寫「設定」（和專案的「設定」分得開）。
const PERSONAL_HUB_OPTIONS = ['我的帳號 個人設定', '通知 個人設定', 'AI 帳號 個人設定', '命令列與整合 個人設定'];
const HUB_OPTIONS_OWNER_BEFORE_PROJECT = [
  '我的工作',
  '知識庫',
  '專案',
  '人員',
  ...PERSONAL_HUB_OPTIONS,
  '說明格式 組織設定',
  '寄信設定 組織設定',
  'AI 設定 組織設定',
  '主機 AI 代理 組織設定',
];

describe('快速跳轉：打開與關上', () => {
  it('Ctrl+K 打開：焦點在輸入框，Esc 關上之後焦點回到原本的元素', async () => {
    // Arrange
    const { user } = await myWorkPage();
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    const link = within(nav).getByRole('link', { name: '我的工作' });
    link.focus();

    // Act
    const { input } = await openJump(user);
    const focusedWhenOpen = document.activeElement;
    await user.keyboard('{Escape}');

    // Assert
    expect(focusedWhenOpen).toBe(input);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(link).toHaveFocus();
  });

  it('側欄最上面有「搜尋或跳到…」，看得到快捷鍵；按了打開，關上後焦點回到按鈕', async () => {
    // Arrange
    const { user } = await myWorkPage();
    const button = screen.getByRole('button', { name: '搜尋或跳到…' });

    // Act
    await user.click(button);
    const dialog = await screen.findByRole('dialog', { name: '搜尋或跳到' });
    await user.click(within(dialog).getByRole('button', { name: '關閉' }));

    // Assert
    expect(button).toHaveAttribute('aria-keyshortcuts', 'Control+K');
    expect(button).toHaveTextContent('Ctrl+K');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });

  it('對話框的標題（也是輸入框的名稱）是「搜尋或跳到」，側欄的按鈕才帶省略號', async () => {
    // Arrange
    const { user } = await myWorkPage();

    // Act
    const { dialog, input } = await openJump(user);

    // Assert
    expect(within(dialog).getByRole('heading', { level: 2 })).toHaveTextContent(/^搜尋或跳到$/);
    expect(input).toHaveAccessibleName('搜尋或跳到');
    expect(screen.getByRole('button', { name: '搜尋或跳到…' })).toBeInTheDocument();
  });

  it('窄螢幕的抽屜裡按「搜尋或跳到…」：先收起抽屜，關上之後焦點回到「選單」', async () => {
    // Arrange
    const { user } = await myWorkPage();
    const menuButton = screen.getByRole('button', { name: '選單' });
    await user.click(menuButton);

    // Act
    await user.click(screen.getByRole('button', { name: '搜尋或跳到…' }));
    await screen.findByRole('dialog', { name: '搜尋或跳到' });
    const drawerWhileOpen = menuButton.getAttribute('aria-expanded');
    await user.keyboard('{Escape}');

    // Assert
    expect(drawerWhileOpen).toBe('false');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(menuButton).toHaveFocus();
  });

  it('抽屜開著時按 Ctrl+K 也一樣：抽屜收起來，關上之後焦點回到「選單」', async () => {
    // Arrange
    const { user } = await myWorkPage();
    const menuButton = screen.getByRole('button', { name: '選單' });
    await user.click(menuButton);

    // Act
    await openJump(user);
    const drawerWhileOpen = menuButton.getAttribute('aria-expanded');
    await user.keyboard('{Escape}');

    // Assert
    expect(drawerWhileOpen).toBe('false');
    expect(menuButton).toHaveFocus();
  });

  it('已經開著時再按 Ctrl+K 不會多開一個，焦點留在輸入框', async () => {
    // Arrange
    const { user } = await myWorkPage();
    const { input } = await openJump(user);

    // Act
    await user.keyboard(OPEN);

    // Assert
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(input).toHaveFocus();
  });

  it('別的對話框開著時 Ctrl+K 不打開', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [agyProject] });
    const { user } = renderApp('/projects');
    await user.click(await screen.findByRole('button', { name: '新增專案' }));
    const other = await screen.findByRole('dialog');

    // Act
    await user.keyboard(OPEN);

    // Assert
    expect(screen.getAllByRole('dialog')).toEqual([other]);
  });
});

describe('快速跳轉：combobox', () => {
  it('輸入框是 combobox，指向結果的 listbox；選項分組，每一組有名稱', async () => {
    // Arrange
    recordRecentItem({ key: 'AGY-3', projectKey: 'AGY', title: '首頁設計稿' });
    const { user } = await myWorkPage();

    // Act
    const { dialog, input } = await openJump(user);

    // Assert
    const listbox = within(dialog).getByRole('listbox');
    expect(input).toHaveAttribute('aria-controls', listbox.id);
    expect(input).toHaveAttribute('aria-expanded', 'true');
    expect(input).toHaveAttribute('aria-autocomplete', 'list');
    const groups = within(listbox).getAllByRole('group');
    expect(groups.map((group) => within(group).getAllByRole('option').length)).toEqual([1, 2, 12]);
    expect(within(listbox).getByRole('group', { name: '最近看過' })).toHaveTextContent('AGY-3 首頁設計稿');
    expect(within(listbox).getByRole('group', { name: '專案' })).toBeInTheDocument();
    expect(within(listbox).getByRole('group', { name: '頁面' })).toBeInTheDocument();
  });

  it('方向鍵選（aria-activedescendant），焦點一直留在輸入框；到底再按回到第一個', async () => {
    // Arrange
    const { user } = await myWorkPage();
    const { dialog, input } = await openJump(user);
    const options = within(dialog).getAllByRole('option');

    // Act
    const first = input.getAttribute('aria-activedescendant');
    await user.keyboard('{ArrowDown}');
    const second = input.getAttribute('aria-activedescendant');
    const selectedAfterDown = options.map((option) => option.getAttribute('aria-selected'));
    await user.keyboard('{ArrowUp}{ArrowUp}');
    const wrapped = input.getAttribute('aria-activedescendant');

    // Assert
    expect(first).toBe(options[0]?.id);
    expect(second).toBe(options[1]?.id);
    expect(selectedAfterDown.slice(0, 2)).toEqual(['false', 'true']);
    expect(selectedAfterDown.filter((selected) => selected === 'true')).toHaveLength(1);
    expect(wrapped).toBe(options.at(-1)?.id);
    expect(input).toHaveFocus();
  });

  it('打字就篩選，status 說找到幾筆；沒有符合時說明，輸入框標 aria-expanded=false', async () => {
    // Arrange
    const { user } = await myWorkPage();
    const { dialog, input } = await openJump(user);

    // Act
    await user.type(input, '冒煙');
    const names = optionNames(dialog);
    const status = within(dialog).getByRole('status');
    await waitFor(() => {
      expect(status).toHaveTextContent('找到 1 筆');
    });
    await user.type(input, '找不到');

    // Assert
    expect(names).toEqual(['SMK 冒煙測試']);
    await waitFor(() => {
      expect(status).toHaveTextContent('沒有符合的結果');
    });
    expect(input).toHaveAttribute('aria-expanded', 'false');
    expect(input).not.toHaveAttribute('aria-activedescendant');
  });

  it('status 不跟著每一個字換（不和字元的回音搶著唸）：停下來才說找到幾筆', async () => {
    // Arrange
    const { user } = await myWorkPage();
    const { dialog, input } = await openJump(user);
    const status = within(dialog).getByRole('status');
    const before = status.textContent;

    // Act：同步改值（不經過 user-event 的非同步步驟），讀到的一定是還沒停下來時的 status。
    fireEvent.change(input, { target: { value: '冒' } });
    const whileTyping = status.textContent;
    await waitFor(() => {
      expect(status).toHaveTextContent('找到 1 筆。');
    });

    // Assert
    expect(before).not.toBe('找到 1 筆。');
    expect(whileTyping).toBe(before);
  });

  it('不在專案裡也搜得到項目：用 q 與 limit 送到跨專案的搜尋，別的專案的項目帶專案名稱', async () => {
    // Arrange
    const { user } = await myWorkPage();
    const requests = serveSearch([agyItem({ number: 1, title: '首頁設計稿' }), smkItem({ number: 4, title: '首頁看板' })]);
    const { dialog, input } = await openJump(user);

    // Act
    await user.type(input, '首頁');
    const group = await within(dialog).findByRole('group', { name: '項目' });

    // Assert
    expect(input).toHaveAttribute('placeholder', '輸入編號、標題、專案、主機名稱或頁面');
    expect(input).toHaveAccessibleDescription('輸入編號、標題、專案、主機名稱或頁面');
    expect(requests.map((url) => [url.searchParams.get('q'), url.searchParams.get('limit')])).toEqual([['首頁', '8']]);
    expect(
      within(group)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(['AGY-1 首頁設計稿 接案測試', 'SMK-4 首頁看板 冒煙測試']);
    expect(within(dialog).getByRole('status')).toHaveTextContent('找到 2 筆');
  });

  it('清空輸入時馬上清空結果，也不再送搜尋；換字之後舊的結果不留著', async () => {
    // Arrange
    const { user } = await myWorkPage();
    const requests = serveSearch([agyItem({ number: 1, title: '首頁設計稿' })]);
    const { dialog, input } = await openJump(user);
    await user.type(input, '首頁');
    await within(dialog).findByRole('group', { name: '項目' });

    // Act
    await user.clear(input);

    // Assert
    expect(within(dialog).queryByRole('group', { name: '項目' })).not.toBeInTheDocument();
    expect(requests).toHaveLength(1);
  });

  it('輸入法選字中的 Enter、方向鍵、Esc 不被吃掉：不前往、不移動、不關上', async () => {
    // Arrange
    const { user, router } = await myWorkPage();
    const { input } = await openJump(user);
    const before = input.getAttribute('aria-activedescendant');

    // Act
    fireEvent.keyDown(input, { key: 'Enter', keyCode: 229 });
    fireEvent.keyDown(input, { key: 'ArrowDown', isComposing: true });
    fireEvent.keyDown(input, { key: 'Escape', isComposing: true });

    // Assert
    expect(currentPath(router)).toBe('/my-work');
    expect(input.getAttribute('aria-activedescendant')).toBe(before);
    expect(screen.getByRole('dialog', { name: '搜尋或跳到' })).toBeInTheDocument();
  });
});

/** 等搜尋結果出現：整套並行跑時比預設的 1 秒慢。 */
const SLOW = { timeout: 3000 } as const;

describe('快速跳轉：用專案說明找專案', () => {
  async function pageWithWeb() {
    signedInAs(exampleOwner, {
      projects: [agyProject, smkProject, webProject],
    });
    const view = renderApp('/my-work');
    await screen.findByRole('heading', { level: 1, name: '我的工作' });
    return view;
  }

  it('說明比對到的專案接在專案組裡，靠右是摘錄；用 q 與 limit 送到專案搜尋', async () => {
    // Arrange
    const { user } = await pageWithWeb();
    const requests = serveProjectSearch([{ project: webProject, snippet: '主機：web01.example.com' }]);
    const { dialog, input } = await openJump(user);

    // Act
    await user.click(input);
    await user.paste('web01');
    const option = await within(dialog).findByRole('option', { name: /官網改版/ }, SLOW);

    // Assert
    expect(option).toHaveTextContent('WEB 官網改版 主機：web01.example.com');
    expect([requests.at(-1)?.searchParams.get('q'), requests.at(-1)?.searchParams.get('limit')]).toEqual(['web01', '8']);
    expect(within(within(dialog).getByRole('group', { name: '專案' })).getAllByRole('option')).toHaveLength(1);
  });

  it('選了說明比對到的專案：前往專案頁', async () => {
    // Arrange
    const { user, router } = await pageWithWeb();
    serveProjectSearch([{ project: webProject, snippet: '主機：web01.example.com' }]);
    const { dialog, input } = await openJump(user);
    await user.click(input);
    await user.paste('web01');

    // Act
    await user.click(await within(dialog).findByRole('option', { name: /官網改版/ }, SLOW));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/WEB');
    });
  });

  it('代號或名稱就比對到的專案不重複：只出現一次，不帶摘錄', async () => {
    // Arrange
    const { user } = await pageWithWeb();
    serveProjectSearch([
      { project: webProject, snippet: null },
      { project: smkProject, snippet: '備註：官網的冒煙測試' },
    ]);
    const { dialog, input } = await openJump(user);

    // Act
    await user.click(input);
    await user.paste('官網');
    await within(dialog).findByText(/備註：官網的冒煙測試/, undefined, SLOW);

    // Assert
    const options = within(within(dialog).getByRole('group', { name: '專案' })).getAllByRole('option');
    expect(options.map((option) => option.textContent)).toEqual(['WEB 官網改版', 'SMK 冒煙測試 備註：官網的冒煙測試']);
  });

  it('專案說明的搜尋失敗：前端比對的專案還在，下面說哪一個搜尋失敗', async () => {
    // Arrange
    const { user } = await pageWithWeb();
    server.use(http.get('/v1/search/projects', () => new Response(null, { status: 500 })));
    const { dialog, input } = await openJump(user);

    // Act
    await user.click(input);
    await user.paste('官網');
    const message = await within(dialog).findByText('專案說明搜尋失敗，請稍後再試。', undefined, SLOW);

    // Assert
    expect(message).toBeInTheDocument();
    expect(within(dialog).getByRole('option', { name: /官網改版/ })).toBeInTheDocument();
  });

  it('封存的專案帶很長的摘錄：「已封存」是獨立的標籤，摘錄自己截斷，列上一定看得到封存', async () => {
    // Arrange
    const { user } = await pageWithWeb();
    const archived = { ...webProject, archivedAt: '2026-11-18T08:00:00Z' };
    const longLine = `網址：https://www.example.com/${'a'.repeat(60)}`;
    serveProjectSearch([{ project: archived, snippet: longLine }]);
    const { dialog, input } = await openJump(user);

    // Act
    await user.click(input);
    await user.paste('example');
    const option = await within(dialog).findByRole('option', { name: /官網改版/ }, SLOW);

    // Assert
    const tag = within(option).getByText('已封存');
    expect(tag).not.toHaveTextContent(longLine);
    expect(within(option).getByText(longLine)).not.toBe(tag);
    expect(option).toHaveTextContent(`WEB 官網改版 已封存 ${longLine}`);
  });
});

describe('快速跳轉：前往', () => {
  it('Ctrl+K、輸入 agy-12、Enter：直接到 AGY-12 的項目頁，焦點到標題', async () => {
    // Arrange
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    signedInAs(exampleOwner, { details: [agyDetail()] });
    const item = detailOf(agyItem({ number: 12, title: '首頁改版' }));
    serveItem(item);
    serveActivity(item.key, []);
    serveItems('AGY', []);
    const { user, router } = renderApp('/my-work');
    await screen.findByRole('heading', { level: 1, name: '我的工作' });

    // Act
    const { dialog, input } = await openJump(user);
    await user.type(input, 'agy-12');
    const first = within(dialog).getAllByRole('option')[0];
    await user.keyboard('{Enter}');

    // Assert
    expect(first).toHaveTextContent('前往 AGY-12');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/AGY/items/AGY-12');
    const heading = await screen.findByRole('heading', {
      level: 1,
      name: /首頁改版/,
    });
    await waitFor(() => {
      expect(heading).toHaveFocus();
    });
  });

  it('用滑鼠點選項：關上並換到那一頁，焦點到那一頁的標題', async () => {
    // Arrange
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    const { user, router } = await myWorkPage();
    const { dialog } = await openJump(user);

    // Act
    await user.click(within(dialog).getByRole('option', { name: '專案' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects');
    const heading = await screen.findByRole('heading', {
      level: 1,
      name: '專案',
    });
    await waitFor(() => {
      expect(heading).toHaveFocus();
    });
  });

  it('選了目前所在的頁面：焦點照樣到標題', async () => {
    // Arrange
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    const { user, router } = await myWorkPage();
    const { input } = await openJump(user);

    // Act
    await user.type(input, '我的工作');
    await user.keyboard('{Enter}');

    // Assert
    expect(currentPath(router)).toBe('/my-work');
    const heading = screen.getByRole('heading', { level: 1, name: '我的工作' });
    await waitFor(() => {
      expect(heading).toHaveFocus();
    });
  });
});

describe('快速跳轉：在專案裡', () => {
  function projectPage(permissions = agyDetail().myPermissions) {
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    signedInAs(exampleOwner, {
      projects: [smkProject],
      details: [agyDetail({ myPermissions: permissions })],
    });
    const requests = serveSearch([
      smkItem({ number: 4, title: '首頁看板' }),
      agyItem({ number: 1, title: '首頁設計稿' }),
      agyItem({ number: 2, title: '首頁的頁尾' }),
      agyItem({ number: 3, title: '登入' }),
    ]);
    return { view: renderApp('/projects/AGY'), requests };
  }

  it('在專案裡也搜全部專案：順序照伺服器的，不把目前專案提前；目前專案的項目不帶專案名稱', async () => {
    // Arrange
    const { view, requests } = projectPage();
    await screen.findByRole('heading', { level: 1, name: '接案測試' });
    const { dialog, input } = await openJump(view.user);

    // Act
    await view.user.type(input, '首頁');
    const group = await within(dialog).findByRole('group', { name: '項目' });

    // Assert
    expect(input).toHaveAttribute('placeholder', '輸入編號、標題、專案、主機名稱或頁面');
    expect(requests.map((url) => [url.searchParams.get('q'), url.searchParams.get('limit')])).toEqual([['首頁', '8']]);
    expect(
      within(group)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(['SMK-4 首頁看板 冒煙測試', 'AGY-1 首頁設計稿', 'AGY-2 首頁的頁尾']);
    expect(within(dialog).getByRole('status')).toHaveTextContent('找到 3 筆');
  });

  it('搜尋中與沒有結果都有說明', async () => {
    // Arrange
    const { view } = projectPage();
    await screen.findByRole('heading', { level: 1, name: '接案測試' });
    const { dialog, input } = await openJump(view.user);
    let answer: () => void = () => undefined;
    const answered = new Promise<void>((resolve) => {
      answer = resolve;
    });
    server.use(
      http.get('/v1/search/items', async () => {
        await answered;
        return ok({ items: [] });
      }),
    );

    // Act
    await view.user.type(input, '沒有這種');
    const status = within(dialog).getByRole('status');
    await waitFor(() => {
      expect(status).toHaveTextContent('正在搜尋…');
    });
    answer();

    // Assert
    await waitFor(() => {
      expect(status).toHaveTextContent('沒有符合的結果');
    });
  });

  it('頁面有這個專案的設定、成員、變更集；沒有 blueprint.read 只有成員', async () => {
    // Arrange
    const { view } = projectPage(['item.read', 'item.create', 'item.update', 'item.transition']);
    await screen.findByRole('heading', { level: 1, name: '接案測試' });

    // Act
    const { dialog } = await openJump(view.user);

    // Assert
    const pages = within(dialog).getByRole('group', { name: '頁面' });
    expect(
      within(pages)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual([...HUB_OPTIONS_OWNER_BEFORE_PROJECT, '成員 接案測試']);
  });

  it('選「成員」在原地打開成員對話框，關上後焦點回到打開快速跳轉前的元素', async () => {
    // Arrange
    server.use(http.get('/v1/people', () => ok([])));
    const { view } = projectPage();
    const heading = await screen.findByRole('heading', {
      level: 1,
      name: '接案測試',
    });
    // 側欄也有一個「設定」（設定中心）：要的是專案頁頁列的那一個。
    const settings = screen.getAllByRole('link', { name: '設定' }).find((link) => link.getAttribute('href') === '/projects/AGY/settings');
    if (settings === undefined) throw new Error('找不到專案的「設定」連結');
    settings.focus();
    const { input } = await openJump(view.user);

    // Act
    await view.user.type(input, '成員');
    await view.user.keyboard('{Enter}');
    const members = await screen.findByRole('dialog', { name: '成員' });
    await view.user.click(within(members).getByRole('button', { name: '關閉' }));

    // Assert
    expect(heading).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(settings).toHaveFocus();
  });

  it('組織的一般成員：頁面沒有人員', async () => {
    // Arrange
    signedInAs(exampleMember, { projects: [agyProject] });
    const { user } = renderApp('/my-work');
    await screen.findByRole('heading', { level: 1, name: '我的工作' });

    // Act
    const { dialog } = await openJump(user);

    // Assert
    const pages = within(dialog).getByRole('group', { name: '頁面' });
    expect(
      within(pages)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(['我的工作', '知識庫', '專案', ...PERSONAL_HUB_OPTIONS]);
  });
});

describe('快速跳轉：封存的專案', () => {
  it('找得到，排在沒封存的後面，靠右標「已封存」', async () => {
    // Arrange
    signedInAs(exampleOwner, {
      projects: [{ ...agyProject, archivedAt: '2026-11-18T08:00:00Z' }, smkProject],
    });
    const { user } = renderApp('/my-work');
    await screen.findByRole('heading', { level: 1, name: '我的工作' });

    // Act
    const { dialog } = await openJump(user);

    // Assert
    const projects = optionNames(dialog).filter((name) => /SMK|AGY/.test(name) && !name.includes('前往'));
    expect(projects[0]).toContain('冒煙測試');
    expect(projects[1]).toContain('接案測試');
    expect(projects[1]).toContain('已封存');
    expect(projects[0]).not.toContain('已封存');
  });
});
