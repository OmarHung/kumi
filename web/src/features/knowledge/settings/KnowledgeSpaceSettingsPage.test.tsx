import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import type { KnowledgeSpace, KnowledgeSpaceSettings } from '../../../api/knowledgeTypes';
import { failure, ok, signedInAs } from '../../../test/api';
import { exampleOwner } from '../../../test/fixtures';
import { knowledgeSpaceOf, serveKnowledge } from '../../../test/knowledge';
import { currentPath, renderApp } from '../../../test/renderApp';
import { server } from '../../../test/server';

function openSettings(space: KnowledgeSpace = knowledgeSpaceOf()) {
  signedInAs(exampleOwner);
  const backend = serveKnowledge({ spaces: [space] });
  return { backend, ...renderApp(`/knowledge/${space.key}/settings`) };
}

/** 伺服器改了空間：之後重抓的人讀到的是新的（儲存、封存之後畫面會重抓整個空間）。 */
function changeSpace(backend: ReturnType<typeof serveKnowledge>, next: KnowledgeSpace): KnowledgeSpace {
  backend.spaces.splice(0, 1, next);
  return next;
}

function serveUpdate(base: KnowledgeSpace, backend: ReturnType<typeof serveKnowledge>) {
  const sent = vi.fn<(settings: KnowledgeSpaceSettings) => void>();
  server.use(
    http.put('/v1/knowledge/spaces/:key', async ({ request }) => {
      const settings = (await request.json()) as KnowledgeSpaceSettings;
      sent(settings);
      return ok(changeSpace(backend, { ...base, ...settings }));
    }),
  );
  return sent;
}

