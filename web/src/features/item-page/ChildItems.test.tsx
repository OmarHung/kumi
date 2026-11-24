import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Permission } from '../../api/blueprint';
import type { Item, ItemDetail, ProjectDetail } from '../../api/types';
import { created, serveActivity } from '../../test/activity';
import { failure, ok, signedInAs } from '../../test/api';
import { agencyBlueprint } from '../../test/blueprints';
import { agyDetail, agyItem, exampleMember, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, serveItems, servePatch, type RecordedRequest } from '../../test/items';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { server } from '../../test/server';

const requirement = detailOf(agyItem({ number: 1, title: '官網需求' }));
const childA = agyItem({
  number: 5,
  type: 'task',
  title: '首頁切版',
  state: 'doing',
  stateCategory: 'in_progress',
  parentKey: 'AGY-1',
  assignee: { personId: exampleMember.personId, displayName: exampleMember.displayName },
});
const childB = agyItem({ number: 6, type: 'task', title: '表單串接', state: 'done', stateCategory: 'done', parentKey: 'AGY-1' });

interface OpenOptions {
  readonly item?: ItemDetail;
  readonly detail?: ProjectDetail;
  readonly pages?: readonly (readonly Item[])[];
}

async function openChildren({ item = requirement, detail = agyDetail(), pages = [[childA, childB]] }: OpenOptions = {}) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItem(item);
  serveActivity(item.key, [created()]);
  const listRequests = serveItems(detail.project.key, ...pages);
  const view = renderApp(`/projects/${detail.project.key}/items/${item.key}`);
  const section = await screen.findByRole('region', { name: '下層項目' });
  return { ...view, section, listRequests };
}

function serveCreate(projectKey: string, result: ItemDetail): RecordedRequest[] {
  const requests: RecordedRequest[] = [];
  server.use(
    http.post(`/v1/projects/${projectKey}/items`, async ({ request }) => {
      requests.push({ url: new URL(request.url), body: await request.json() });
      return ok(result, 201);
    }),
  );
  return requests;
}

