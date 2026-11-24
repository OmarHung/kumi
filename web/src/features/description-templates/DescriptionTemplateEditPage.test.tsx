import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http } from 'msw';
import { failure, ok, signedInAs } from '../../test/api';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { orgTemplate, serveDescriptionTemplates } from '../../test/projectDescription';
import { currentPath, renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { editorText, findEditor, typeInEditor } from '../../test/richText';

const ops = orgTemplate({ id: 'op-1', name: '維運案子', body: '## 主機\n- 名稱：\n', isDefault: false });
const REMINDER = '不要貼密碼或金鑰，寫密碼管理器裡的項目名稱就好。';

async function openEditor(path: string, templates = [ops]) {
  signedInAs(exampleOwner);
  const served = serveDescriptionTemplates(templates);
  const view = renderApp(path);
  const name = await screen.findByRole('textbox', { name: '名稱' });
  return { ...view, served, name, body: await findEditor('內容') };
}

describe('說明格式：編輯頁的位置', () => {
  it('位置是「設定 › 說明格式」，說明格式連回清單、設定不是連結；不在設定中心的版面裡（沒有次導覽）', async () => {
    // Arrange / Act
    await openEditor('/description-templates/new');

    // Assert
    const trail = screen.getByRole('navigation', { name: '位置' });
    expect(within(trail).getByText('設定')).toBeInTheDocument();
    expect(within(trail).queryByRole('link', { name: '設定' })).not.toBeInTheDocument();
    expect(within(trail).getByRole('link', { name: '說明格式' })).toHaveAttribute('href', '/description-templates');
    expect(screen.queryByRole('navigation', { name: '設定' })).not.toBeInTheDocument();
  });
});

describe('說明格式：新增', () => {
  it('空白的編輯頁：名稱、設成預設、內容、固定的提醒、儲存與取消', async () => {
    // Arrange / Act
    const { name, body } = await openEditor('/description-templates/new');

    // Assert
    expect(screen.getByRole('heading', { level: 1, name: '新增格式' })).toBeInTheDocument();
    expect(name).toHaveValue('');
    expect(editorText(body)).toBe('');
    expect(screen.getByRole('checkbox', { name: /設成預設/ })).not.toBeChecked();
    expect(screen.getByText(REMINDER)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '儲存' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '取消' })).toBeInTheDocument();
  });

  it('寫好按儲存：送出名稱、內容與預設，回清單並說「已儲存「名稱」。」', async () => {
    // Arrange
    const { user, served, name, body, router } = await openEditor('/description-templates/new');

    // Act
    await user.type(name, '  客製系統  ');
    await typeInEditor(user, body, '## 需求');
    await user.click(screen.getByRole('checkbox', { name: /設成預設/ }));
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByRole('table', { name: '說明格式' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/description-templates');
    expect(served.writes.map((write) => write.body)).toEqual([{ name: '客製系統', body: '## 需求', isDefault: true }]);
    expect(await screen.findByText('已儲存「客製系統」。')).toBeInTheDocument();
    expect(within(screen.getByRole('row', { name: /客製系統/ })).getByText('預設')).toBeInTheDocument();
  });

  it('複製內建的：帶入「網站案子（副本）」與內容，不是預設', async () => {
    // Arrange / Act
    const { name, body } = await openEditor('/description-templates/new?from=web_project');

    // Assert
    await waitFor(() => {
      expect(name).toHaveValue('網站案子（副本）');
    });
    expect(editorText(body)).toBe('基本資料\n客戶：\n網址\n正式：測試：');
    expect(screen.getByRole('checkbox', { name: /設成預設/ })).not.toBeChecked();
  });

  it('名稱重複（409）：訊息放在名稱欄位下，內容不丟', async () => {
    // Arrange
    const { user, served, name, body } = await openEditor('/description-templates/new');
    served.rejectNext({ status: 409, code: 'description_template.name_taken', message: '已經有同名的說明格式。', path: 'name' });

    // Act
    await user.type(name, '維運案子');
    await typeInEditor(user, body, '## 內容');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(name).toHaveAccessibleDescription('已經有同名的說明格式。');
    });
    expect(name).toHaveAttribute('aria-invalid', 'true');
    expect(editorText(body)).toBe('內容');
  });

  it('內容有私鑰（422）：訊息放在內容下，名稱與內容不丟', async () => {
    // Arrange
    const { user, served, name, body } = await openEditor('/description-templates/new');
    served.rejectNext({ status: 422, code: 'project.description_secret', message: '說明裡有私鑰。', path: 'body' });

    // Act
    await user.type(name, '新格式');
    await typeInEditor(user, body, 'x');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('說明裡有私鑰。')).toBeInTheDocument();
    expect(name).toHaveValue('新格式');
    expect(editorText(body)).toBe('x');
  });

  it('上限（409 沒有欄位）：寫在表單上方', async () => {
    // Arrange
    const { user, served, name, body } = await openEditor('/description-templates/new');
    served.rejectNext({ status: 409, code: 'description_template.limit_reached', message: '說明格式最多 50 份。' });

    // Act
    await user.type(name, '新格式');
    await typeInEditor(user, body, 'x');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('說明格式最多 50 份。');
  });

  it('名稱或內容沒填：在欄位旁說明、不送出', async () => {
    // Arrange
    const { user, served, name, body } = await openEditor('/description-templates/new');

    // Act
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(name).toHaveAccessibleDescription('請輸入名稱。');
    expect(body).toHaveAccessibleDescription(/請輸入內容。/);
    expect(served.writes).toHaveLength(0);
  });
});

