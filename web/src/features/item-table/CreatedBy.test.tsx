import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { exampleMember, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';

const OTHER = { personId: exampleMember.personId, displayName: exampleMember.displayName };
const ITEMS = [
  smkItem({ number: 1, title: '自己開的', assignee: null }),
  smkItem({ number: 2, title: '同事開的', assignee: null, createdBy: OTHER }),
];

async function openSmk(path = '/projects/SMK') {
  signedInAs(exampleOwner, { details: [smkDetail({ members: [{ ...exampleOwner, role: 'admin' }, { ...OTHER, role: 'member' }] })] });
  const server = serveItemsByQuery('SMK', ITEMS);
  const view = renderApp(path);
  await screen.findByRole('table', { name: '項目' });
  return { ...view, server };
}

// 量不到寬度（jsdom）時全部條件都排在列上；建立者是列上的條件（放不下時才先收進「更多條件」，見 MoreConditions.test.tsx）。
async function findBar() {
  return within(await screen.findByRole('group', { name: '篩選項目' }));
}

describe('表格的建立者欄', () => {
  it('預設不顯示，從「顯示的欄位」打開，格子是建立的人（唯讀）', async () => {
    // Arrange
    const { user } = await openSmk();
    const table = screen.getByRole('table', { name: '項目' });
    expect(within(table).queryByRole('columnheader', { name: '建立者' })).not.toBeInTheDocument();

    // Act
    await user.click(await screen.findByRole('button', { name: '顯示的欄位' }));
    const group = await screen.findByRole('group', { name: '顯示的欄位' });
    expect(within(group).getByRole('checkbox', { name: '建立者' })).not.toBeChecked();
    await user.click(within(group).getByRole('checkbox', { name: '建立者' }));

    // Assert
    expect(await within(table).findByRole('columnheader', { name: '建立者' })).toBeInTheDocument();
    const row = within(table).getByRole('row', { name: /同事開的/ });
    expect(within(row).getByText(exampleMember.displayName)).toBeInTheDocument();
    expect(within(row).queryByRole('button', { name: /建立者/ })).not.toBeInTheDocument();
  });

  it('預設就不顯示的建立者不算進「隱藏了 N 欄」', async () => {
    // Arrange & Act
    await openSmk();

    // Assert
    expect(screen.queryByText(/隱藏了/)).not.toBeInTheDocument();
  });
});

describe('專案清單的建立者篩選', () => {
  it('選項和負責人一樣：所有人、我、其他成員；選「我」寫進網址 createdBy=me 並送給 API', async () => {
    // Arrange
    const { user, server, router } = await openSmk();
    const bar = await findBar();
    const field = bar.getByRole('combobox', { name: '建立者' });

    // Act
    const options = await openOptions(user, field);
    expect(options.map((option) => option.dataset.value)).toEqual(['', 'me', exampleMember.personId]);
    await chooseOption(user, field, '我');

    // Assert
    await waitFor(() => {
      expect(server.requests.at(-1)?.searchParams.get('createdBy')).toBe('me');
    });
    expect(currentPath(router)).toBe('/projects/SMK?createdBy=me');
    expect(await screen.findByText('自己開的')).toBeInTheDocument();
    expect(screen.queryByText('同事開的')).not.toBeInTheDocument();
  });

  it('指定的人用人員編號；可以和負責人一起用', async () => {
    // Arrange
    const { user, server } = await openSmk('/projects/SMK?assignee=unassigned');
    const bar = await findBar();

    // Act
    await chooseOption(user, bar.getByRole('combobox', { name: '建立者' }), exampleMember.displayName);

    // Assert
    await waitFor(() => {
      expect(server.requests.at(-1)?.searchParams.get('createdBy')).toBe(exampleMember.personId);
    });
    expect(server.requests.at(-1)?.searchParams.get('assignee')).toBe('unassigned');
    expect(await screen.findByText('同事開的')).toBeInTheDocument();
  });

  it('網址帶 createdBy 時就是篩好的：列上的建立者是「我」，清除條件拿掉它', async () => {
    // Arrange
    const { user, router } = await openSmk('/projects/SMK?createdBy=me');
    const bar = await findBar();
    expect(bar.getByRole('combobox', { name: '建立者' })).toHaveTextContent('我');

    // Act
    await user.click(bar.getByRole('button', { name: '清除條件' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK');
    });
  });
});

describe('看板、行事曆與時間軸也能用建立者篩選', () => {
  it.each([
    ['board', '看板'],
    ['calendar', '行事曆'],
    ['timeline', '時間軸'],
  ] as const)('%s：網址的 createdBy 送給 API，篩選列有建立者', async (view, label) => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const server = serveItemsByQuery('SMK', ITEMS);

    // Act
    renderApp(`/projects/SMK?view=${view}&createdBy=me`);

    // Assert
    await waitFor(() => {
      expect(server.requests.some((url) => url.searchParams.get('createdBy') === 'me')).toBe(true);
    });
    const bar = within(await screen.findByRole('group', { name: `篩選${label}` }));
    expect(bar.getByRole('combobox', { name: '建立者' })).toHaveTextContent('我');
  });
});
