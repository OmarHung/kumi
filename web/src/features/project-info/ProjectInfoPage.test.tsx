import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http } from 'msw';
import { failure, ok, signedInAs } from '../../test/api';
import { server } from '../../test/server';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { archivedDetail } from '../../test/projects';
import {
  describedBy,
  serveDescriptionTemplates,
  serveProjectDescription,
  webProjectTemplate,
} from '../../test/projectDescription';
import { currentPath, renderApp } from '../../test/renderApp';
import { chooseOption } from '../../test/select';
import { clearEditor, editorText, findEditor, typeInEditor } from '../../test/richText';
import { formatInstant } from '../../domain/time';

const DRAFT_KEY = 'draft.project-description.SMK';
const ARCHIVED_EDITABLE = '這個專案已經封存，項目只能看；專案說明還是可以改。';
const ARCHIVED_READ_ONLY = '這個專案已封存：只能看，不能新增或修改。';
const REMINDER = '不要貼密碼或金鑰，寫密碼管理器裡的項目名稱就好。';
const UPDATED_AT = '2026-11-20T08:05:00Z';

function openInfo(options: { readonly detail?: ReturnType<typeof smkDetail> } = {}) {
  signedInAs(exampleOwner, { details: [options.detail ?? smkDetail()] });
  serveDescriptionTemplates();
  return renderApp('/projects/SMK/info');
}

const readerDetail = () => smkDetail({ myPermissions: ['item.read_own', 'comment.create'] });

describe('資訊頁：看說明', () => {
  it('有說明：照 Markdown 畫，下面一行寫最後由誰在什麼時候更新', async () => {
    // Arrange
    serveProjectDescription('SMK', describedBy('## 網址\n- 正式：https://www.example.com'));

    // Act
    openInfo();

    // Assert
    expect(await screen.findByRole('heading', { name: '網址' })).toBeInTheDocument();
    expect(screen.getByText(`最後由 王小明 於 ${formatInstant(UPDATED_AT)} 更新`)).toBeInTheDocument();
  });

  it('頁列是「專案名稱 › 資訊」，專案名稱連回專案', async () => {
    // Arrange
    serveProjectDescription('SMK', describedBy('內容'));

    // Act
    const { user, router } = openInfo();
    await screen.findByText('內容');
    await user.click(screen.getByRole('link', { name: '冒煙測試' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(router)).toMatch(/^\/projects\/SMK/);
    });
    expect(currentPath(router)).not.toContain('/info');
  });

  it('沒有 project.manage（例如只看自己項目的執行者）：看得到說明，沒有「編輯」', async () => {
    // Arrange
    serveProjectDescription('SMK', describedBy('主機：example-host'));

    // Act
    openInfo({ detail: readerDetail() });

    // Assert
    expect(await screen.findByText(/example-host/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '編輯專案說明' })).not.toBeInTheDocument();
  });

  it('說明裡的附件圖片不發出請求，也不畫圖', async () => {
    // Arrange
    const id = '11111111-2222-3333-4444-555555555555';
    serveProjectDescription('SMK', describedBy(`架構圖：![圖](attachment:${id})`));

    // Act
    const { container } = openInfo();
    await screen.findByText(/架構圖/);

    // Assert
    await waitFor(() => {
      expect(container.querySelector('[data-state="rendered"]')).not.toBeNull();
    });
    // 只看說明那一塊：外框的側欄本來就有產品標誌的 <img>。
    expect(container.querySelector('[data-state="rendered"]')?.querySelector('img')).toBeNull();
  });

  it('封存的專案：能改的人看到「專案說明還是可以改」，而且真的能儲存', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK', describedBy('舊的主機'));
    const { user } = openInfo({ detail: archivedDetail(smkDetail()) });
    expect(await screen.findByText(ARCHIVED_EDITABLE)).toBeInTheDocument();
    expect(screen.queryByText(ARCHIVED_READ_ONLY)).not.toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));
    const editor = await findEditor('說明');

    // Act
    await clearEditor(user, editor);
    await typeInEditor(user, editor, '新的主機');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('新的主機')).toBeInTheDocument();
    expect(backend.saved[0]?.body).toEqual({ text: '新的主機' });
  });

  it('封存的專案：不能改的人照原本的提示，沒有「編輯」', async () => {
    // Arrange
    serveProjectDescription('SMK', describedBy('內容'));

    // Act
    openInfo({ detail: archivedDetail(smkDetail({ myPermissions: ['item.read', 'blueprint.read'] })) });

    // Assert
    expect(await screen.findByText(ARCHIVED_READ_ONLY)).toBeInTheDocument();
    expect(await screen.findByText('內容')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '編輯專案說明' })).not.toBeInTheDocument();
  });
});