describe('說明格式：修改', () => {
  it('帶入目前的名稱、內容與預設，改了儲存，用 PUT 送給那一份', async () => {
    // Arrange
    const { user, served, name, body } = await openEditor('/description-templates/op-1');
    await waitFor(() => {
      expect(name).toHaveValue('維運案子');
    });

    // Act
    expect(editorText(body)).toBe('主機\n名稱：');
    await user.clear(name);
    await user.type(name, '維運與保固');
    await user.click(screen.getByRole('checkbox', { name: /設成預設/ }));
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('已儲存「維運與保固」。')).toBeInTheDocument();
    expect(served.writes.at(0)?.url.pathname).toBe('/v1/description-templates/op-1');
    expect(served.writes.at(0)?.body).toEqual({ name: '維運與保固', body: ops.body, isDefault: true });
  });

  it('在編輯器裡接著打字：送出的內容是 Markdown，標題與清單的寫法沒被改壞', async () => {
    // Arrange
    const { user, served, name, body } = await openEditor('/description-templates/op-1');
    await waitFor(() => {
      expect(name).toHaveValue('維運案子');
    });

    // Act
    await typeInEditor(user, body, 'web_01');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    await screen.findByText('已儲存「維運案子」。');
    expect(served.writes.at(0)?.body).toEqual({ name: '維運案子', body: '## 主機\n\n- 名稱：web_01', isDefault: false });
  });

  it('找不到的格式（含內建的）：說找不到，給回清單的路', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveDescriptionTemplates([ops]);

    // Act
    renderApp('/description-templates/web_project');

    // Assert
    expect(await screen.findByText('找不到這個說明格式')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '回到說明格式' })).toHaveAttribute('href', '/description-templates');
  });
});

describe('說明格式：離開提醒', () => {
  it('有改過卻要離開：問一次；「留下來」留在原地、內容還在，「捨棄並離開」才離開', async () => {
    // Arrange
    const { user, body, router } = await openEditor('/description-templates/new');
    await typeInEditor(user, body, '打到一半');

    // Act
    await user.click(screen.getByRole('button', { name: '取消' }));
    const dialog = await screen.findByRole('dialog', { name: '還有沒儲存的修改' });
    await user.click(within(dialog).getByRole('button', { name: '留下來' }));
    const stayedAt = currentPath(router);
    await user.click(screen.getByRole('button', { name: '取消' }));
    await user.click(within(await screen.findByRole('dialog', { name: '還有沒儲存的修改' })).getByRole('button', { name: '捨棄修改並離開' }));

    // Assert
    expect(stayedAt).toBe('/description-templates/new');
    expect(editorText(body)).toBe('打到一半');
    expect(await screen.findByRole('table', { name: '說明格式' })).toBeInTheDocument();
  });

  it('沒改過：取消直接回清單，不問', async () => {
    // Arrange
    const { user, router } = await openEditor('/description-templates/new');

    // Act
    await user.click(screen.getByRole('button', { name: '取消' }));

    // Assert
    expect(await screen.findByRole('table', { name: '說明格式' })).toBeInTheDocument();
    expect(currentPath(router)).toBe('/description-templates');
  });
});

