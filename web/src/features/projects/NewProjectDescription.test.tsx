import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import type { Project } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner, webProject } from '../../test/fixtures';
import { orgTemplate, serveDescriptionTemplates, webProjectTemplate } from '../../test/projectDescription';
import { renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { server } from '../../test/server';

const created: Project = { ...webProject, key: 'SHOP', name: '購物車改版' };

async function openDialog() {
  const view = renderApp('/projects');
  await view.user.click(await screen.findByRole('button', { name: '新增專案' }));
  const dialog = await screen.findByRole('dialog', { name: '新增專案' });
  await waitFor(() => {
    expect(within(dialog).getByRole('combobox', { name: '模板' })).toHaveTextContent('接案專案');
  });
  return { ...view, dialog };
}

function serveCreate() {
  const received = vi.fn<(body: unknown) => void>();
  server.use(
    http.post('/v1/projects', async ({ request }) => {
      received(await request.json());
      return ok(created, 201);
    }),
  );
  return received;
}

async function fillAndCreate(user: Awaited<ReturnType<typeof openDialog>>['user'], dialog: HTMLElement) {
  await user.type(within(dialog).getByLabelText('代號'), 'shop');
  await user.type(within(dialog).getByLabelText('名稱'), '購物車改版');
  await user.click(within(dialog).getByRole('button', { name: '建立' }));
}

describe('新增專案：專案說明', () => {
  it('下拉預先選預設的那一份，選項是說明格式加「不套用」；建立時送出那一份的內容', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    serveDescriptionTemplates([orgTemplate({ name: '維運案子', isDefault: true, body: '## 主機\n' })]);
    const received = serveCreate();
    const { user, dialog } = await openDialog();
    const select = within(dialog).getByRole('combobox', { name: '專案說明' });

    // Act
    await waitFor(() => {
      expect(select).toHaveTextContent('維運案子');
    });
    const labels = (await openOptions(user, select)).map((option) => option.textContent);
    await user.keyboard('{Escape}');
    await fillAndCreate(user, dialog);

    // Assert
    expect(labels).toEqual(['維運案子', '網站案子', '不套用']);
    await waitFor(() => {
      expect(received).toHaveBeenCalledWith(expect.objectContaining({ key: 'SHOP', description: '## 主機\n' }));
    });
  });

  it('換成別的格式，送出別的格式的內容', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    serveDescriptionTemplates([]);
    const received = serveCreate();
    const { user, dialog } = await openDialog();
    const select = within(dialog).getByRole('combobox', { name: '專案說明' });
    await waitFor(() => {
      expect(select).toHaveTextContent('網站案子');
    });

    // Act
    await chooseOption(user, select, '不套用');
    await chooseOption(user, select, '網站案子');
    await fillAndCreate(user, dialog);

    // Assert
    await waitFor(() => {
      expect(received).toHaveBeenCalledWith(expect.objectContaining({ description: webProjectTemplate.body }));
    });
  });

  it('選「不套用」：請求裡沒有 description', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    serveDescriptionTemplates([]);
    const received = serveCreate();
    const { user, dialog } = await openDialog();
    const select = within(dialog).getByRole('combobox', { name: '專案說明' });
    await waitFor(() => {
      expect(select).toHaveTextContent('網站案子');
    });

    // Act
    await chooseOption(user, select, '不套用');
    await fillAndCreate(user, dialog);

    // Assert
    await waitFor(() => {
      expect(received).toHaveBeenCalled();
    });
    expect(received.mock.calls.at(0)?.at(0)).not.toHaveProperty('description');
  });

  it('格式讀不到：下拉只有「不套用」、寫一行「說明格式讀不到」，照樣能開專案', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    server.use(http.get('/v1/description-templates', () => failure(500, 'server.error', '伺服器出錯了。')));
    const received = serveCreate();
    const { user, dialog } = await openDialog();

    // Act
    const select = within(dialog).getByRole('combobox', { name: '專案說明' });
    await waitFor(() => {
      expect(select).toHaveTextContent('不套用');
    });
    const labels = (await openOptions(user, select)).map((option) => option.textContent);
    await user.keyboard('{Escape}');
    const hintShown = within(dialog).queryByText('說明格式讀不到') !== null;
    await fillAndCreate(user, dialog);

    // Assert
    expect(labels).toEqual(['不套用']);
    expect(hintShown).toBe(true);
    await waitFor(() => {
      expect(received).toHaveBeenCalled();
    });
    expect(received.mock.calls.at(0)?.at(0)).not.toHaveProperty('description');
  });

  it('說明有私鑰（422，路徑 description）：訊息放在「專案說明」欄位下，對話框留著', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    serveDescriptionTemplates([]);
    server.use(
      http.post('/v1/projects', () =>
        failure(422, 'project.description_secret', '說明裡有私鑰。', [{ code: 'project.description_secret', message: '說明裡有私鑰。', path: 'description' }]),
      ),
    );
    const { user, dialog } = await openDialog();
    await waitFor(() => {
      expect(within(dialog).getByRole('combobox', { name: '專案說明' })).toHaveTextContent('網站案子');
    });

    // Act
    await fillAndCreate(user, dialog);

    // Assert
    await waitFor(() => {
      expect(within(dialog).getByRole('combobox', { name: '專案說明' })).toHaveAccessibleDescription(/說明裡有私鑰。/);
    });
  });
});
