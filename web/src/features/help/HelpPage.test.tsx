import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleMember } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { BELOW_WIDE_QUERY } from '../../lib/useMediaQuery';
import { findHelpChapter, HELP_CHAPTERS } from './helpChapters';

afterEach(() => {
  // jsdom 本來沒有 matchMedia 與 scrollIntoView：測試放的假的要拿掉，不留到別的測試。
  Reflect.deleteProperty(window, 'matchMedia');
  Reflect.deleteProperty(Element.prototype, 'scrollIntoView');
});

async function openHelp(path = '/help') {
  signedInAs(exampleMember);
  const view = renderApp(path);
  const heading = await screen.findByRole('heading', { level: 1, name: '快速上手' });
  return { ...view, heading };
}

describe('使用說明：章節', () => {
  it('/help 打開第一章（快速上手），位置是「使用說明」，分頁標題帶上章名', async () => {
    // Arrange / Act
    await openHelp();

    // Assert
    const crumbs = screen.getByRole('navigation', { name: '位置' });
    expect(within(crumbs).getByText('使用說明')).toBeInTheDocument();
    expect(document.title).toContain('快速上手 · 使用說明');
  });

  it('寬螢幕左邊的次導覽列出章節，目前這一章標 aria-current', async () => {
    // Arrange / Act
    await openHelp('/help/quick-start');

    // Assert
    const nav = screen.getByRole('navigation', { name: '使用說明' });
    expect(within(nav).getByText('開始使用')).toBeInTheDocument();
    expect(within(nav).getByRole('link', { name: '快速上手' })).toHaveAttribute('aria-current', 'page');
  });

  it('每一節是有名稱的區塊，標題是 h2：報讀軟體用地標或標題都跳得到', async () => {
    // Arrange / Act
    await openHelp();

    // Assert
    const article = screen.getByRole('article', { name: '快速上手' });
    const titles = within(article)
      .getAllByRole('heading', { level: 2 })
      .map((heading) => heading.textContent);
    expect(titles).toEqual([
      '1. 登入',
      '2. 認識畫面',
      '3. 看自己的工作：我的工作',
      '4. 進入專案',
      '5. 建立與修改項目',
      '6. 留言、提及與通知',
      '7. 請 AI 助理幫忙',
      '8. 接下來',
      '常見問題',
    ]);
    expect(within(article).getByRole('region', { name: '6. 留言、提及與通知' })).toHaveAttribute('id', 'notifications');
  });

  it('說明裡提到的頁面直接連過去：點「設定 → 通知」到通知設定', async () => {
    // Arrange
    const user = userEvent.setup();
    const { router } = await openHelp();
    const section = screen.getByRole('region', { name: '6. 留言、提及與通知' });

    // Act
    await user.click(within(section).getByRole('link', { name: '設定 → 通知' }));

    // Assert
    expect(currentPath(router)).toBe('/account/notifications');
  });

  it('畫面的示意圖：示意框不給報讀軟體，圖例是有序清單，編號對得上', async () => {
    // Arrange / Act
    await openHelp();

    // Assert
    const figure = screen.getByRole('figure', { name: '畫面的示意圖' });
    const legend = within(figure).getByRole('list');
    expect(within(legend).getAllByRole('listitem')).toHaveLength(5);
    expect(within(legend).getByRole('link', { name: '收件匣' })).toHaveAttribute('href', '/inbox');
    // 示意框裡也畫了「收件匣」，但它是 aria-hidden：整張圖只有圖例那一個收件匣讀得到。
    expect(within(figure).getAllByText('收件匣')).toHaveLength(2);
    expect(within(figure).getAllByRole('link', { name: '收件匣' })).toHaveLength(1);
  });

  it('步驟流程是有名稱的有序清單，一步一項', async () => {
    // Arrange / Act
    await openHelp();

    // Assert
    const flow = screen.getByRole('list', { name: 'AI 助理怎麼幫你改東西' });
    expect(flow.tagName).toBe('OL');
    expect(within(flow).getAllByRole('listitem').map((step) => step.textContent)).toEqual([
      '說你要做什麼可以附上會議紀錄、截圖',
      '助理提出建議卡列出要改什麼',
      '你按「套用」才會改以你的身分、照你的權限',
    ]);
  });

  it('常見問題平常只看得到問題，點了才展開答案', async () => {
    // Arrange
    const user = userEvent.setup();
    await openHelp();
    const faq = screen.getByRole('region', { name: '常見問題' });
    const question = within(faq).getByText('側欄沒有我要的專案。');
    const details = question.closest('details');

    // Act
    await user.click(question);

    // Assert
    expect(details).toHaveAttribute('open');
    expect(within(faq).getByText('你還不是那個專案的成員，請專案的管理員把你加進去。')).toBeVisible();
  });

  it('「接下來」的格子整格是連到那一頁的連結', async () => {
    // Arrange / Act
    await openHelp();

    // Assert
    const tiles = screen.getByRole('list', { name: '接下來可以看的頁面' });
    expect(within(tiles).getByRole('link', { name: /專案總覽/ })).toHaveAttribute('href', '/overview');
    expect(within(tiles).getByRole('link', { name: /命令列與整合/ })).toHaveAttribute('href', '/account/integrations');
  });

  it('網址帶著一節的錨點時捲到那一節，焦點放到它的標題', async () => {
    // Arrange
    const scrollIntoView = vi.fn();
    Object.defineProperty(Element.prototype, 'scrollIntoView', { value: scrollIntoView, configurable: true, writable: true });

    // Act
    await openHelp('/help/quick-start#notifications');

    // Assert
    const heading = screen.getByRole('heading', { level: 2, name: '6. 留言、提及與通知' });
    await waitFor(() => {
      expect(heading).toHaveFocus();
    });
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
  });

  it('不認得的章節是找不到的頁，可以回到使用說明', async () => {
    // Arrange
    signedInAs(exampleMember);

    // Act
    renderApp('/help/no-such-chapter');

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '找不到這一章' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到使用說明' })).toHaveAttribute('href', '/help');
  });
});

describe('使用說明：窄螢幕', () => {
  it('64rem 以下次導覽換成工具列上的下拉，按鈕上是目前的章名', async () => {
    // Arrange
    window.matchMedia = (query: string) =>
      ({
        matches: query === BELOW_WIDE_QUERY,
        media: query,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
      }) as unknown as MediaQueryList;

    // Act
    await openHelp();

    // Assert
    const nav = screen.getByRole('navigation', { name: '使用說明' });
    expect(within(nav).getByRole('combobox', { name: '使用說明的章節' })).toHaveTextContent('快速上手');
    expect(within(nav).queryByRole('link')).not.toBeInTheDocument();
  });
});

describe('使用說明：入口', () => {
  it('側欄最下面的「使用說明」每個人都有，點了到第一章', async () => {
    // Arrange
    const user = userEvent.setup();
    signedInAs(exampleMember);
    const { router } = renderApp('/my-work');
    const sidebar = await screen.findByRole('navigation', { name: '主要導覽' });

    // Act
    await user.click(within(sidebar).getByRole('link', { name: '使用說明' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '快速上手' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/help');
    expect(within(sidebar).getByRole('link', { name: '使用說明' })).toHaveAttribute('aria-current', 'page');
  });
});

describe('helpChapters', () => {
  it('沒有章節的那一段是第一章；不認得的是 undefined', () => {
    // Arrange / Act / Assert
    expect(findHelpChapter(undefined)).toBe(HELP_CHAPTERS[0]);
    expect(findHelpChapter('quick-start')?.label).toBe('快速上手');
    expect(findHelpChapter('nope')).toBeUndefined();
  });
});
