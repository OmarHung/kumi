import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { failure, ok, signedInAs } from '../../test/api';
import { agyDetail, agyItem, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { archivedDetail, serveProjectLifecycle } from '../../test/projects';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

const ARCHIVED_TEXT = '這個專案已封存：只能看，不能新增或修改。';

function openAgy(detail = agyDetail(), options: Parameters<typeof serveProjectLifecycle>[1] = {}) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItems('AGY', [agyItem({ number: 1, title: '官網需求' })]);
  const backend = serveProjectLifecycle(detail, options);
  return { backend, view: renderApp('/projects/AGY') };
}

describe('封存專案', () => {
  it('從「更多」選「封存專案」直接做、不問確認；頁面上方出現封存的提示，快速新增不見了', async () => {
    // Arrange
    const { backend, view } = openAgy();
    const { user } = view;
    await screen.findByRole('form', { name: '新增項目' });
    expect(screen.queryByText(ARCHIVED_TEXT)).not.toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '專案的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '封存專案' }));

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(backend.archives()).toBe(1);
    await waitFor(() => { expect(screen.queryByRole('form', { name: '新增項目' })).not.toBeInTheDocument(); });
    await user.click(screen.getByRole('button', { name: '專案的更多動作' }));
    const menu = await screen.findByRole('menu', { name: '專案的更多動作' });
    expect(within(menu).getByRole('menuitem', { name: '取消封存' })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: '封存專案' })).not.toBeInTheDocument();
  });

  it('提示旁的「取消封存」讓專案恢復：提示不見、快速新增回來', async () => {
    // Arrange
    const { backend, view } = openAgy(archivedDetail(agyDetail()), { restoredPermissions: agyDetail().myPermissions });
    const { user } = view;
    await screen.findByText(ARCHIVED_TEXT);

    // Act
    await user.click(screen.getByRole('button', { name: '取消封存' }));

    // Assert：按的按鈕跟著提示不見，焦點移到頁面的標題（DESIGN 8.3「焦點」；2026-11-19 實測掉到 body）。
    expect(await screen.findByRole('form', { name: '新增項目' })).toBeInTheDocument();
    expect(screen.queryByText(ARCHIVED_TEXT)).not.toBeInTheDocument();
    expect(backend.unarchives()).toBe(1);
    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 1 })).toHaveFocus();
    });
  });

  it('從「更多」選「取消封存」也一樣', async () => {
    // Arrange
    const { backend, view } = openAgy(archivedDetail(agyDetail()), { restoredPermissions: agyDetail().myPermissions });
    const { user } = view;
    await screen.findByText(ARCHIVED_TEXT);

    // Act
    await user.click(screen.getByRole('button', { name: '專案的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '取消封存' }));

    // Assert
    await waitFor(() => { expect(screen.queryByText(ARCHIVED_TEXT)).not.toBeInTheDocument(); });
    expect(backend.unarchives()).toBe(1);
  });

  it('沒有 project.manage 的人看到提示，但沒有「取消封存」，「更多」也是空的所以沒有按鈕', async () => {
    // Arrange
    const detail = archivedDetail(agyDetail({ myPermissions: ['item.read', 'blueprint.read'] }));

    // Act
    openAgy(detail);

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '取消封存' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '專案的更多動作' })).not.toBeInTheDocument();
  });

  it('封存失敗時說明原因，專案維持原樣', async () => {
    // Arrange
    const { view } = openAgy();
    const { user } = view;
    await screen.findByRole('form', { name: '新增項目' });
    server.use(http.post('/v1/projects/AGY/archive', () => failure(403, 'work.forbidden', '你沒有權限封存這個專案。')));

    // Act
    await user.click(screen.getByRole('button', { name: '專案的更多動作' }));
    await user.click(await screen.findByRole('menuitem', { name: '封存專案' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('你沒有權限封存這個專案。');
    expect(screen.queryByText(ARCHIVED_TEXT)).not.toBeInTheDocument();
  });
});

describe('別人剛封存了專案、這一頁還是舊的', () => {
  it('新增項目回 409 project.archived：專案重抓，頁面換成唯讀並出現封存的提示', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '既有的項目' })]);
    const { user } = renderApp('/projects/SMK');
    const form = await screen.findByRole('form', { name: '新增項目' });
    server.use(
      http.get('/v1/projects/SMK', () => ok(archivedDetail(smkDetail()))),
      http.post('/v1/projects/SMK/items', () => failure(409, 'project.archived', '專案「冒煙測試」已封存，只能看不能改。')),
    );

    // Act
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '新的任務{Enter}');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    await waitFor(() => { expect(screen.queryByRole('form', { name: '新增項目' })).not.toBeInTheDocument(); });
  });

  it('其他的錯誤代碼不會讓專案重抓', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '既有的項目' })]);
    let projectReads = 0;
    const { user } = renderApp('/projects/SMK');
    const form = await screen.findByRole('form', { name: '新增項目' });
    server.use(
      http.get('/v1/projects/SMK', () => {
        projectReads += 1;
        return ok(smkDetail());
      }),
      http.post('/v1/projects/SMK/items', () => failure(403, 'work.forbidden', '你沒有權限。')),
    );

    // Act
    await user.type(within(form).getByRole('textbox', { name: '標題' }), '新的任務{Enter}');
    await screen.findByText('你沒有權限。');

    // Assert
    expect(projectReads).toBe(0);
  });
});