describe('資訊頁：沒有說明', () => {
  it('不能改的人只看到「還沒有專案說明。」', async () => {
    // Arrange
    serveProjectDescription('SMK');

    // Act
    openInfo({ detail: readerDetail() });

    // Assert
    expect(await screen.findByText('還沒有專案說明。')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '開始寫' })).not.toBeInTheDocument();
  });

  it('能改的人：格式的下拉預設是預設的那一份，「開始寫」填入格式的內容，按儲存才送出', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK');
    const { user } = openInfo();
    const select = await screen.findByRole('combobox', { name: '說明格式' });
    await waitFor(() => {
      expect(select).toHaveTextContent('網站案子');
    });

    // Act
    await user.click(screen.getByRole('button', { name: '開始寫' }));
    const editor = await findEditor('說明');
    expect(within(editor).getByRole('heading', { name: '基本資料' })).toBeInTheDocument();
    expect(backend.saved).toHaveLength(0);
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(backend.saved).toHaveLength(1);
    });
    expect(backend.saved[0]?.body).toEqual({ text: webProjectTemplate.body });
    expect(await screen.findByRole('heading', { name: '基本資料' })).toBeInTheDocument();
    expect(screen.getByText(/最後由 範例擁有者 於/)).toBeInTheDocument();
  });

  it('套用格式後打字再儲存：送出的是格式的 Markdown 加上打的字，標題與清單都在', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK');
    const { user } = openInfo();
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: '說明格式' })).toHaveTextContent('網站案子');
    });
    await user.click(screen.getByRole('button', { name: '開始寫' }));
    const editor = await findEditor('說明');

    // Act
    await typeInEditor(user, editor, 'https://www.example.com');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(backend.saved).toHaveLength(1);
    });
    expect(backend.saved[0]?.body).toEqual({
      text: '## 基本資料\n\n- 客戶：\n\n## 網址\n\n- 正式：\n- 測試：https://www.example.com',
    });
  });

  it('選「空白」再開始寫：文字框是空的', async () => {
    // Arrange
    serveProjectDescription('SMK');
    const { user } = openInfo();
    const select = await screen.findByRole('combobox', { name: '說明格式' });
    await waitFor(() => {
      expect(select).toHaveTextContent('網站案子');
    });

    // Act
    await chooseOption(user, select, '空白');
    await user.click(screen.getByRole('button', { name: '開始寫' }));

    // Assert
    expect(editorText(await findEditor('說明'))).toBe('');
  });
});

