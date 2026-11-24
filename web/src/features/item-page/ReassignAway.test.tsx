import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { created, serveActivity } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleMember, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItems } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

const mine = smkItem({ number: 7, title: '首頁設計稿', assignee: { personId: exampleMember.personId, displayName: exampleMember.displayName } });
const NOTICE = '已經改給「範例使用者」負責，你看不到這個項目了。';
const CONTRIBUTOR = ['item.read_own', 'item.create', 'item.update', 'item.transition', 'comment.create', 'blueprint.read'] as const;

/** 項目讀得到時回 200；readable() 回 false 就是 404（伺服器依「負責或建立」算出看不到）。 */
function serveSetup(permissions: readonly (typeof CONTRIBUTOR)[number][] | 'all', readable: () => boolean) {
  const detail = permissions === 'all' ? smkDetail() : smkDetail({ myPermissions: permissions });
  signedInAs(exampleMember, { details: [detail] });
  serveItems('SMK', [mine]);
  serveActivity(mine.key, [created()]);
  const reads: string[] = [];
  let current = detailOf(mine, '說明');
  server.use(
    http.get('/v1/items/SMK-7', () => {
      reads.push('get');
      return readable() ? ok(current) : failure(404, 'item.not_found', '找不到項目。');
    }),
  );
  server.use(
    http.patch('/v1/items/SMK-7', async ({ request }) => {
      const body = (await request.json()) as { readonly assigneeId: string };
      current = { ...current, assignee: { personId: body.assigneeId, displayName: exampleOwner.displayName } };
      return ok(current);
    }),
  );
  return { reads };
}

async function assignToOwner(user: ReturnType<typeof renderApp>['user'], scope: HTMLElement) {
  await user.click(await within(scope).findByRole('button', { name: `負責人：${exampleMember.displayName}` }));
  await chooseOption(user, await within(scope).findByRole('combobox', { name: '負責人' }), exampleOwner.displayName);
}

describe('只看自己的項目的人把項目改給別人', () => {
  it('項目頁：建立的人不是他，改完離開、回到專案的清單並說明，清單裡沒有那一筆', async () => {
    // Arrange
    let isHidden = false;
    serveSetup(CONTRIBUTOR, () => !isHidden);
    const { user, router } = renderApp('/projects/SMK/items/SMK-7');
    const fields = await screen.findByRole('region', { name: '欄位' });
    isHidden = true;
    serveItems('SMK', []);

    // Act
    await assignToOwner(user, fields);

    // Assert
    expect(await screen.findByText(NOTICE)).toHaveAttribute('role', 'status');
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK');
    });
    expect(screen.queryByText('找不到這個項目')).not.toBeInTheDocument();
  });

  it('項目頁：建立的人是他（還讀得到），留在原地、沒有提示', async () => {
    // Arrange
    const setup = serveSetup(CONTRIBUTOR, () => true);
    const { user, router } = renderApp('/projects/SMK/items/SMK-7');
    const fields = await screen.findByRole('region', { name: '欄位' });

    // Act
    await assignToOwner(user, fields);

    // Assert
    expect(await within(fields).findByRole('button', { name: `負責人：${exampleOwner.displayName}` })).toBeInTheDocument();
    await waitFor(() => {
      expect(setup.reads.length).toBeGreaterThanOrEqual(2);
    });
    expect(currentPath(router)).toBe('/projects/SMK/items/SMK-7');
    expect(screen.queryByText(NOTICE)).not.toBeInTheDocument();
  });

  it('看得到全部的人改負責人：不多發讀取項目的請求，也不離開', async () => {
    // Arrange
    const setup = serveSetup('all', () => true);
    const { user, router } = renderApp('/projects/SMK/items/SMK-7');
    const fields = await screen.findByRole('region', { name: '欄位' });
    const before = setup.reads.length;

    // Act
    await assignToOwner(user, fields);

    // Assert
    expect(await within(fields).findByRole('button', { name: `負責人：${exampleOwner.displayName}` })).toBeInTheDocument();
    expect(setup.reads).toHaveLength(before);
    expect(currentPath(router)).toBe('/projects/SMK/items/SMK-7');
  });

  it('項目面板：改完面板關上、留在專案的清單並說明', async () => {
    // Arrange
    let isHidden = false;
    serveSetup(CONTRIBUTOR, () => !isHidden);
    const { user, router } = renderApp('/projects/SMK');
    const table = await screen.findByRole('table', { name: '項目' });
    await user.click(within(table).getByRole('link', { name: '首頁設計稿' }));
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-7' });
    const fields = await within(panel).findByRole('region', { name: '欄位' });
    isHidden = true;
    serveItems('SMK', []);

    // Act
    await assignToOwner(user, fields);

    // Assert
    expect(await screen.findByText(NOTICE)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('complementary', { name: '項目 SMK-7' })).not.toBeInTheDocument();
    });
    expect(currentPath(router)).toBe('/projects/SMK');
  });
});