describe('下層項目', () => {
  it('列出直接下層的編號、標題、狀態與負責人，各自連到項目頁', async () => {
    // Arrange & Act
    const { section, listRequests } = await openChildren();

    // Assert
    const list = await within(section).findByRole('list', { name: '下層項目' });
    const [first, second] = within(list).getAllByRole('listitem');
    expect(within(first as HTMLElement).getByRole('link', { name: 'AGY-5 首頁切版 狀態：進行中 負責人：範例成員' })).toHaveAttribute(
      'href',
      '/projects/AGY/items/AGY-5',
    );
    expect(first).toHaveTextContent('進行中');
    expect(first).toHaveTextContent('範例成員');
    expect(second).toHaveTextContent('完成');
    expect(second).toHaveTextContent('沒有負責人');
    expect(within(section).getByText('1 / 2 完成')).toBeInTheDocument();
    expect(listRequests[0]?.searchParams.get('parent')).toBe('AGY-1');
  });

  // F 輪（U1）：寬螢幕點下層項目改成在旁邊開項目面板（原本換到它的項目頁），上層的項目頁留著。
  it('點下層項目在旁邊開它的面板，上層的項目頁留著', async () => {
    // Arrange
    const { user, router, section } = await openChildren();
    serveItem(detailOf(childA));
    serveActivity(childA.key, [created()]);
    const link = await within(section).findByRole('link', { name: /^AGY-5 首頁切版 / });

    // Act
    await user.click(link);

    // Assert
    const panel = await screen.findByRole('complementary', { name: '項目 AGY-5' });
    expect(currentPath(router)).toBe('/projects/AGY/items/AGY-1?item=AGY-5');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('官網需求');
    expect(link).toHaveAttribute('href', '/projects/AGY/items/AGY-5');
    expect(link).toHaveAttribute('aria-current', 'true');
    const fields = await within(panel).findByRole('region', { name: '欄位' });
    expect(within(fields).getByRole('button', { name: '上層項目：AGY-1' })).toBeInTheDocument();
    expect(within(fields).getByRole('link', { name: '開啟上層項目 AGY-1' })).toHaveAttribute('href', '/projects/AGY/items/AGY-1');
  });

  // F 輪（U1）：下層項目開在面板裡（原本是它的項目頁），欄位用 within(面板) 找；上一頁關上面板。
  it('在下層項目的面板拿掉上層，關上面板時清單裡就沒有它，不必重抓', async () => {
    // Arrange
    const { user, router, section, listRequests } = await openChildren();
    serveItem(detailOf(childA));
    serveActivity(childA.key, [created()]);
    servePatch(childA);
    await user.click(await within(section).findByRole('link', { name: /^AGY-5 首頁切版 / }));
    const panel = await screen.findByRole('complementary', { name: '項目 AGY-5' });
    const fields = await within(panel).findByRole('region', { name: '欄位' });

    // Act
    await user.click(within(fields).getByRole('button', { name: '上層項目：AGY-1' }));
    await chooseOption(user, within(fields).getByRole('combobox', { name: '上層項目' }), '沒有上層項目');
    await within(fields).findByRole('button', { name: '上層項目：未填' });
    await router.navigate(-1);

    // Assert
    const again = await screen.findByRole('region', { name: '下層項目' });
    expect(within(again).getByRole('link', { name: /^AGY-6 表單串接 / })).toBeInTheDocument();
    expect(within(again).queryByRole('link', { name: /^AGY-5 首頁切版 / })).not.toBeInTheDocument();
    expect(listRequests.filter((url) => url.searchParams.get('parent') === 'AGY-1')).toHaveLength(1);
  });

  it('沒有下層時說明', async () => {
    // Arrange & Act
    const { section } = await openChildren({ pages: [[]] });

    // Assert
    expect(await within(section).findByText('還沒有下層項目。')).toBeInTheDocument();
  });

  it('沒有類型能放在這個類型底下時，不顯示下層項目', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    const task = detailOf(smkItem({ number: 2 }));
    serveItem(task);
    serveActivity(task.key, [created()]);

    // Act
    renderApp('/projects/SMK/items/SMK-2');

    // Assert
    await screen.findByRole('region', { name: '欄位' });
    expect(screen.queryByRole('region', { name: '下層項目' })).not.toBeInTheDocument();
  });

  it('下層很多時可以載入更多', async () => {
    // Arrange
    const { user, section } = await openChildren({ pages: [[childA], [childB]] });
    await within(section).findByRole('link', { name: /^AGY-5 首頁切版 / });

    // Act
    await user.click(within(section).getByRole('button', { name: '載入更多' }));

    // Assert
    expect(await within(section).findByRole('link', { name: /^AGY-6 表單串接 / })).toBeInTheDocument();
  });

  it('讀不到下層時顯示訊息並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [agyDetail()] });
    serveItem(requirement);
    serveActivity(requirement.key, [created()]);
    serveItems('AGY', [childA]);
    server.use(http.get('/v1/projects/AGY/items', () => failure(500, 'server.error', 'boom'), { once: true }));
    const { user } = renderApp('/projects/AGY/items/AGY-1');
    const section = await screen.findByRole('region', { name: '下層項目' });

    // Act
    const alert = await within(section).findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(await within(section).findByRole('link', { name: /^AGY-5 首頁切版 / })).toBeInTheDocument();
  });
});

