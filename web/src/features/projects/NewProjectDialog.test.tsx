import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import type { Project, Template } from '../../api/types';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleOwner, webProject } from '../../test/fixtures';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption, openOptions } from '../../test/select';
import { webProjectTemplate } from '../../test/descriptionTemplateFixtures';
import { server } from '../../test/server';

// 新的專案有自己的編號：和 webProject 同一個 id 的話，側欄的清單有兩個同 key 的專案。
const created: Project = { ...webProject, id: '0190a000-0000-7000-8000-0000000000c1', key: 'SHOP', name: '購物車改版' };

/** 模板讀到了：選項只有打開清單時才在 DOM 裡，所以看按鈕上的值（預設第一個模板；讀取中是「載入中…」）。 */
async function expectTemplatesLoaded(dialog: HTMLElement) {
  await waitFor(() => {
    expect(within(dialog).getByRole('combobox', { name: '模板' })).toHaveTextContent('接案專案');
  });
}

async function openDialog() {
  const view = renderApp('/projects');
  await view.user.click(await screen.findByRole('button', { name: '新增專案' }));
  const dialog = await screen.findByRole('dialog', { name: '新增專案' });
  await expectTemplatesLoaded(dialog);
  return { ...view, dialog };
}

describe('新增專案', () => {
  it('打開時焦點在代號欄位，模板從 API 來，Esc 關閉後焦點回到按鈕', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user, dialog } = await openDialog();

    // Act
    const keyField = within(dialog).getByLabelText('代號');
    const focusedOnOpen = document.activeElement;
    const templateNames = (await openOptions(user, within(dialog).getByRole('combobox', { name: '模板' }))).map((option) => option.textContent);
    // 第一個 Esc 只關清單，第二個才關對話框。
    await user.keyboard('{Escape}');
    await user.keyboard('{Escape}');

    // Assert
    expect(focusedOnOpen).toBe(keyField);
    expect(templateNames).toEqual(['接案專案', '簡單任務']);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '新增專案' })).toHaveFocus();
  });

  it('Tab 不會跑出對話框', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user, dialog } = await openDialog();

    // Act
    // 代號、名稱、模板、範例的勾選框、專案說明、取消、建立，再一次回到代號。
    for (let stop = 0; stop < 7; stop += 1) {
      await user.tab();
    }

    // Assert
    expect(within(dialog).getByLabelText('代號')).toHaveFocus();
    await user.tab({ shift: true });
    expect(within(dialog).getByRole('button', { name: '建立' })).toHaveFocus();
  });

  it('代號輸入小寫會轉成大寫', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user, dialog } = await openDialog();

    // Act
    await user.type(within(dialog).getByLabelText('代號'), 'shop1');

    // Assert
    expect(within(dialog).getByLabelText('代號')).toHaveValue('SHOP1');
  });

  it('代號格式不對、名稱沒填時，在欄位旁邊說明而且不送出', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const posted = vi.fn();
    server.use(
      http.post('/v1/projects', () => {
        posted();
        return ok(created, 201);
      }),
    );
    const { user, dialog } = await openDialog();

    // Act
    await user.type(within(dialog).getByLabelText('代號'), '1A');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    const keyField = within(dialog).getByLabelText('代號');
    expect(keyField).toHaveAttribute('aria-invalid', 'true');
    expect(keyField).toHaveAccessibleDescription(/英文字母開頭/);
    expect(within(dialog).getByLabelText('名稱')).toHaveAccessibleDescription('請輸入專案名稱。');
    expect(keyField).toHaveFocus();
    expect(posted).not.toHaveBeenCalled();
  });

  it('建立成功後進到新的專案，清單也更新', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const received = vi.fn<(body: unknown) => void>();
    server.use(
      http.post('/v1/projects', async ({ request }) => {
        received(await request.json());
        signedInAs(exampleOwner, { projects: [webProject, created] });
        return ok(created, 201);
      }),
    );
    const { user, dialog, router } = await openDialog();

    // Act
    await user.type(within(dialog).getByLabelText('代號'), 'shop');
    await user.type(within(dialog).getByLabelText('名稱'), '購物車改版');
    await chooseOption(user, within(dialog).getByLabelText('模板'), 'simple_tasks');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '購物車改版' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/projects/SHOP');
    expect(received).toHaveBeenCalledWith({ key: 'SHOP', name: '購物車改版', template: 'simple_tasks', includeExamples: false, description: webProjectTemplate.body });
    const nav = screen.getByRole('navigation', { name: '主要導覽' });
    expect(await within(nav).findByRole('link', { name: /購物車改版/ })).toBeInTheDocument();
  });

  it('選了模板就在欄位下面列出流程與欄位，欄位的說明連到預覽，換模板跟著換', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user, dialog } = await openDialog();
    const select = within(dialog).getByRole('combobox', { name: '模板' });

    // Act
    const agencyDescription = select.getAttribute('aria-describedby') ?? '';
    const agencyPreview = document.getElementById(agencyDescription.split(' ')[1] ?? '');
    const agencyText = agencyPreview?.textContent;
    expect(select).toHaveAccessibleDescription(/從需求確認到結案的交付流程。.*需求確認、\s*報價、\s*製作、\s*結案/);
    await chooseOption(user, select, 'simple_tasks');

    // Assert
    expect(agencyText).toContain('需求、需求變更單：');
    expect(agencyText).toContain('欄位：報價金額、客戶驗收日');
    expect(select).not.toHaveAccessibleDescription('模板內容');
    expect(select).toHaveAccessibleDescription(/任務：\s*待辦、\s*進行中、\s*完成\s*；另外：不處理/);
    expect(select).toHaveAccessibleDescription(/上下層：\s*任務\s*，底下是任務/);
    expect(select).not.toHaveAccessibleDescription(/需求確認/);
  });

  it('模板的說明只出現一次', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });

    // Act
    const { dialog } = await openDialog();

    // Assert
    expect(within(dialog).getAllByText('從需求確認到結案的交付流程。')).toHaveLength(1);
  });

  it('模板有範例時才出現「附上範例項目」，預設沒勾，勾了送出帶 includeExamples', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const received = vi.fn<(body: unknown) => void>();
    server.use(
      http.post('/v1/projects', async ({ request }) => {
        received(await request.json());
        return ok(created, 201);
      }),
    );
    const { user, dialog } = await openDialog();
    const checkbox = within(dialog).getByRole('checkbox', { name: '附上範例項目（6 筆）' });
    const defaultChecked = (checkbox as HTMLInputElement).checked;
    expect(checkbox).toHaveAccessibleDescription('放幾筆示範用的項目，第一筆說明這個專案怎麼用；之後可以逐筆刪掉。');

    // Act
    await user.click(checkbox);
    await user.type(within(dialog).getByLabelText('代號'), 'shop');
    await user.type(within(dialog).getByLabelText('名稱'), '購物車改版');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(defaultChecked).toBe(false);
    await waitFor(() => {
      expect(received).toHaveBeenCalledWith({ key: 'SHOP', name: '購物車改版', template: 'agency_project', includeExamples: true, description: webProjectTemplate.body });
    });
  });

  it('換到沒有範例的模板時勾選框不見，送出的 includeExamples 是 false', async () => {
    // Arrange
    const noExamples: readonly Template[] = [
      { key: 'agency_project', name: '接案專案', description: null, workflows: [], parents: [], fields: [], exampleCount: 6 },
      { key: 'simple_tasks', name: '簡單任務', description: null, workflows: [], parents: [], fields: [], exampleCount: 0 },
    ];
    signedInAs(exampleOwner, { projects: [webProject], templates: noExamples });
    const received = vi.fn<(body: unknown) => void>();
    server.use(
      http.post('/v1/projects', async ({ request }) => {
        received(await request.json());
        return ok(created, 201);
      }),
    );
    const { user, dialog } = await openDialog();
    await user.click(within(dialog).getByRole('checkbox', { name: /附上範例項目/ }));

    // Act
    await chooseOption(user, within(dialog).getByRole('combobox', { name: '模板' }), 'simple_tasks');
    const checkboxCount = within(dialog).queryAllByRole('checkbox').length;
    await user.type(within(dialog).getByLabelText('代號'), 'shop');
    await user.type(within(dialog).getByLabelText('名稱'), '購物車改版');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(checkboxCount).toBe(0);
    expect(dialog).not.toHaveTextContent('欄位：');
    await waitFor(() => {
      expect(received).toHaveBeenCalledWith({ key: 'SHOP', name: '購物車改版', template: 'simple_tasks', includeExamples: false, description: webProjectTemplate.body });
    });
  });

  it('後端回的欄位錯誤顯示在對應的欄位旁邊', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    server.use(
      http.post('/v1/projects', () =>
        failure(409, 'project.key_taken', '專案代號已經被使用。', [
          { code: 'project.key_taken', path: 'key', message: 'WEB 已經有專案在用了。' },
        ]),
      ),
    );
    const { user, dialog } = await openDialog();

    // Act
    await user.type(within(dialog).getByLabelText('代號'), 'WEB');
    await user.type(within(dialog).getByLabelText('名稱'), '另一個官網');
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    const keyField = within(dialog).getByLabelText('代號');
    await waitFor(() => {
      expect(keyField).toHaveAccessibleDescription(/WEB 已經有專案在用了。/);
    });
    expect(keyField).toHaveAttribute('aria-invalid', 'true');
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('沒有細項的錯誤用代碼對到欄位；對不到的顯示在表單上方', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    server.use(
      http.post('/v1/projects', () => failure(422, 'project.name_too_long', '名稱太長。'), {
        once: true,
      }),
    );
    const { user, dialog } = await openDialog();
    await user.type(within(dialog).getByLabelText('代號'), 'SHOP');
    await user.type(within(dialog).getByLabelText('名稱'), '購物車改版');

    // Act
    await user.click(within(dialog).getByRole('button', { name: '建立' }));
    await waitFor(() => {
      expect(within(dialog).getByLabelText('名稱')).toHaveAccessibleDescription('名稱太長。');
    });
    server.use(http.post('/v1/projects', () => failure(403, 'work.forbidden', '你沒有權限開專案。')));
    await user.click(within(dialog).getByRole('button', { name: '建立' }));

    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('你沒有權限開專案。');
  });

  it('模板載入失敗時說明並可以重試', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    server.use(
      http.get('/v1/templates', () => failure(500, 'server.internal_error', 'boom'), { once: true }),
    );
    const { user } = renderApp('/projects');
    await user.click(await screen.findByRole('button', { name: '新增專案' }));
    const dialog = await screen.findByRole('dialog', { name: '新增專案' });

    // Act
    const alert = await within(dialog).findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    await expectTemplatesLoaded(dialog);
  });

  it('按「取消」關閉', async () => {
    // Arrange
    signedInAs(exampleOwner, { projects: [webProject] });
    const { user, dialog } = await openDialog();

    // Act
    await user.click(within(dialog).getByRole('button', { name: '取消' }));

    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
