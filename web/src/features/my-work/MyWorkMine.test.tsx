import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { signedInAs } from '../../test/api';
import { serveCrossProjectItems } from '../../test/crossProjectItems';
import { daysFromToday, exampleMember, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';

const mine = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };
const partner = { personId: '0190a000-0000-7000-8000-000000000003', displayName: '範例夥伴' };

const assignedToMe = smkItem({ number: 1, title: '我負責的稿', createdBy: partner });
const createdByMe = smkItem({ number: 2, title: '我開的稿', assignee: partner, createdBy: mine });
const createdByMeUnassigned = smkItem({ number: 3, title: '還沒指派的稿', assignee: null, createdBy: mine });
const ITEMS = [assignedToMe, createdByMe, createdByMeUnassigned];

function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

function arrange() {
  signedInAs(exampleOwner, { details: [smkDetail()], items: ITEMS });
  return serveCrossProjectItems(ITEMS, exampleOwner.personId);
}

async function findToolbar() {
  return within(await screen.findByRole('navigation', { name: '範圍' }));
}

describe('我的工作：我負責的／我建立的', () => {
  it('預設是我負責的，網址不帶 mine，請求送 assignee=me', async () => {
    // Arrange
    const requests = arrange();

    // Act
    renderApp('/my-work');

    // Assert
    const scope = await findToolbar();
    expect(scope.getByRole('link', { name: '我負責的' })).toHaveAttribute('aria-current', 'page');
    expect(scope.getByRole('link', { name: '我建立的' })).not.toHaveAttribute('aria-current');
    expect(await screen.findByRole('link', { name: '我負責的稿' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '我開的稿' })).not.toBeInTheDocument();
    expect(requests.at(-1)?.searchParams.get('assignee')).toBe('me');
    expect(requests.at(-1)?.searchParams.has('createdBy')).toBe(false);
    expect(screen.queryByRole('columnheader', { name: /負責人/ })).not.toBeInTheDocument();
  });

  it('切到我建立的：寫進網址 mine=created，請求送 createdBy=me、不送 assignee，列上多一欄負責人', async () => {
    // Arrange
    const requests = arrange();
    const { user, router } = renderApp('/my-work');
    const scope = await findToolbar();

    // Act
    await user.click(scope.getByRole('link', { name: '我建立的' }));

    // Assert
    expect(await screen.findByRole('link', { name: '我開的稿' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '還沒指派的稿' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '我負責的稿' })).not.toBeInTheDocument();
    expect(currentPath(router)).toBe('/my-work?mine=created');
    const last = requests.at(-1)?.searchParams;
    expect(last?.get('createdBy')).toBe('me');
    expect(last?.has('assignee')).toBe(false);
    expect(await screen.findByRole('columnheader', { name: /負責人/ })).toBeInTheDocument();
    const row = screen.getByRole('row', { name: /我開的稿/ });
    expect(within(row).getByText('範例夥伴')).toBeInTheDocument();
    expect(within(screen.getByRole('row', { name: /還沒指派的稿/ })).getByText('沒有負責人')).toBeInTheDocument();
  });

  it('從網址 mine=created 進來就是我建立的；切回我負責的就把 mine 拿掉', async () => {
    // Arrange
    arrange();
    const { user, router } = renderApp('/my-work?mine=created');
    const scope = await findToolbar();
    expect(scope.getByRole('link', { name: '我建立的' })).toHaveAttribute('aria-current', 'page');

    // Act
    await user.click(scope.getByRole('link', { name: '我負責的' }));

    // Assert
    expect(await screen.findByRole('link', { name: '我負責的稿' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/my-work');
  });

  it('範圍不算條件：只有它時沒有「清除條件」；有別的條件時清除之後範圍留著', async () => {
    // Arrange
    arrange();
    const { user, router } = renderApp('/my-work?mine=created&priority=high');
    const bar = within(await screen.findByRole('group', { name: '篩選項目' }));

    // Act
    await user.click(await bar.findByRole('button', { name: '清除條件' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work?mine=created');
    });
    expect(bar.queryByRole('button', { name: '清除條件' })).not.toBeInTheDocument();
  });

  it('我建立的而且沒有項目：說的是建立的項目，不是負責的工作', async () => {
    // Arrange
    signedInAs(exampleMember, { myWork: [] });

    // Act
    renderApp('/my-work?mine=created');

    // Assert
    expect(await screen.findByText('你還沒有建立任何還沒結束的項目')).toBeInTheDocument();
    expect(screen.queryByText('目前沒有你負責的工作')).not.toBeInTheDocument();
  });

  it('切換版面時範圍跟著帶過去', async () => {
    // Arrange
    arrange();
    const { user, router } = renderApp('/my-work?mine=created');
    const tabs = await screen.findByRole('navigation', { name: '版面' });

    // Act
    await user.click(within(tabs).getByRole('link', { name: '看板' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work?view=board&mine=created');
    });
  });

  it('範圍的連結只換 mine：條件、排序與版面都帶著，目前的那個標成 aria-current', async () => {
    // Arrange
    arrange();
    renderApp('/my-work?view=board&priority=high&q=稿');

    // Act
    const scope = await findToolbar();

    // Assert
    const created = scope.getByRole('link', { name: '我建立的' });
    expect(new URLSearchParams(created.getAttribute('href')?.split('?')[1]).toString()).toBe(
      new URLSearchParams('view=board&mine=created&q=稿&priority=high').toString(),
    );
    expect(scope.getByRole('link', { name: '我負責的' })).toHaveAttribute('aria-current', 'page');
  });

  it('窄螢幕換成下拉，放在同一列（不另開一列）；選「我建立的」寫進網址，請求送 createdBy=me', async () => {
    // Arrange
    stubCompactScreen();
    const requests = arrange();
    const { user, router } = renderApp('/my-work');
    const select = await screen.findByRole('combobox', { name: '範圍' });
    expect(screen.queryByRole('navigation', { name: '範圍' })).not.toBeInTheDocument();

    // Act
    await user.click(select);
    await user.click(await screen.findByRole('option', { name: '我建立的' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/my-work?mine=created');
    });
    expect(await screen.findByRole('link', { name: /我開的稿/ })).toBeInTheDocument();
    expect(requests.at(-1)?.searchParams.get('createdBy')).toBe('me');
  });

  it('窄螢幕的範圍下拉是工具列的第一顆（在版面之前），頁列只剩標題', async () => {
    // Arrange
    stubCompactScreen();
    arrange();
    renderApp('/my-work');

    // Act
    const scope = await screen.findByRole('combobox', { name: '範圍' });
    const layout = await screen.findByRole('combobox', { name: '版面' });

    // Assert
    const toolbar = scope.closest('[data-scrollable]');
    expect(toolbar).not.toBeNull();
    expect(toolbar).toContainElement(layout);
    expect(scope.compareDocumentPosition(layout) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(scope.closest('header')).toBeNull();
  });

  it('寬螢幕維持連結的分頁，不出現範圍的下拉', async () => {
    // Arrange
    arrange();
    renderApp('/my-work');

    // Act
    const nav = await findToolbar();

    // Assert
    expect(nav.getByRole('link', { name: '我建立的' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: '範圍' })).not.toBeInTheDocument();
  });

  it('窄螢幕的清單看我建立的：第二行有負責人，沒有負責人的寫「沒有負責人」', async () => {
    // Arrange
    stubCompactScreen();
    arrange();

    // Act
    renderApp('/my-work?mine=created');

    // Assert
    const assigned = await screen.findByRole('link', { name: /我開的稿/ });
    expect(assigned).toHaveTextContent('範例夥伴');
    expect(screen.getByRole('link', { name: /還沒指派的稿/ })).toHaveTextContent('沒有負責人');
  });

  it('看我負責的時窄螢幕的清單不寫負責人（每一筆都是自己）', async () => {
    // Arrange
    stubCompactScreen();
    arrange();

    // Act
    renderApp('/my-work');

    // Assert
    const link = await screen.findByRole('link', { name: /我負責的稿/ });
    expect(link).not.toHaveTextContent('負責人');
  });

  it('看板看我建立的：請求送 createdBy=me、不送 assignee，卡片寫出負責人（沒有負責人的也寫）', async () => {
    // Arrange
    const requests = arrange();

    // Act
    renderApp('/my-work?view=board&mine=created');

    // Assert
    const card = await screen.findByRole('article', { name: '我開的稿' });
    expect(within(card).getByText('範例夥伴')).toBeInTheDocument();
    expect(within(screen.getByRole('article', { name: '還沒指派的稿' })).getByText('沒有負責人')).toBeInTheDocument();
    const last = requests.at(-1)?.searchParams;
    expect(last?.get('createdBy')).toBe('me');
    expect(last?.has('assignee')).toBe(false);
  });

  it('看板看我負責的：卡片不寫負責人', async () => {
    // Arrange
    arrange();

    // Act
    renderApp('/my-work?view=board');

    // Assert
    const card = await screen.findByRole('article', { name: '我負責的稿' });
    expect(within(card).queryByText('負責人')).not.toBeInTheDocument();
    expect(within(card).queryByText('沒有負責人')).not.toBeInTheDocument();
  });

  it('行事曆看我建立的：請求送 createdBy=me、不送 assignee，項目的名稱帶負責人', async () => {
    // Arrange
    const dated = smkItem({ number: 5, title: '有日期的稿', assignee: partner, createdBy: mine, dueDate: daysFromToday(1) });
    signedInAs(exampleOwner, { details: [smkDetail()], items: [dated] });
    const requests = serveCrossProjectItems([dated], exampleOwner.personId);

    // Act
    renderApp('/my-work?view=calendar&mine=created');

    // Assert
    const link = await screen.findByRole('link', { name: /有日期的稿/ });
    expect(link).toHaveAccessibleName(expect.stringContaining('負責人 範例夥伴'));
    expect(link).toHaveAttribute('title', expect.stringContaining('負責人 範例夥伴'));
    const last = requests.at(-1)?.searchParams;
    expect(last?.get('createdBy')).toBe('me');
    expect(last?.has('assignee')).toBe(false);
  });
});