describe('資訊頁：編輯', () => {
  it('編輯、儲存：送出新的內容，畫面換成新的說明與新的更新者', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK', describedBy('舊的內容'));
    const { user } = openInfo();
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));
    const editor = await findEditor('說明');
    expect(editorText(editor)).toBe('舊的內容');

    // Act
    await clearEditor(user, editor);
    await typeInEditor(user, editor, '新的內容');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('新的內容')).toBeInTheDocument();
    expect(backend.saved[0]?.body).toEqual({ text: '新的內容' });
    expect(screen.queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
    expect(sessionStorage.getItem(DRAFT_KEY)).toBeNull();
    expect(screen.getByText(/最後由 範例擁有者 於/)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '編輯專案說明' })).toHaveFocus();
    });
  });

  it('按鉛筆才進入編輯：平常是畫好的說明，沒有編輯器', async () => {
    // Arrange
    serveProjectDescription('SMK', describedBy('主機：example-host'));
    const { user } = openInfo();
    await screen.findByText(/example-host/);
    expect(screen.queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();

    // Act
    await user.click(screen.getByRole('button', { name: '編輯專案說明' }));

    // Assert
    expect(editorText(await findEditor('說明'))).toBe('主機：example-host');
    expect(screen.queryByRole('button', { name: '編輯專案說明' })).not.toBeInTheDocument();
  });

  it('打開編輯器不改就儲存：不送出請求，回到原本的說明', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK', describedBy('* 一\n* 二'));
    const { user } = openInfo();
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));
    await findEditor('說明');

    // Act
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
    });
    expect(backend.saved).toHaveLength(0);
    expect(sessionStorage.getItem(DRAFT_KEY)).toBeNull();
  });

  it('打字儲存：送到伺服器的是 Markdown（標題與清單的寫法沒有被改壞）', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK', describedBy('## 網址\n- 正式：https://www.example.com'));
    const { user } = openInfo();
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));
    const editor = await findEditor('說明');

    // Act
    await typeInEditor(user, editor, '/a_b');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(backend.saved).toHaveLength(1);
    });
    expect(backend.saved[0]?.body).toEqual({ text: '## 網址\n\n- 正式：https://www.example.com/a_b' });
  });

  it('編輯器下面固定有不要貼密碼與金鑰的提醒', async () => {
    // Arrange
    serveProjectDescription('SMK', describedBy('內容'));
    const { user } = openInfo();

    // Act
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));

    // Assert
    expect(await screen.findByText(REMINDER)).toBeInTheDocument();
  });

  it('取消：不送出，回到原本的說明', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK', describedBy('原本的內容'));
    const { user } = openInfo();
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));
    const editor = await findEditor('說明');
    await typeInEditor(user, editor, '多打的字');

    // Act
    await user.click(screen.getByRole('button', { name: '取消' }));

    // Assert
    expect(await screen.findByText('原本的內容')).toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(backend.saved).toHaveLength(0);
    expect(sessionStorage.getItem(DRAFT_KEY)).toBeNull();
  });

  it('伺服器回 422：訊息顯示在編輯器下面，內容不丟，還在編輯中', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK', describedBy('內容'));
    backend.rejectNext({
      status: 422,
      code: 'project.description_secret',
      message: '說明裡有私鑰。私鑰不能存在這裡，請改寫密碼管理器裡的項目名稱。',
      path: 'text',
    });
    const { user } = openInfo();
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));
    const editor = await findEditor('說明');
    await typeInEditor(user, editor, '加一行');

    // Act
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText(/說明裡有私鑰/)).toBeInTheDocument();
    expect(editorText(screen.getByRole('textbox', { name: '說明' }))).toBe('內容加一行');
  });

  it('按「儲存」時焦點先回到編輯器；失敗後再打字，錯誤訊息收掉', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK', describedBy('內容'));
    backend.rejectNext({
      status: 422,
      code: 'project.description_secret',
      message: '說明裡有私鑰。私鑰不能存在這裡，請改寫密碼管理器裡的項目名稱。',
      path: 'text',
    });
    const { user } = openInfo();
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));
    const editor = await findEditor('說明');
    await typeInEditor(user, editor, '加一行');

    // Act
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('說明裡有私鑰');
    expect(editor).toHaveFocus();

    // Act
    await user.keyboard('！');

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
  });

  it('清成空白再儲存：送出 null，回到「還沒有專案說明。」', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK', describedBy('內容'));
    const { user } = openInfo();
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));
    await clearEditor(user, await findEditor('說明'));

    // Act
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('還沒有專案說明。')).toBeInTheDocument();
    expect(backend.saved[0]?.body).toEqual({ text: null });
  });

  it('上次沒送出的草稿：回到這一頁就還原並進入編輯，旁邊說一聲', async () => {
    // Arrange
    sessionStorage.setItem('draft.project-description.SMK', JSON.stringify({ text: '寫到一半', base: '內容' }));
    serveProjectDescription('SMK', describedBy('內容'));

    // Act
    openInfo();

    // Assert
    expect(editorText(await findEditor('說明'))).toBe('寫到一半');
    expect(await screen.findByText('已還原上次沒送出的內容。')).toBeInTheDocument();
  });

  it('伺服器回 403：訊息顯示出來，內容不丟，還在編輯中', async () => {
    // Arrange
    const backend = serveProjectDescription('SMK', describedBy('內容'));
    backend.rejectNext({ status: 403, code: 'work.forbidden', message: '你的角色不能修改專案說明。' });
    const { user } = openInfo();
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));
    await typeInEditor(user, await findEditor('說明'), '加一行');

    // Act
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('你的角色不能修改專案說明。')).toBeInTheDocument();
    expect(editorText(screen.getByRole('textbox', { name: '說明' }))).toBe('內容加一行');
  });

  it('草稿是在舊的說明上打的（別人之後改過）：換成警告，說儲存會蓋掉新的', async () => {
    // Arrange
    sessionStorage.setItem(DRAFT_KEY, JSON.stringify({ text: '我的版本', base: '舊的說明' }));
    serveProjectDescription('SMK', describedBy('別人改過的說明'));

    // Act
    openInfo();

    // Assert
    expect(editorText(await findEditor('說明'))).toBe('我的版本');
    expect(await screen.findByText(/說明在你離開之後被別人改過/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /看伺服器上的內容/ })).toBeInTheDocument();
  });
});

