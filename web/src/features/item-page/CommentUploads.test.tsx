import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Permission } from '../../api/blueprint';
import type { ProjectDetail } from '../../api/types';
import { created, serveActivity, serveComment, showAllActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { aFile, attachment, installFakeUploads, serveAttachments, type FakeUpload } from '../../test/attachments';
import { exampleOwner, smkDetail, smkItem, smkProject } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { editorText, findEditor, tiptapOf, typeInEditor } from '../../test/richText';
import { server } from '../../test/server';
import { NO_UPLOAD_PERMISSION } from '../attachments/useEditorUploads';

// 留言框（所見即所得的編輯器）的貼圖與附加檔案。斷言 Markdown 一律看分頁裡存的草稿或送到 API 的內容，不看編輯器的畫面。
beforeEach(() => {
  showAllActivity();
});

const MB = 1024 * 1024;
const IMAGE_ID = attachment(1).id;
const DRAFT_KEY = 'draft.comment.SMK-7';
const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }), '原本的說明');
const COMMENTER: readonly Permission[] = ['item.read', 'comment.create', 'blueprint.read'];

let uploads: readonly FakeUpload[];

beforeEach(() => {
  uploads = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

async function openItem(detail: ProjectDetail = smkDetail()) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItem(task);
  const timeline = serveActivity(task.key, [created()]);
  const served = serveAttachments(task.key, []);
  const posted = serveComment(task.key);
  const view = renderApp('/projects/SMK/items/SMK-7');
  const section = await screen.findByRole('region', { name: '活動與留言' });
  await within(section).findByRole('list', { name: '活動與留言' });
  const editor = await findEditor('留言', section);
  return { ...view, section, editor, timeline, served, posted };
}

/** 留言框存在分頁裡的草稿（Markdown）。 */
async function expectDraft(markdown: string | null): Promise<void> {
  await waitFor(() => {
    expect(sessionStorage.getItem(DRAFT_KEY)).toBe(markdown);
  });
}

function pasteFiles(target: HTMLElement, files: File[]) {
  fireEvent.paste(target, { clipboardData: { files, types: ['Files'], getData: () => '' } });
}

function dropFiles(target: HTMLElement, files: File[]) {
  fireEvent.drop(target, { dataTransfer: { files, types: ['Files'] } });
}

function statusTexts(section: HTMLElement): (string | null)[] {
  return within(section).getAllByRole('status').map((node) => node.textContent);
}

const screenshot = () => aFile('截圖.png', 2048, 'image/png');
const imageFile = (name: string) => aFile(name, 100, 'image/png');
const pngAttachment = (name = '截圖.png') => attachment(1, { fileName: name, contentType: 'image/png', sizeBytes: 2048 });
const pngNamed = (number: number, name: string) => attachment(number, { fileName: name, contentType: 'image/png' });

/** 編輯器裡某段文字開頭的位置（設游標用）。 */
function positionOf(editor: HTMLElement, text: string): number {
  const found: { position: number } = { position: -1 };
  tiptapOf(editor).state.doc.descendants((node, position) => {
    if (found.position === -1 && node.isText && node.text?.includes(text) === true) found.position = position + (node.text.indexOf(text));
    return found.position === -1;
  });
  return found.position;
}

describe('留言框：貼圖與附加檔案', () => {
  it('貼上圖片：游標的位置先放「上傳中」，上傳到附件的端點，送出停用並說明原因', async () => {
    // Arrange
    const { section, editor, user } = await openItem();
    await typeInEditor(user, editor, '看這個：');

    // Act
    pasteFiles(editor, [screenshot()]);

    // Assert
    await expectDraft('看這個：\n![上傳中：截圖.png…]()');
    expect(within(editor).getByText('上傳中：截圖.png…')).toBeInTheDocument();
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    expect(uploads[0]?.url).toBe('/v1/items/SMK-7/attachments');
    const send = within(section).getByRole('button', { name: '送出留言' });
    expect(send).toHaveAttribute('aria-disabled', 'true');
    expect(send).toHaveAccessibleDescription('還有檔案在上傳');
  });

  it('上傳完成：記號換成圖片，存的是 ![檔名](attachment:編號)，唸出完成，附件清單與活動重抓，可以送出', async () => {
    // Arrange
    const { section, editor, served, timeline, user } = await openItem();
    pasteFiles(editor, [screenshot()]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    const before = served.listRequests();
    const activityBefore = timeline.requests.length;

    // Act
    uploads[0]?.respond(201, pngAttachment());

    // Assert
    await expectDraft(`![截圖.png](attachment:${IMAGE_ID})`);
    expect(await within(editor).findByRole('img', { name: '截圖.png' })).toBeInTheDocument();
    expect(within(editor).queryByText(/上傳中/)).not.toBeInTheDocument();
    await waitFor(() => {
      expect(statusTexts(section)).toContain('上傳完成：截圖.png');
    });
    await waitFor(() => {
      expect(served.listRequests()).toBeGreaterThan(before);
      expect(timeline.requests.length).toBeGreaterThan(activityBefore);
    });
    const send = within(section).getByRole('button', { name: '送出留言' });
    expect(send).not.toHaveAttribute('aria-disabled');
    await user.click(send);
  });

  it('非圖片：換成檔案的小卡，存的是檔案連結；檔名裡的中括號與反斜線跳脫，換行換成空白', async () => {
    // Arrange
    const { editor } = await openItem();
    pasteFiles(editor, [aFile('報告 [v2]\\最終.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    uploads[0]?.respond(201, attachment(2, { fileName: '報告 [v2]\\最終\n.pdf' }));

    // Assert
    await expectDraft(`[報告 \\[v2\\]\\最終 .pdf](attachment:${attachment(2).id})`);
  });

  it('上傳失敗：記號拿掉，編輯器下面寫「檔名 上傳失敗：原因」，送出恢復', async () => {
    // Arrange
    const { section, editor, user } = await openItem();
    await typeInEditor(user, editor, '前');
    pasteFiles(editor, [screenshot()]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    uploads[0]?.respondFailure(409, 'attachment.limit_reached', '這個項目的附件已經滿了。');

    // Assert
    await expectDraft('前');
    expect(editorText(editor)).toBe('前');
    expect(await within(section).findByText('截圖.png 上傳失敗：這個項目的附件已經滿了。', { selector: 'div' })).toBeInTheDocument();
    await waitFor(() => {
      expect(statusTexts(section)).toContain('截圖.png 上傳失敗：這個項目的附件已經滿了。');
    });
    expect(within(section).getByRole('button', { name: '送出留言' })).not.toHaveAttribute('aria-disabled');
  });

  it('超過 25 MB：不上傳、不插入，說明「超過 25 MB：檔名」；其他的照傳', async () => {
    // Arrange
    const { section, editor } = await openItem();

    // Act
    pasteFiles(editor, [aFile('大.pdf', 26 * MB), screenshot()]);

    // Assert
    expect(await within(section).findByText('超過 25 MB：大.pdf', { selector: 'div' })).toBeInTheDocument();
    await expectDraft('![上傳中：截圖.png…]()');
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
  });

  it('同時最多 3 個，其他排隊', async () => {
    // Arrange
    const { editor } = await openItem();

    // Act
    pasteFiles(editor, ['a', 'b', 'c', 'd'].map((name) => aFile(`${name}.png`, 100, 'image/png')));

    // Assert
    await waitFor(() => {
      expect(uploads).toHaveLength(3);
    });
    uploads[0]?.respond(201, attachment(1, { fileName: 'a.png', contentType: 'image/png' }));
    await waitFor(() => {
      expect(uploads).toHaveLength(4);
    });
  });

  it('只有文字的貼上照常，不攔', async () => {
    // Arrange
    const { editor } = await openItem();
    const event = new Event('paste', { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'clipboardData', { value: { files: [], types: ['text/plain'], getData: (type: string) => (type === 'text/plain' ? '字' : '') } });

    // Act
    editor.dispatchEvent(event);

    // Assert
    expect(uploads).toHaveLength(0);
  });

  it('拖進編輯器：放下就上傳；拖曳時外框換成焦點的樣子，放下後恢復', async () => {
    // Arrange
    const { editor } = await openItem();
    const field = editor.closest('[data-collapsed]');

    // Act
    fireEvent.dragEnter(editor, { dataTransfer: { types: ['Files'] } });

    // Assert
    await waitFor(() => {
      expect(field).toHaveAttribute('data-dropping', 'true');
    });
    dropFiles(editor, [screenshot()]);
    await waitFor(() => {
      expect(field).not.toHaveAttribute('data-dropping');
    });
    await expectDraft('![上傳中：截圖.png…]()');
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
  });

  it('拖一段文字經過不亮、不攔', async () => {
    // Arrange
    const { editor } = await openItem();

    // Act
    fireEvent.dragEnter(editor, { dataTransfer: { types: ['text/plain'] } });

    // Assert
    expect(editor.closest('[data-collapsed]')).not.toHaveAttribute('data-dropping');
  });

  it('「插入圖片或檔案」：可以多選，每個檔案一個記號', async () => {
    // Arrange
    const { section, editor, user } = await openItem();
    await user.click(editor);

    // Act
    await user.upload(within(section).getByLabelText('選擇要插入的圖片或檔案'), [screenshot(), aFile('規格.pdf')]);

    // Assert
    await waitFor(() => {
      expect(within(editor).getAllByText(/^上傳中：/)).toHaveLength(2);
    });
    await expectDraft('![上傳中：截圖.png…]()\n[上傳中：規格.pdf…]()');
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });
  });

  it('還在上傳時 Ctrl+Enter 也不送出；傳完才送', async () => {
    // Arrange
    const { section, editor, user, posted } = await openItem();
    pasteFiles(editor, [screenshot()]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    await user.click(editor);
    await user.keyboard('{Control>}{Enter}{/Control}');
    await user.click(within(section).getByRole('button', { name: '送出留言' }));

    // Assert：不送，並且用即時區域說原因
    expect(posted).toHaveLength(0);
    await waitFor(() => {
      expect(within(section).getAllByRole('status').map((live) => live.textContent)).toContain('還有檔案在上傳');
    });
    uploads[0]?.respond(201, pngAttachment());
    await expectDraft(`![截圖.png](attachment:${IMAGE_ID})`);
    await user.keyboard('{Control>}{Enter}{/Control}');
    await waitFor(() => {
      expect(posted).toHaveLength(1);
    });
    expect(posted[0]?.body).toEqual({ body: `![截圖.png](attachment:${IMAGE_ID})` });
  });
});

describe('留言框：不能上傳的人', () => {
  it('只能留言的人：沒有按鈕；貼上或拖進檔案說明原因，不上傳、不插入', async () => {
    // Arrange
    const { section, editor } = await openItem(smkDetail({ myPermissions: COMMENTER }));
    expect(within(section).queryByRole('button', { name: '插入圖片或檔案' })).not.toBeInTheDocument();

    // Act
    pasteFiles(editor, [screenshot()]);

    // Assert
    expect(await within(section).findByText(NO_UPLOAD_PERMISSION, { selector: 'div' })).toBeInTheDocument();
    expect(editorText(editor)).toBe('');
    dropFiles(editor, [screenshot()]);
    expect(uploads).toHaveLength(0);
    expect(within(section).getByText('支援 Markdown；輸入 @ 提及成員')).toBeInTheDocument();
  });

  it('專案已封存：沒有按鈕，貼上說明原因', async () => {
    // Arrange
    const archived = smkDetail({ project: { ...smkProject, archivedAt: '2026-11-15T00:00:00Z' } });
    const { section, editor } = await openItem(archived);

    // Act
    pasteFiles(editor, [screenshot()]);

    // Assert
    expect(within(section).queryByRole('button', { name: '插入圖片或檔案' })).not.toBeInTheDocument();
    expect(await within(section).findByText(NO_UPLOAD_PERMISSION, { selector: 'div' })).toBeInTheDocument();
    expect(uploads).toHaveLength(0);
  });
});

describe('留言框：上傳中的記號被刪掉', () => {
  it('使用者刪掉「上傳中」的記號：上傳中止、送出恢復，完成時也不補回', async () => {
    // Arrange
    const { section, editor, user } = await openItem();
    pasteFiles(editor, [screenshot()]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    expect(within(section).getByRole('button', { name: '送出留言' })).toHaveAttribute('aria-disabled', 'true');

    // Act
    tiptapOf(editor).commands.clearContent();
    await typeInEditor(user, editor, '改寫了');

    // Assert
    await waitFor(() => {
      expect(uploads[0]?.isAborted).toBe(true);
    });
    expect(within(section).getByRole('button', { name: '送出留言' })).not.toHaveAttribute('aria-disabled');
    uploads[0]?.respond(201, pngAttachment());
    await expectDraft('改寫了');
    expect(editorText(editor)).toBe('改寫了');
  });

  it('只刪掉其中一個記號：另一個照傳、照換', async () => {
    // Arrange
    const { editor } = await openItem();
    pasteFiles(editor, [imageFile('a.png'), imageFile('b.png')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });
    const tiptap = tiptapOf(editor);
    const first: { range: { from: number; to: number } | null } = { range: null };
    tiptap.state.doc.descendants((node, position) => {
      if (first.range === null && node.type.name === 'uploadPlaceholder' && node.attrs.name === 'a.png') first.range = { from: position, to: position + node.nodeSize };
    });

    // Act
    if (first.range !== null) tiptap.commands.deleteRange(first.range);

    // Assert
    await waitFor(() => {
      expect(uploads[0]?.isAborted).toBe(true);
    });
    expect(uploads[1]?.isAborted).toBe(false);
    uploads[1]?.respond(201, pngNamed(2, 'b.png'));
    await waitFor(() => {
      expect(sessionStorage.getItem(DRAFT_KEY)).toContain(`![b.png](attachment:${attachment(2).id})`);
    });
    expect(sessionStorage.getItem(DRAFT_KEY)).not.toContain('上傳中');
  });
});

describe('留言框：還原的草稿裡有「上傳中」的記號', () => {
  it('掛上時拿掉，說明有沒傳完的檔案已經移除', async () => {
    // Arrange
    sessionStorage.setItem(DRAFT_KEY, '看這個\n![上傳中：a.png…]()\n結尾');

    // Act
    const { section, editor } = await openItem();

    // Assert
    expect(editor).toHaveTextContent('看這個');
    expect(editor).toHaveTextContent('結尾');
    expect(within(editor).queryByText(/上傳中/)).not.toBeInTheDocument();
    await waitFor(() => {
      expect(sessionStorage.getItem(DRAFT_KEY)).not.toContain('上傳中');
    });
    expect(await within(section).findByText('有沒傳完的檔案已經移除，請重新加入。', { selector: 'div' })).toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '送出留言' })).not.toHaveAttribute('aria-disabled');
  });
});

describe('留言框：換字時的游標', () => {
  it('記號換成圖片之後，游標還在原本那個字的後面，繼續打字不會跑到最後', async () => {
    // Arrange
    const { editor, user } = await openItem();
    pasteFiles(editor, [screenshot()]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    await user.click(editor);
    tiptapOf(editor).commands.insertContentAt(tiptapOf(editor).state.doc.content.size - 1, '後面接著打');
    tiptapOf(editor).commands.setTextSelection(positionOf(editor, '後面接著打') + 1);

    // Act
    uploads[0]?.respond(201, pngAttachment());
    await expectDraft(`![截圖.png](attachment:${IMAGE_ID})後面接著打`);
    await user.keyboard('X');

    // Assert
    await expectDraft(`![截圖.png](attachment:${IMAGE_ID})後X面接著打`);
  });

  it('輸入法組字中不換字，組字結束才換', async () => {
    // Arrange
    const { section, editor } = await openItem();
    pasteFiles(editor, [screenshot()]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    fireEvent.compositionStart(editor);

    // Act
    uploads[0]?.respond(201, pngAttachment());

    // Assert
    await waitFor(() => {
      expect(statusTexts(section)).toContain('上傳完成：截圖.png');
    });
    expect(sessionStorage.getItem(DRAFT_KEY)).toBe('![上傳中：截圖.png…]()');
    fireEvent.compositionEnd(editor);
    await expectDraft(`![截圖.png](attachment:${IMAGE_ID})`);
  });
});

describe('留言框：失敗的訊息與同名的檔案', () => {
  it('同名的檔案記號分得開（第二個帶序號），各自換成自己的參照', async () => {
    // Arrange
    const { editor } = await openItem();

    // Act
    pasteFiles(editor, [imageFile('a.png'), imageFile('a.png')]);

    // Assert
    await expectDraft('![上傳中：a.png…]()\n![上傳中：a.png（2）…]()');
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });
    uploads[1]?.respond(201, pngNamed(2, 'a.png'));
    await expectDraft(`![上傳中：a.png…]()\n![a.png](attachment:${attachment(2).id})`);
  });

  it('失敗的訊息累加，下一次貼上不蓋掉；由使用者關閉', async () => {
    // Arrange
    const { section, editor, user } = await openItem();
    pasteFiles(editor, [imageFile('a.png')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    uploads[0]?.respondFailure(409, 'attachment.limit_reached', '壞了');
    await within(section).findByText('a.png 上傳失敗：壞了', { selector: 'div,li' });

    // Act
    pasteFiles(editor, [aFile('大.pdf', 26 * MB)]);

    // Assert
    expect(await within(section).findByText('超過 25 MB：大.pdf', { selector: 'li' })).toBeInTheDocument();
    expect(within(section).getByText('a.png 上傳失敗：壞了', { selector: 'li' })).toBeInTheDocument();
    await user.click(within(section).getByRole('button', { name: '知道了（檔案的問題）' }));
    expect(within(section).queryByText('超過 25 MB：大.pdf', { selector: 'li' })).not.toBeInTheDocument();
  });
});

describe('留言框：貼上的內容', () => {
  it('同時有文字與檔案（試算表、網頁複製）：照常貼文字，不攔、不上傳', async () => {
    // Arrange
    const { editor } = await openItem();
    const event = new Event('paste', { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'clipboardData', { value: { files: [screenshot()], types: ['text/plain', 'Files'], getData: (type: string) => (type === 'text/plain' ? '儲存格' : '') } });

    // Act
    editor.dispatchEvent(event);

    // Assert
    expect(uploads).toHaveLength(0);
  });

  it('貼上的截圖（image.png）改名成「截圖 年-月-日 時.分.png」，上傳與記號都用新名字，同一分鐘加序號', async () => {
    // Arrange
    vi.useFakeTimers({ toFake: ['Date'], now: new Date(2026, 10, 19, 14, 5) });
    try {
      const { editor } = await openItem();

      // Act
      pasteFiles(editor, [imageFile('image.png'), imageFile('image.png')]);

      // Assert
      await expectDraft('![上傳中：截圖 2026-11-19 14.05.png…]()\n![上傳中：截圖 2026-11-19 14.05 (2).png…]()');
      await waitFor(() => {
        expect(uploads).toHaveLength(2);
      });
      expect(uploads.map((upload) => upload.file?.name)).toEqual(['截圖 2026-11-19 14.05.png', '截圖 2026-11-19 14.05 (2).png']);
    } finally {
      vi.useRealTimers();
    }
  });

  it('文字太長放不下參照（參照比記號長）：先擋下，不等送出才 422', async () => {
    // Arrange
    const marker = '![上傳中：a.png…]()';
    sessionStorage.setItem(DRAFT_KEY, 'x'.repeat(10_000 - marker.length - 10));
    const { section, editor } = await openItem();

    // Act
    pasteFiles(editor, [imageFile('a.png')]);

    // Assert
    expect(await within(section).findByText('文字太長，放不下檔案的參照。', { selector: 'div' })).toBeInTheDocument();
    expect(uploads).toHaveLength(0);
  });
});

describe('留言框：報讀', () => {
  it('開始上傳唸「開始上傳 N 個檔案」；幾個一起完成合成一句', async () => {
    // Arrange
    const { section, editor } = await openItem();

    // Act
    pasteFiles(editor, [imageFile('a.png'), imageFile('b.png')]);

    // Assert
    await waitFor(() => {
      expect(statusTexts(section)).toContain('開始上傳 2 個檔案');
    });
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });
    uploads[0]?.respond(201, pngNamed(1, 'a.png'));
    uploads[1]?.respond(201, pngNamed(2, 'b.png'));
    await waitFor(() => {
      expect(statusTexts(section)).toContain('上傳完成：a.png。上傳完成：b.png');
    });
  });
});

describe('留言框：送出中', () => {
  it('送出中（編輯器唯讀）貼上或拖進檔案一律忽略', async () => {
    // Arrange
    const { section, editor, user } = await openItem();
    server.use(http.post('/v1/items/SMK-7/comments', () => new Promise(() => undefined)));
    await typeInEditor(user, editor, '送出的字');
    await user.click(within(section).getByRole('button', { name: '送出留言' }));
    await waitFor(() => {
      expect(editor).toHaveAttribute('aria-readonly', 'true');
    });

    // Act
    pasteFiles(editor, [screenshot()]);
    dropFiles(editor, [screenshot()]);

    // Assert
    expect(uploads).toHaveLength(0);
    expect(editorText(editor)).toBe('送出的字');
  });
});
