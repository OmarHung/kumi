import { act, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http } from 'msw';
import { failure, signedInAs } from '../../../test/api';
import { exampleOwner } from '../../../test/fixtures';
import { serveKnowledge } from '../../../test/knowledge';
import { currentPath, renderApp } from '../../../test/renderApp';
import { clearEditor, editorText, findEditor, typeInEditor } from '../../../test/richText';
import { server } from '../../../test/server';
import { pageDraftKey, serializePageDraft } from './pageDraft';

const BODY = '舊的內容';
const DRAFT_KEY = pageDraftKey('DOCS', 2);

function openPage(path = '/knowledge/DOCS/2', options: Parameters<typeof serveKnowledge>[0] = {}) {
  signedInAs(exampleOwner);
  const backend = serveKnowledge({ bodies: { 'DOCS/2': BODY }, ...options });
  return { backend, ...renderApp(path) };
}

async function startEditing(view: ReturnType<typeof openPage>) {
  await view.user.click(await screen.findByRole('button', { name: '編輯' }));
  return findEditor('內容');
}

function patchesOf(backend: ReturnType<typeof serveKnowledge>) {
  return backend.requests.filter((request) => request.method === 'PATCH').map((request) => request.body);
}

describe('編輯一頁', () => {
  it('改內容再儲存：只送內容與開始編輯時的版本，存好之後回到檢視、焦點回到「編輯」', async () => {
    // Arrange
    const view = openPage();
    const editor = await startEditing(view);

    // Act
    await typeInEditor(view.user, editor, '追加的一行');
    await view.user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText(/追加的一行/)).toBeInTheDocument();
    expect(patchesOf(view.backend)).toEqual([{ body: expect.stringContaining('追加的一行') as string, expectedRevision: 1 }]);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '編輯' })).toHaveFocus();
    });
    expect(sessionStorage.getItem(DRAFT_KEY)).toBeNull();
  });

  it('只改標題：只送標題；樹上的標題也跟著換', async () => {
    // Arrange
    const view = openPage();
    await startEditing(view);
    const title = screen.getByRole('textbox', { name: '標題' });

    // Act
    await view.user.clear(title);
    await view.user.type(title, '帳號與權限');
    await view.user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByRole('heading', { level: 1, name: '帳號與權限' })).toBeInTheDocument();
    expect(patchesOf(view.backend)).toEqual([{ title: '帳號與權限', expectedRevision: 1 }]);
  });

  it('標題在輸入框裡按 Enter 是跳到內容，不是儲存', async () => {
    // Arrange
    const view = openPage();
    const editor = await startEditing(view);
    const title = screen.getByRole('textbox', { name: '標題' });

    // Act
    await view.user.click(title);
    await view.user.keyboard('{Enter}');

    // Assert
    expect(editor).toHaveFocus();
    expect(patchesOf(view.backend)).toHaveLength(0);
  });

  it('標題空白：說明，不送請求', async () => {
    // Arrange
    const view = openPage();
    await startEditing(view);
    await view.user.clear(screen.getByRole('textbox', { name: '標題' }));

    // Act
    await view.user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('標題不能是空的。')).toBeInTheDocument();
    expect(patchesOf(view.backend)).toHaveLength(0);
  });

  it('沒改就儲存：直接結束編輯，不送請求', async () => {
    // Arrange
    const view = openPage();
    await startEditing(view);

    // Act
    await view.user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByRole('button', { name: '編輯' })).toBeInTheDocument();
    expect(patchesOf(view.backend)).toHaveLength(0);
  });

  it('取消：有修改時先問要不要捨棄；確定捨棄才回到檢視、草稿也清掉', async () => {
    // Arrange
    const view = openPage();
    const editor = await startEditing(view);
    await typeInEditor(view.user, editor, '寫到一半');
    expect(sessionStorage.getItem(DRAFT_KEY)).not.toBeNull();

    // Act
    await view.user.click(screen.getByRole('button', { name: '取消' }));
    const dialog = await screen.findByRole('dialog', { name: '捨棄修改？' });
    expect(screen.getByRole('textbox', { name: '標題' })).toBeInTheDocument();
    await view.user.click(within(dialog).getByRole('button', { name: '捨棄修改' }));

    // Assert
    expect(await screen.findByRole('button', { name: '編輯' })).toBeInTheDocument();
    expect(screen.queryByText(/寫到一半/)).not.toBeInTheDocument();
    expect(sessionStorage.getItem(DRAFT_KEY)).toBeNull();
  });

  it('取消時選「繼續編輯」：留在編輯，修改都還在', async () => {
    // Arrange
    const view = openPage();
    const editor = await startEditing(view);
    await typeInEditor(view.user, editor, '寫到一半');

    // Act
    await view.user.click(screen.getByRole('button', { name: '取消' }));
    await view.user.click(within(await screen.findByRole('dialog', { name: '捨棄修改？' })).getByRole('button', { name: '繼續編輯' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(editorText(await findEditor('內容'))).toContain('寫到一半');
    expect(sessionStorage.getItem(DRAFT_KEY)).not.toBeNull();
  });

  it('編輯器裡按 Esc：有修改也先問', async () => {
    // Arrange
    const view = openPage();
    const editor = await startEditing(view);
    await typeInEditor(view.user, editor, '寫到一半');

    // Act
    await view.user.keyboard('{Escape}');

    // Assert
    expect(await screen.findByRole('dialog', { name: '捨棄修改？' })).toBeInTheDocument();
  });

  it('沒有修改就取消：直接回到檢視，不問', async () => {
    // Arrange
    const view = openPage();
    await startEditing(view);

    // Act
    await view.user.click(screen.getByRole('button', { name: '取消' }));

    // Assert
    expect(await screen.findByRole('button', { name: '編輯' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('伺服器說標題有問題（422）：寫在標題下面，編輯的內容留著', async () => {
    // Arrange
    const view = openPage();
    const editor = await startEditing(view);
    await typeInEditor(view.user, editor, '寫了很多');
    server.use(
      http.patch('/v1/knowledge/spaces/:key/pages/:number', () =>
        failure(422, 'common.validation', '輸入有問題。', [{ code: 'knowledge.title_invalid', path: 'title', message: '標題不能有換行。' }]),
      ),
    );
    await view.user.type(screen.getByRole('textbox', { name: '標題' }), '新');

    // Act
    await view.user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('標題不能有換行。')).toBeInTheDocument();
    expect(editorText(await findEditor('內容'))).toContain('寫了很多');
  });
});

describe('草稿', () => {
  it('寫到一半換頁再回來：還原並進入編輯，說一聲，可以捨棄', async () => {
    // Arrange
    const view = openPage();
    const editor = await startEditing(view);
    await typeInEditor(view.user, editor, '沒送出的字');
    view.unmount();

    // Act
    const again = renderApp('/knowledge/DOCS/2');

    // Assert
    expect(await screen.findByText('已還原上次沒送出的內容。')).toBeInTheDocument();
    expect(editorText(await findEditor('內容'))).toContain('沒送出的字');
    await again.user.click(screen.getByRole('button', { name: '捨棄還沒儲存的頁面' }));
    expect(editorText(await findEditor('內容'))).toBe(BODY);
    expect(screen.queryByText('已還原上次沒送出的內容。')).not.toBeInTheDocument();
  });

  it('還原的草稿是在舊的內容上打的（別人之後改過）：警告，儲存會蓋掉新的內容，送出的是現在的版本', async () => {
    // Arrange
    sessionStorage.setItem(
      DRAFT_KEY,
      JSON.stringify({
        text: serializePageDraft({ title: '帳號申請', body: '我的草稿' }),
        base: serializePageDraft({ title: '帳號申請', body: '更舊的內容' }),
      }),
    );
    const view = openPage();

    // Act
    expect(await screen.findByText(/這一頁在你離開之後被別人改過/)).toBeInTheDocument();
    await view.user.click(await screen.findByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(patchesOf(view.backend)).toEqual([{ body: '我的草稿', expectedRevision: 1 }]);
    });
  });

  it('壞掉的草稿當成沒有：不進編輯、不丟例外', async () => {
    // Arrange
    sessionStorage.setItem(DRAFT_KEY, '{不是 JSON');

    // Act
    openPage();

    // Assert
    expect(await screen.findByRole('button', { name: '編輯' })).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: '標題' })).not.toBeInTheDocument();
  });
});

describe('別人先改了這一頁', () => {
  async function conflictOn(view: ReturnType<typeof openPage>) {
    const editor = await startEditing(view);
    await typeInEditor(view.user, editor, '我的追加');
    // 別人在這時存了：伺服器上的版本變成 2
    view.backend.bodies['DOCS/2'] = '別人改的內容';
    view.backend.revisions['DOCS/2'] = 2;
    await view.user.click(screen.getByRole('button', { name: '儲存' }));
    await screen.findByText('這一頁在你編輯時被別人改過了。');
  }

  it('儲存被擋下（409）：什麼都沒改，自己的內容留著，給「看新的內容」與「用我的版本覆蓋」', async () => {
    // Arrange
    const view = openPage();

    // Act
    await conflictOn(view);

    // Assert
    expect(screen.getByRole('button', { name: '看新的內容' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '用我的版本覆蓋' })).toBeInTheDocument();
    expect(editorText(await findEditor('內容'))).toContain('我的追加');
  });

  it('「看新的內容」：換成伺服器上現在的內容，留在編輯中，提示不見', async () => {
    // Arrange
    const view = openPage();
    await conflictOn(view);

    // Act
    await view.user.click(screen.getByRole('button', { name: '看新的內容' }));

    // Assert
    await waitFor(() => {
      expect(editorText(screen.getByRole('textbox', { name: '內容' }))).toBe('別人改的內容');
    });
    expect(screen.queryByText('這一頁在你編輯時被別人改過了。')).not.toBeInTheDocument();
  });

  it('「用我的版本覆蓋」：用現在的版本再送一次，存好', async () => {
    // Arrange
    const view = openPage();
    await conflictOn(view);

    // Act
    await view.user.click(screen.getByRole('button', { name: '用我的版本覆蓋' }));

    // Assert
    expect(await screen.findByRole('button', { name: '編輯' })).toBeInTheDocument();
    expect(patchesOf(view.backend).at(-1)).toEqual({ body: expect.stringContaining('我的追加') as string, expectedRevision: 2 });
  });

  it('編輯中背景重抓到新的版本：提示（不打斷），「繼續編輯」之後同一個版本不再提示', async () => {
    // Arrange
    const view = openPage();
    await startEditing(view);

    // Act
    view.backend.revisions['DOCS/2'] = 2;
    await act(async () => {
      await view.queryClient.invalidateQueries({ queryKey: ['knowledge', 'space', 'DOCS', 'page', 2] });
    });

    // Assert
    expect(await screen.findByText('這一頁在你編輯時被別人改過了。')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '用我的版本覆蓋' })).not.toBeInTheDocument();
    await view.user.click(screen.getByRole('button', { name: '繼續編輯' }));
    expect(screen.queryByText('這一頁在你編輯時被別人改過了。')).not.toBeInTheDocument();
  });
});

