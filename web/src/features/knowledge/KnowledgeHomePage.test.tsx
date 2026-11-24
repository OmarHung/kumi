import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import type { KnowledgeSpace, NewKnowledgeSpace } from '../../api/knowledgeTypes';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleMember, exampleOwner, projectDetail, webProject } from '../../test/fixtures';
import { knowledgeSpaceOf, serveKnowledge } from '../../test/knowledge';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { chooseOption } from '../../test/select';

const projectSpace = knowledgeSpaceOf({
  id: 'space-web',
  key: 'WEB',
  name: '官網文件',
  description: null,
  project: { key: 'WEB', name: '官網改版', isArchived: false },
});
const archivedSpace = knowledgeSpaceOf({ id: 'space-old', key: 'OLD', name: '舊手冊', description: null, archivedAt: '2026-11-01T00:00:00+00:00' });

/** 蓋掉假後端的建立：記下送出的內容，回傳建好的空間。 */
function serveCreate(created: KnowledgeSpace | null = null) {
  const sent = vi.fn<(body: NewKnowledgeSpace) => void>();
  server.use(
    http.post('/v1/knowledge/spaces', async ({ request }) => {
      const body = (await request.json()) as NewKnowledgeSpace;
      sent(body);
      return ok(created ?? knowledgeSpaceOf({ id: 'space-new', key: body.key, name: body.name }), 201);
    }),
  );
  return sent;
}

async function openDialog(user: ReturnType<typeof renderApp>['user']) {
  await user.click(await screen.findByRole('button', { name: '新增空間' }));
  return screen.findByRole('dialog', { name: '新增空間' });
}

describe('空間清單', () => {
  it('一列一個空間：名稱、代號、專案與簡介，封存的標出來，點整列進空間', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    serveKnowledge({ spaces: [knowledgeSpaceOf(), projectSpace, archivedSpace] });

    // Act
    renderApp('/knowledge');

    // Assert
    const list = await screen.findByRole('list', { name: '空間' });
    const [org, project, archived, ...rest] = within(list).getAllByRole('listitem');
    expect(rest).toHaveLength(0);
    expect(within(org as HTMLElement).getByRole('link')).toHaveAttribute('href', '/knowledge/DOCS');
    expect(org).toHaveTextContent('團隊文件');
    expect(org).toHaveTextContent('操作手冊、會議記錄與決策紀錄。');
    expect(project).toHaveTextContent('WEB 官網改版');
    expect(archived).toHaveTextContent('已封存');
    expect(screen.getByRole('heading', { level: 1, name: '知識庫' })).toBeInTheDocument();
  });

  it('沒有空間：說明知識庫是什麼，能建的人有按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveKnowledge({ spaces: [] });

    // Act
    renderApp('/knowledge');

    // Assert
    expect(await screen.findByText('還沒有空間')).toBeInTheDocument();
    expect(screen.getByText(/知識庫用空間整理頁面/)).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: '新增空間' })).toHaveLength(1);
  });

  it('一般成員在沒有能管理的專案時沒有「新增空間」', async () => {
    // Arrange
    signedInAs(exampleMember, { projects: [webProject], details: [{ ...projectDetail(webProject), myPermissions: ['item.read'] }] });
    serveKnowledge({ spaces: [] });

    // Act
    renderApp('/knowledge');

    // Assert
    expect(await screen.findByText('還沒有空間')).toBeInTheDocument();
    expect(screen.getByText(/請組織管理員或專案的管理員開一個空間/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '新增空間' })).not.toBeInTheDocument();
  });

  it('讀不到清單時說明原因，可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.get('/v1/knowledge/spaces', () => failure(500, 'server.error', '伺服器出了問題。')));

    // Act
    renderApp('/knowledge');

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('伺服器暫時無法處理，請稍後再試。');
    expect(screen.getByRole('button', { name: '重試' })).toBeInTheDocument();
  });
});