describe('空間設定', () => {
  it('代號與綁的專案是唯讀的文字，名稱與簡介是欄位', async () => {
    // Arrange、Act
    openSettings(knowledgeSpaceOf({ project: { key: 'WEB', name: '官網改版', isArchived: false } }));

    // Assert
    expect(await screen.findByRole('textbox', { name: '名稱' })).toHaveValue('團隊文件');
    expect(screen.getByRole('textbox', { name: '簡介' })).toHaveValue('操作手冊、會議記錄與決策紀錄。');
    expect(screen.getByText('DOCS')).toBeInTheDocument();
    expect(screen.getByText('專案空間：WEB 官網改版')).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: '代號' })).not.toBeInTheDocument();
  });

  it('沒有修改時「儲存」停用；改了名稱存起來，送出整理過的內容並說已儲存', async () => {
    // Arrange
    const space = knowledgeSpaceOf();
    const { user, backend } = openSettings(space);
    const sent = serveUpdate(space, backend);
    const name = await screen.findByRole('textbox', { name: '名稱' });
    const save = screen.getByRole('button', { name: '儲存' });
    const wasDisabled = (save as HTMLButtonElement).disabled;

    // Act
    await user.clear(name);
    await user.type(name, '  新名稱 ');
    await user.clear(screen.getByRole('textbox', { name: '簡介' }));
    await user.click(save);

    // Assert
    expect(wasDisabled).toBe(true);
    await waitFor(() => { expect(sent).toHaveBeenCalledWith({ name: '新名稱', description: null }); });
    expect(await screen.findByText('已儲存。')).toBeInTheDocument();
    expect(name).toHaveValue('新名稱');
    expect(screen.getByRole('button', { name: '儲存' })).toBeDisabled();
  });

  it('名稱空白時不送出，錯誤在欄位上', async () => {
    // Arrange
    const space = knowledgeSpaceOf();
    const { user, backend } = openSettings(space);
    const sent = serveUpdate(space, backend);

    // Act
    await user.clear(await screen.findByRole('textbox', { name: '名稱' }));
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('請輸入空間名稱。')).toBeInTheDocument();
    expect(sent).not.toHaveBeenCalled();
  });

  it('有沒存的修改時離開要確認', async () => {
    // Arrange
    const { user, router } = openSettings();
    await user.type(await screen.findByRole('textbox', { name: '名稱' }), '改');

    // Act
    await user.click(screen.getByRole('link', { name: '團隊文件' }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '還有沒儲存的修改' });
    expect(currentPath(router)).toBe('/knowledge/DOCS/settings');
    await user.click(within(dialog).getByRole('button', { name: '捨棄修改並離開' }));
    await waitFor(() => { expect(currentPath(router)).toBe('/knowledge/DOCS'); });
  });

  it('封存要先確認，說明封存後只能看；確認後換成「取消封存」', async () => {
    // Arrange
    const space = knowledgeSpaceOf();
    const { user, backend } = openSettings(space);
    server.use(http.post('/v1/knowledge/spaces/DOCS/archive', () => ok(changeSpace(backend, { ...space, archivedAt: '2026-11-25T03:00:00+00:00' }))));

    // Act
    await user.click(await screen.findByRole('button', { name: '封存空間' }));
    const dialog = await screen.findByRole('dialog', { name: '封存「團隊文件」' });
    expect(dialog).toHaveTextContent('封存後只能看，不能新增或修改頁面');
    await user.click(within(dialog).getByRole('button', { name: '封存空間' }));

    // Assert
    expect(await screen.findByRole('button', { name: '取消封存' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('封存之後回到頁面：頁面資料裡的權限也已經更新（不能再編輯），不用等快取過期', async () => {
    // Arrange
    signedInAs(exampleOwner);
    const backend = serveKnowledge({ bodies: { 'DOCS/2': '內容' } });
    const archived = knowledgeSpaceOf({ archivedAt: '2026-11-25T03:00:00+00:00', myPermissions: { canEdit: false, canComment: false, canManage: true } });
    server.use(
      http.post('/v1/knowledge/spaces/DOCS/archive', () => {
        backend.spaces.splice(0, 1, archived);
        return ok(archived);
      }),
    );
    const { user } = renderApp('/knowledge/DOCS/2');
    await user.click(await screen.findByRole('button', { name: '編輯' }));
    await user.click(await screen.findByRole('button', { name: '取消' }));
    await user.click(await screen.findByRole('link', { name: '空間設定' }));
    await user.click(await screen.findByRole('button', { name: '封存空間' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '封存空間' }));
    await screen.findByRole('button', { name: '取消封存' });

    // Act
    await user.click(screen.getByRole('link', { name: '團隊文件' }));
    await user.click(await screen.findByRole('link', { name: '帳號申請' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '帳號申請' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '編輯' })).not.toBeInTheDocument();
  });

  it('封存失敗時對話框留著並說明原因', async () => {
    // Arrange
    const { user } = openSettings();
    server.use(http.post('/v1/knowledge/spaces/DOCS/archive', () => failure(403, 'knowledge.forbidden', '你沒有權限這樣做。')));

    // Act
    await user.click(await screen.findByRole('button', { name: '封存空間' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '封存空間' }));

    // Assert
    expect(await screen.findByText('你沒有權限這樣做。')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('已封存的空間：取消封存不必確認，直接換回「封存空間」', async () => {
    // Arrange
    const archived = knowledgeSpaceOf({ archivedAt: '2026-11-01T00:00:00+00:00' });
    const { user, backend } = openSettings(archived);
    server.use(http.post('/v1/knowledge/spaces/DOCS/unarchive', () => ok(changeSpace(backend, { ...archived, archivedAt: null }))));

    // Act
    await user.click(await screen.findByRole('button', { name: '取消封存' }));

    // Assert
    expect(await screen.findByRole('button', { name: '封存空間' })).toBeInTheDocument();
  });

  it('沒有 canManage 的人看到沒有權限，連回空間', async () => {
    // Arrange、Act
    openSettings(knowledgeSpaceOf({ myPermissions: { canEdit: true, canComment: true, canManage: false } }));

    // Assert
    expect(await screen.findByRole('heading', { name: '沒有權限管理這個空間' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到空間' })).toHaveAttribute('href', '/knowledge/DOCS');
    expect(screen.queryByRole('textbox', { name: '名稱' })).not.toBeInTheDocument();
  });
});