describe('離開有沒存的修改', () => {
  it('站內換頁先問：留下來就留著，捨棄修改並離開才走', async () => {
    // Arrange
    const view = openPage();
    const editor = await startEditing(view);
    await typeInEditor(view.user, editor, '還沒存');
    const link = screen.getByRole('link', { name: '會議記錄', description: '' });

    // Act
    await view.user.click(link);

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '還有沒儲存的修改' });
    expect(dialog).toHaveTextContent('離開這一頁的話，標題與內容的修改會不見。');
    await view.user.click(screen.getByRole('button', { name: '留下來' }));
    expect(currentPath(view.router)).toBe('/knowledge/DOCS/2');
    await view.user.click(link);
    await view.user.click(await screen.findByRole('button', { name: '捨棄修改並離開' }));
    await waitFor(() => {
      expect(currentPath(view.router)).toBe('/knowledge/DOCS/4');
    });
  });

  it('什麼都沒改就換頁：不問', async () => {
    // Arrange
    const view = openPage();
    await startEditing(view);

    // Act
    await view.user.click(screen.getByRole('link', { name: '會議記錄' }));

    // Assert
    await waitFor(() => {
      expect(currentPath(view.router)).toBe('/knowledge/DOCS/4');
    });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('清空內容', () => {
  it('內容清成空白再儲存：送出空字串', async () => {
    // Arrange
    const view = openPage();
    const editor = await startEditing(view);
    await clearEditor(view.user, editor);

    // Act
    await view.user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(patchesOf(view.backend)).toEqual([{ body: '', expectedRevision: 1 }]);
    });
  });
});