describe('資訊頁：說明格式的清單', () => {
  it('載入中：下拉顯示「載入中…」，「開始寫」停用，不先顯示「空白」', async () => {
    // Arrange
    serveProjectDescription('SMK');
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    signedInAs(exampleOwner, { details: [smkDetail()] });
    server.use(
      http.get('/v1/description-templates', async () => {
        await gate;
        return ok([webProjectTemplate]);
      }),
    );

    // Act
    renderApp('/projects/SMK/info');
    const select = await screen.findByRole('combobox', { name: '說明格式' });

    // Assert
    expect(select).toHaveTextContent('載入中…');
    expect(select).not.toHaveTextContent('空白');
    expect(screen.getByRole('button', { name: '開始寫' })).toBeDisabled();
    release();
    await waitFor(() => {
      expect(select).toHaveTextContent('網站案子');
    });
    expect(screen.getByRole('button', { name: '開始寫' })).toBeEnabled();
  });

  it('讀不到（500）：說「說明格式讀不到。」加「重試」，下拉只剩「空白」，「開始寫」從空白開始', async () => {
    // Arrange
    serveProjectDescription('SMK');
    signedInAs(exampleOwner, { details: [smkDetail()] });
    server.use(http.get('/v1/description-templates', () => failure(500, 'server.error', '伺服器出錯了。')));
    const { user } = renderApp('/projects/SMK/info');

    // Act
    expect(await screen.findByText('說明格式讀不到。')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '重試' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: '說明格式' })).toHaveTextContent('空白');
    await user.click(screen.getByRole('button', { name: '開始寫' }));

    // Assert
    expect(editorText(await findEditor('說明'))).toBe('');
  });

  it('按「重試」讀到格式之後，提示不見，預設的格式選好', async () => {
    // Arrange
    serveProjectDescription('SMK');
    signedInAs(exampleOwner, { details: [smkDetail()] });
    server.use(http.get('/v1/description-templates', () => failure(500, 'server.error', '伺服器出錯了。')));
    const { user } = renderApp('/projects/SMK/info');
    await screen.findByText('說明格式讀不到。');
    server.use(http.get('/v1/description-templates', () => ok([webProjectTemplate])));

    // Act
    await user.click(screen.getByRole('button', { name: '重試' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByText('說明格式讀不到。')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('combobox', { name: '說明格式' })).toHaveTextContent('網站案子');
  });
});