describe('新增下層項目', () => {
  it('只有一種類型能放在底下時只要填標題；新增後接在清單最後並說明', async () => {
    // Arrange
    const { user, section } = await openChildren();
    const createdChild = detailOf(agyItem({ number: 9, type: 'task', title: '上線檢查', state: 'todo', parentKey: 'AGY-1' }));
    const requests = serveCreate('AGY', createdChild);
    const form = within(section).getByRole('form', { name: '新增下層項目' });
    expect(within(form).queryByRole('combobox', { name: '類型' })).not.toBeInTheDocument();

    // Act
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '  上線檢查  {Enter}');

    // Assert
    expect(await within(form).findByRole('status')).toHaveTextContent('已新增 AGY-9。');
    expect(requests.map((request) => request.body)).toEqual([{ type: 'task', title: '上線檢查', parentKey: 'AGY-1' }]);
    const list = within(section).getByRole('list', { name: '下層項目' });
    expect(list.lastElementChild).toHaveTextContent('AGY-9 上線檢查');
    expect(within(form).getByRole('textbox', { name: '標題' })).toHaveValue('');
    expect(within(form).getByRole('textbox', { name: '標題' })).toHaveFocus();
  });

  it('新增之後再「載入更多」，後面那頁又帶到剛新增的項目時只出現一次，排在伺服器給的位置', async () => {
    // Arrange
    const { user, section } = await openChildren({ pages: [[childA], [childB]] });
    await within(section).findByRole('link', { name: /^AGY-5 首頁切版 / });
    const createdChild = detailOf(agyItem({ number: 9, type: 'task', title: '上線檢查', parentKey: 'AGY-1' }));
    serveCreate('AGY', createdChild);
    await user.type(within(section).getByRole('textbox', { name: '標題' }), '上線檢查{Enter}');
    await within(section).findByText('已新增 AGY-9。');
    serveItems('AGY', [childA], [childB, createdChild]);

    // Act
    await user.click(within(section).getByRole('button', { name: '載入更多' }));

    // Assert
    await within(section).findByRole('link', { name: /^AGY-6 表單串接 / });
    const list = within(section).getByRole('list', { name: '下層項目' });
    expect(within(list).getAllByRole('link').map((link) => link.textContent)).toEqual([
      expect.stringMatching(/^AGY-5 首頁切版/),
      expect.stringMatching(/^AGY-6 表單串接/),
      expect.stringMatching(/^AGY-9 上線檢查/),
    ]);
    expect(within(section).getByText('1 / 3 完成')).toBeInTheDocument();
  });

  it('有好幾種類型能放在底下時先選類型', async () => {
    // Arrange
    const blueprint = {
      ...agencyBlueprint,
      itemTypes: [
        ...agencyBlueprint.itemTypes,
        { key: 'defect', name: '缺陷', workflow: 'task_flow', fields: [], parents: ['requirement'] },
      ],
    };
    const { user, section } = await openChildren({ detail: agyDetail({ blueprint }) });
    const requests = serveCreate('AGY', detailOf(agyItem({ number: 9, type: 'defect', title: '按鈕跑版', parentKey: 'AGY-1' })));
    const form = within(section).getByRole('form', { name: '新增下層項目' });

    // Act
    await chooseOption(user, within(form).getByRole('combobox', { name: '類型' }), '缺陷');
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '按鈕跑版');
    await user.click(within(form).getByRole('button', { name: '新增' }));

    // Assert
    expect(await within(form).findByRole('status')).toHaveTextContent('已新增 AGY-9。');
    expect(requests.map((request) => request.body)).toEqual([{ type: 'defect', title: '按鈕跑版', parentKey: 'AGY-1' }]);
  });

  it('標題空白時不送出並說明', async () => {
    // Arrange
    const { user, section } = await openChildren();
    const requests = serveCreate('AGY', detailOf(childA));
    const form = within(section).getByRole('form', { name: '新增下層項目' });

    // Act
    await user.click(within(form).getByRole('button', { name: '新增' }));

    // Assert
    expect(within(form).getByRole('textbox', { name: '標題' })).toHaveAccessibleDescription(/請輸入標題/);
    expect(requests).toHaveLength(0);
  });

  it('後端擋下時說明原因', async () => {
    // Arrange
    const { user, section } = await openChildren();
    server.use(
      http.post('/v1/projects/AGY/items', () =>
        failure(422, 'item.parent_not_allowed', '這個類型不能放在「需求」底下。', [
          { code: 'item.parent_not_allowed', path: 'parent', message: '這個類型不能放在「需求」底下。' },
        ]),
      ),
    );
    const form = within(section).getByRole('form', { name: '新增下層項目' });

    // Act
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '上線檢查{Enter}');

    // Assert
    expect(await within(form).findByRole('alert')).toHaveTextContent('這個類型不能放在「需求」底下。');
    expect(within(form).getByRole('textbox', { name: '標題' })).toHaveValue('上線檢查');
  });

  it('沒有 item.create 的人沒有新增的地方', async () => {
    // Arrange
    const permissions: readonly Permission[] = ['item.read', 'item.update', 'blueprint.read'];

    // Act
    const { section } = await openChildren({ detail: agyDetail({ myPermissions: permissions }) });

    // Assert
    await within(section).findByRole('list', { name: '下層項目' });
    expect(within(section).queryByRole('form', { name: '新增下層項目' })).not.toBeInTheDocument();
  });
});