describe('新增空間', () => {
  it('組織管理員建立組織空間：名稱是英文字時代號自動帶入，建好直接進空間', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveKnowledge({ spaces: [] });
    const sent = serveCreate();
    const { user, router } = renderApp('/knowledge');
    const dialog = await openDialog(user);

    // Act
    await user.type(within(dialog).getByRole('textbox', { name: '名稱' }), 'Handbook');
    const key = within(dialog).getByRole('textbox', { name: '代號' });
    const suggested = (key as HTMLInputElement).value;
    await user.type(within(dialog).getByRole('textbox', { name: '簡介' }), '給新同事看的');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(suggested).toBe('HANDBOOK');
    await waitFor(() => { expect(currentPath(router)).toBe('/knowledge/HANDBOOK'); });
    expect(sent).toHaveBeenCalledWith({ key: 'HANDBOOK', name: 'Handbook', description: '給新同事看的', projectKey: null });
  });

  it('代號輸入時轉成大寫，改過之後不再跟著名稱', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveKnowledge({ spaces: [] });
    const sent = serveCreate();
    const { user } = renderApp('/knowledge');
    const dialog = await openDialog(user);

    // Act
    await user.type(within(dialog).getByRole('textbox', { name: '代號' }), 'hr');
    await user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '人事手冊');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    await waitFor(() => { expect(sent).toHaveBeenCalledTimes(1); });
    expect(sent).toHaveBeenCalledWith(expect.objectContaining({ key: 'HR', name: '人事手冊', description: null }));
  });

  it('有組織空間與專案空間兩種可以建時能切換；專案空間的代號帶專案代號、專案選得到', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    serveKnowledge({ spaces: [] });
    const sent = serveCreate();
    const { user } = renderApp('/knowledge');
    const dialog = await openDialog(user);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '專案空間' }));
    const project = within(dialog).getByRole('combobox', { name: '專案' });
    await user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '官網文件');
    const keyField = within(dialog).getByRole('textbox', { name: '代號' });
    expect(keyField).toHaveValue('WEB');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(project).toHaveTextContent('WEB 官網改版');
    await waitFor(() => { expect(sent).toHaveBeenCalledWith({ key: 'WEB', name: '官網文件', description: null, projectKey: 'WEB' }); });
  });

  it('已經有空間的專案、封存的專案不能選', async () => {
    // Arrange
    const archivedProject = { ...webProject, id: 'p-archived', key: 'OLD', name: '舊專案', archivedAt: '2026-11-01T00:00:00Z' };
    signedInAs(exampleOwner, { projects: [webProject, archivedProject] });
    serveKnowledge({ spaces: [projectSpace] });
    const { user } = renderApp('/knowledge');

    // Act
    const dialog = await openDialog(user);

    // Assert
    expect(within(dialog).queryByRole('button', { name: '專案空間' })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('combobox', { name: '專案' })).not.toBeInTheDocument();
  });

  it('專案的管理員（不是組織管理員）只能建專案空間，不顯示種類的選擇', async () => {
    // Arrange
    const detail = { ...projectDetail(webProject), myPermissions: ['item.read' as const, 'project.manage' as const] };
    signedInAs(exampleMember, { projects: [webProject], details: [detail] });
    serveKnowledge({ spaces: [] });
    const sent = serveCreate();
    const { user } = renderApp('/knowledge');

    // Act
    const dialog = await openDialog(user);
    await user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '官網文件');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(within(dialog).queryByRole('group', { name: '種類' })).not.toBeInTheDocument();
    await waitFor(() => { expect(sent).toHaveBeenCalledWith(expect.objectContaining({ key: 'WEB', projectKey: 'WEB' })); });
  });

  it('欄位有問題時不送出，並把焦點放到第一個有問題的欄位', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveKnowledge({ spaces: [] });
    const sent = serveCreate();
    const { user } = renderApp('/knowledge');
    const dialog = await openDialog(user);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(await within(dialog).findByText('請輸入空間名稱。')).toBeInTheDocument();
    expect(within(dialog).getByText('請輸入空間代號。')).toBeInTheDocument();
    expect(within(dialog).getByRole('textbox', { name: '名稱' })).toHaveFocus();
    expect(sent).not.toHaveBeenCalled();
  });

  it('代號已被用掉時，錯誤顯示在代號欄位，對話框留著', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveKnowledge({ spaces: [] });
    server.use(http.post('/v1/knowledge/spaces', () => failure(409, 'knowledge.space_key_taken', '這個代號已經有空間在用了。')));
    const { user } = renderApp('/knowledge');
    const dialog = await openDialog(user);

    // Act
    await user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '人事手冊');
    await user.type(within(dialog).getByRole('textbox', { name: '代號' }), 'HR');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(await within(dialog).findByText('這個代號已經有空間在用了。')).toBeInTheDocument();
    expect(within(dialog).getByRole('textbox', { name: '代號' })).toHaveFocus();
    expect(within(dialog).getByRole('textbox', { name: '代號' })).toBeInvalid();
  });

  it('伺服器說專案已經有空間時，錯誤顯示在專案欄位', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    serveKnowledge({ spaces: [] });
    server.use(http.post('/v1/knowledge/spaces', () => failure(409, 'knowledge.space_project_taken', '這個專案已經有空間了。')));
    const { user } = renderApp('/knowledge');
    const dialog = await openDialog(user);

    // Act
    await user.click(within(dialog).getByRole('button', { name: '專案空間' }));
    await user.type(within(dialog).getByRole('textbox', { name: '名稱' }), '官網文件');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(await within(dialog).findByText('這個專案已經有空間了。')).toBeInTheDocument();
  });

  it('專案可以從下拉換掉，代號跟著換', async () => {
    // Arrange
    const other = { ...webProject, id: 'p-app', key: 'APP', name: '行動應用' };
    signedInAs(exampleOwner, { projects: [webProject, other] });
    serveKnowledge({ spaces: [] });
    const { user } = renderApp('/knowledge');
    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole('button', { name: '專案空間' }));

    // Act
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '專案' }), 'APP 行動應用');

    // Assert
    expect(within(dialog).getByRole('textbox', { name: '代號' })).toHaveValue('APP');
  });
});