describe('說明格式：編輯頁的其他情況', () => {
  it('不是管理員打新增與修改的網址：說沒有權限，沒有表單', async () => {
    // Arrange
    signedInAs(exampleMember);
    serveDescriptionTemplates([ops]);

    // Act
    const view = renderApp('/description-templates/new');
    const onNew = (await screen.findByText('沒有權限')).isConnected;
    view.unmount();
    renderApp('/description-templates/op-1');
    const onEdit = await screen.findByText('沒有權限');

    // Assert
    expect(onNew).toBe(true);
    expect(onEdit).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: '名稱' })).not.toBeInTheDocument();
  });

  it('?from= 指到不存在的格式：從空白開始', async () => {
    // Arrange / Act
    const { name, body } = await openEditor('/description-templates/new?from=missing');

    // Assert
    expect(name).toHaveValue('');
    expect(editorText(body)).toBe('');
  });

  it('讀不到格式清單：說載入失敗並給「重試」，沒有表單', async () => {
    // Arrange
    signedInAs(exampleOwner);
    server.use(http.get('/v1/description-templates', () => failure(500, 'server.error', '伺服器出錯了。')));

    // Act
    renderApp('/description-templates/op-1');

    // Assert
    expect(await screen.findByText('說明格式載入失敗')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '重試' })).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: '名稱' })).not.toBeInTheDocument();
  });

  it('名稱沒填（422，路徑 name）：訊息放在名稱欄位下', async () => {
    // Arrange
    const { user, served, name, body } = await openEditor('/description-templates/new');
    served.rejectNext({ status: 422, code: 'description_template.name_required', message: '說明格式的名稱不能空白。', path: 'name' });

    // Act
    await user.type(name, 'x');
    await typeInEditor(user, body, 'y');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => { expect(name).toHaveAccessibleDescription('說明格式的名稱不能空白。'); });
    expect(editorText(body)).toBe('y');
  });

  it('儲存時這一份剛好被別人刪掉（404）：表單與內容留著，上方說明，再按「儲存」另存成新的', async () => {
    // Arrange
    const { user, served, name, body, router } = await openEditor('/description-templates/op-1');
    await waitFor(() => { expect(name).toHaveValue('維運案子'); });
    served.removeOnServer('op-1');

    // Act
    await typeInEditor(user, body, '多寫的一行');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('這個說明格式已經被刪掉了。');
    expect(screen.queryByText('找不到這個說明格式')).not.toBeInTheDocument();
    expect(editorText(body)).toBe('主機\n名稱：多寫的一行');
    await user.click(screen.getByRole('button', { name: '儲存' }));
    expect(await screen.findByText('已儲存「維運案子」。')).toBeInTheDocument();
    expect(currentPath(router)).toBe('/description-templates');
    expect(served.current().map((template) => template.name)).toContain('維運案子');
    expect(served.writes.map((write) => write.url.pathname)).toEqual(['/v1/description-templates/op-1', '/v1/description-templates']);
  });

  it('儲存中：「儲存」改標 aria-disabled、焦點還在上面，欄位唯讀', async () => {
    // Arrange
    const { user, name, body } = await openEditor('/description-templates/new');
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => { release = resolve; });
    server.use(
      http.post('/v1/description-templates', async () => {
        await gate;
        return ok(ops, 201);
      }),
    );
    await user.type(name, '新格式');
    await typeInEditor(user, body, 'x');

    // Act
    const save = screen.getByRole('button', { name: '儲存' });
    await user.click(save);

    // Assert
    const saving = await screen.findByRole('button', { name: '儲存中…' });
    expect(saving).toHaveAttribute('aria-disabled', 'true');
    expect(saving).toHaveFocus();
    expect(body).toHaveAttribute('aria-readonly', 'true');
    release();
    expect(await screen.findByRole('table', { name: '說明格式' })).toBeInTheDocument();
  });
});
