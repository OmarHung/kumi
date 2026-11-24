import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Person } from '../../api/types';
import { COMPACT_QUERY } from '../../lib/useMediaQuery';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const owner: Person = { personId: exampleOwner.personId, displayName: exampleOwner.displayName, email: exampleOwner.email, role: 'owner', disabled: false };
const member: Person = { personId: exampleMember.personId, displayName: exampleMember.displayName, email: exampleMember.email, role: 'member', disabled: false };

/** 窄螢幕：只有窄螢幕的查詢符合。 */
function stubCompactScreen() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === COMPACT_QUERY,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

function itemOf(list: HTMLElement, name: string): HTMLElement {
  const item = within(list).getAllByRole('listitem').find((candidate) => within(candidate).queryByText(name) !== null);
  if (item === undefined) throw new Error(`找不到「${name}」那一筆`);
  return item;
}

describe('人員頁：窄螢幕', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('人員是清單而不是表格：名稱、Email 與組織角色，標出自己；第一筆是內容的開頭', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner);
    server.use(http.get('/v1/people', () => ok([owner, member])));

    // Act
    renderApp('/people');

    // Assert
    const main = await screen.findByRole('main');
    const list = await within(main).findByRole('list', { name: '人員' });
    expect(within(main).queryByRole('table')).not.toBeInTheDocument();
    const memberItem = itemOf(list, '範例成員');
    expect(within(memberItem).getByText('member@example.com')).toBeInTheDocument();
    expect(within(memberItem).getByText('成員')).toBeInTheDocument();
    const ownerItem = itemOf(list, '範例使用者');
    expect(within(ownerItem).getByText('擁有者')).toBeInTheDocument();
    expect(within(ownerItem).getByText('你')).toBeInTheDocument();
    expect(within(list).getAllByRole('listitem')[0]).toHaveAttribute('data-content-start');
  });
});

describe('人員頁：窄螢幕的動作', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('停用的人標「已停用」；擁有者在別人那一筆有動作的選單，自己那一筆沒有', async () => {
    // Arrange
    stubCompactScreen();
    signedInAs(exampleOwner);
    server.use(http.get('/v1/people', () => ok([owner, { ...member, disabled: true }])));
    const { user } = renderApp('/people');
    const list = await screen.findByRole('list', { name: '人員' });

    // Act
    await user.click(within(itemOf(list, '範例成員')).getByRole('button', { name: '範例成員 的動作' }));

    // Assert
    expect(within(itemOf(list, '範例成員')).getByText('已停用')).toBeInTheDocument();
    expect(within(itemOf(list, '範例使用者')).queryByRole('button')).not.toBeInTheDocument();
    const menu = await screen.findByRole('menu', { name: '範例成員 的動作' });
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['變更角色…', '重設密碼…', '重新啟用帳號']);
  });
});

describe('人員頁：沒有權限', () => {
  it('訪客拿不到清單時，除了說明，還給回到我的工作的路', async () => {
    // Arrange
    signedInAs({ ...exampleMember, role: 'guest' });
    server.use(http.get('/v1/people', () => failure(403, 'person.forbidden', '你沒有權限看組織的人員。')));

    // Act
    renderApp('/people');

    // Assert
    const main = await screen.findByRole('main');
    expect(await within(main).findByText('沒有權限')).toBeInTheDocument();
    expect(within(main).getByRole('link', { name: '回到我的工作' })).toHaveAttribute('href', '/my-work');
  });
});
