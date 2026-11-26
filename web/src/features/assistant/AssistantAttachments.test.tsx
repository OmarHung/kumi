import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CSRF_HEADER } from '../../api/http';
import { aFile, installFakeUploads, type FakeUpload } from '../../test/attachments';
import { CLAUDE_SELECTION, completeReply, makeConversationSummary, makeMessage, rejectReply, serveConversation, serveCreateConversation, serveReply } from '../../test/assistant';
import { ok } from '../../test/api';
import { server } from '../../test/server';
import { findInput, localEngineOptions, openAssistant, typeMessage, uploadedAs } from './assistantTestKit';

const NEW = makeConversationSummary({ id: 'conv-new' });
const MIB = 1024 * 1024;

let uploads: readonly FakeUpload[];

beforeEach(() => {
  uploads = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function view() {
  return openAssistant();
}

async function pick(user: ReturnType<typeof openAssistant>['user'], files: File[]) {
  await findInput();
  await user.upload(screen.getByLabelText('選擇附件檔案'), files);
}

/** 第 index 個上傳收到伺服器的回應。 */
async function finishUpload(index: number, name: string) {
  await waitFor(() => {
    expect(uploads.length).toBeGreaterThan(index);
  });
  uploads[index]?.respond(201, uploadedAs(name));
}

function deleteSpy() {
  const deleted: string[] = [];
  server.use(
    http.delete('/v1/projects/SMK/assistant/attachments/:id', ({ params }) => {
      deleted.push(String(params.id));
      return new Response(null, { status: 204 });
    }),
  );
  return deleted;
}

describe('助理：附加檔案', () => {
  it('選檔：立刻上傳到助理的附件端點（file 欄位、CSRF 標頭），上傳中有狀態，收下後顯示大小', async () => {
    // Arrange
    const { user } = view();

    // Act
    await pick(user, [aFile('規格.pdf', 2048)]);

    // Assert
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    expect(uploads[0]?.url).toBe('/v1/projects/SMK/assistant/attachments');
    expect(uploads[0]?.file?.name).toBe('規格.pdf');
    expect(uploads[0]?.headers.get(CSRF_HEADER)).toBe('1');
    expect(await screen.findByText('上傳中…')).toBeInTheDocument();
    uploads[0]?.respond(201, uploadedAs('規格.pdf'));
    expect(await screen.findByText('2.0 KB')).toBeInTheDocument();
  });

  it('只有附件、沒有字也能送出；帶的是上傳時拿到的編號；伺服器收下之後才清掉小卡', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    const reply = serveReply('SMK', 'conv-new', (stream) => {
      completeReply(stream, '看過了');
    });
    const { user } = view();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    await finishUpload(0, '筆記.md');
    const send = await screen.findByRole('button', { name: '送出' });
    await waitFor(() => {
      expect(send).toBeEnabled();
    });

    // Act
    await user.click(send);

    // Assert
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    expect(reply.sent[0]?.body).toEqual({ text: '', selection: CLAUDE_SELECTION, itemKey: null, attachmentIds: ['att-筆記.md'] });
    await waitFor(() => {
      expect(screen.queryByRole('list', { name: '附件' })).not.toBeInTheDocument();
    });
  });

  it('上傳中不能送出；全部上傳好才可以', async () => {
    // Arrange
    const { user } = view();
    await typeMessage(user, '看一下');
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    await screen.findByText('上傳中…');

    // Assert
    expect(screen.getByRole('button', { name: '送出' })).toBeDisabled();

    // Act
    await finishUpload(0, '筆記.md');

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '送出' })).toBeEnabled();
    });
  });

  it('送出之前就失敗（伺服器沒收下）：字與小卡都留著', async () => {
    // Arrange
    serveCreateConversation('SMK', NEW);
    rejectReply('SMK', 'conv-new', 422, 'ai.attachment_not_found', '找不到附件。');
    const { user } = view();
    await typeMessage(user, '看一下');
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    await finishUpload(0, '筆記.md');
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '送出' })).toBeEnabled();
    });

    // Act
    await user.click(screen.getByRole('button', { name: '送出' }));

    // Assert
    expect(await screen.findByText('找不到附件。')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: '訊息' })).toHaveValue('看一下');
    expect(screen.getByRole('list', { name: '附件' })).toBeInTheDocument();
    expect(screen.getByText('筆記.md')).toBeInTheDocument();
  });

  it('上傳失敗（伺服器暫時出錯）：原因完整列在小卡列下面、有重試，不能送出；重試成功後可以', async () => {
    // Arrange
    const { user } = view();
    await typeMessage(user, '看一下');
    await pick(user, [aFile('報告.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    uploads[0]?.respondFailure(503, 'server.unavailable', '暫時不能用。');

    // Assert
    expect(await screen.findByText('報告.pdf：伺服器暫時無法處理，請稍後再試。')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '送出' })).toBeDisabled();

    // Act
    await user.click(screen.getByRole('button', { name: '重試上傳 報告.pdf' }));
    await finishUpload(1, '報告.pdf');

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '送出' })).toBeEnabled();
    });
  });

  it('移除已上傳的：呼叫 DELETE，小卡不見', async () => {
    // Arrange
    const deleted = deleteSpy();
    const { user } = view();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    await finishUpload(0, '筆記.md');
    await screen.findByText('2.0 KB');

    // Act
    await user.click(screen.getByRole('button', { name: '移除附件 筆記.md' }));

    // Assert（前一個測試離開時的清理刪除可能晚到，所以只看有沒有這一個）
    await waitFor(() => {
      expect(deleted).toContain('att-筆記.md');
    });
    expect(screen.queryByText('筆記.md')).not.toBeInTheDocument();
  });

  it('移除上傳中的：中止請求、不呼叫 DELETE', async () => {
    // Arrange
    const deleted = deleteSpy();
    const { user } = view();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    await screen.findByText('上傳中…');

    // Act
    await user.click(screen.getByRole('button', { name: '移除附件 筆記.md' }));

    // Assert
    expect(uploads[0]?.isAborted).toBe(true);
    expect(deleted).not.toContain('att-筆記.md');
    expect(screen.queryByText('筆記.md')).not.toBeInTheDocument();
  });

  it('前端先擋：第 6 個超過數量、太大的圖片不上傳，小卡說明原因且不能送出', async () => {
    // Arrange
    const { user } = view();
    const files = [1, 2, 3, 4, 5].map((n) => aFile(`筆記${String(n)}.txt`, 100, 'text/plain'));

    // Act
    await pick(user, [...files, aFile('第六個.txt', 100, 'text/plain'), aFile('大圖.png', 6 * MIB, 'image/png')]);

    // Assert
    const problems = await screen.findByRole('list', { name: '附件的問題' });
    expect(within(problems).getAllByRole('listitem').map((row) => row.textContent)).toEqual(['第六個.txt：一則訊息最多 5 個附件。', '大圖.png：一則訊息最多 5 個附件。']);
    expect(uploads).toHaveLength(5);
    expect(screen.getByRole('button', { name: '送出' })).toBeDisabled();
  });

  it('太大的圖片：說明超過 5.0 MB，不上傳', async () => {
    // Arrange
    const { user } = view();

    // Act
    await pick(user, [aFile('大圖.png', 6 * MIB, 'image/png')]);

    // Assert
    expect(await screen.findByText('大圖.png：檔案超過 5.0 MB。')).toBeInTheDocument();
    expect(uploads).toHaveLength(0);
  });

  it('貼上圖片：沒有檔名的叫「貼上的圖片.png」並上傳；貼上文字照常', async () => {
    // Arrange
    view();
    const input = await findInput();
    const image = new File(['x'], '', { type: 'image/png' });

    // Act
    fireEvent.paste(input, { clipboardData: { files: [image], types: ['Files'] } });

    // Assert
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    expect(uploads[0]?.file?.name).toBe('貼上的圖片.png');

    // Act
    fireEvent.paste(input, { clipboardData: { files: [], types: ['text/plain'] } });

    // Assert
    expect(uploads).toHaveLength(1);
  });

  it('拖進來：外框換成虛線，放開就上傳；沒有檔案的拖曳不理', async () => {
    // Arrange
    view();
    const input = await findInput();
    const form = input.closest('form');
    if (form === null) throw new Error('找不到輸入框的表單');
    const file = aFile('筆記.md', 100, 'text/markdown');

    // Act
    fireEvent.dragEnter(form, { dataTransfer: { types: ['text/plain'], files: [] } });
    expect(input.closest('[data-dragging]')).toBeNull();
    fireEvent.dragEnter(form, { dataTransfer: { types: ['Files'], files: [file] } });

    // Assert
    expect(input.closest('[data-dragging="true"]')).not.toBeNull();

    // Act
    fireEvent.drop(form, { dataTransfer: { types: ['Files'], files: [file] } });

    // Assert
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    expect(input.closest('[data-dragging]')).toBeNull();
  });

  it('附件跟著這個對話的草稿：從「新的對話」送出之前選的，不會出現在別的對話', async () => {
    // Arrange
    const { user, router } = view();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    await finishUpload(0, '筆記.md');
    await screen.findByText('2.0 KB');

    // Act：換到另一則對話
    server.use(http.get('/v1/projects/SMK/assistant/conversations/conv-2', () => ok({ conversation: makeConversationSummary({ id: 'conv-2' }), messages: [] })));
    await act(async () => {
      await router.navigate('/projects/SMK/assistant/conv-2');
    });

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('list', { name: '附件' })).not.toBeInTheDocument();
    });
  });
});

describe('助理：本機與主機的引擎讀得到哪些附件', () => {
  async function addAndFinish(user: ReturnType<typeof openAssistant>['user'], name: string, size: number, type: string, index = 0) {
    await pick(user, [aFile(name, size, type)]);
    await finishUpload(index, name);
  }

  it('Claude Code：圖片與 PDF 都可以送出，沒有說明', async () => {
    // Arrange
    const { user } = openAssistant('/projects/SMK/assistant', localEngineOptions('claude_code'));
    await addAndFinish(user, '截圖.png', 1000, 'image/png');
    await addAndFinish(user, '規格.pdf', 1000, 'application/pdf', 1);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '送出' })).toBeEnabled();
    });
    expect(screen.queryByText(/讀不到/)).not.toBeInTheDocument();
  });

  it.each([
    ['本機', false, 'Example-PC'],
    ['主機', true, '主機上的來源'],
  ])('Codex（%s）：PDF 的小卡標「Codex 讀不到 PDF」、送出停用、說明寫出來源與做法；圖片與文字檔照常', async (_label, hosted, sourceName) => {
    // Arrange
    const { user } = openAssistant('/projects/SMK/assistant', localEngineOptions('codex', hosted));
    await addAndFinish(user, '規格.pdf', 1000, 'application/pdf');

    // Assert
    expect(await screen.findByText(`你選的「${sourceName}」讀不到 PDF：移除它，或改選 Claude Code。`)).toBeInTheDocument();
    expect(screen.getByText('Codex 讀不到 PDF')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '送出' })).toBeDisabled();

    // Act
    await user.click(screen.getByRole('button', { name: '移除附件 規格.pdf' }));
    await addAndFinish(user, '截圖.png', 1000, 'image/png', 1);

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '送出' })).toBeEnabled();
    });
    expect(screen.queryByText(/讀不到/)).not.toBeInTheDocument();
  });

  it('伺服器的 422（kumi 太舊）顯示在輸入框上方，字與小卡留著', async () => {
    // Arrange
    const message = '你電腦上的 kumi 是 0.9.0，要 0.10.0 以上才能交圖片與 PDF 給引擎，請執行 kumi update。';
    serveCreateConversation('SMK', NEW);
    rejectReply('SMK', 'conv-new', 422, 'ai.attachment_unsupported', message);
    const { user } = openAssistant('/projects/SMK/assistant', localEngineOptions('claude_code'));
    await typeMessage(user, '看一下');
    await addAndFinish(user, '截圖.png', 1000, 'image/png');
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '送出' })).toBeEnabled();
    });

    // Act
    await user.click(screen.getByRole('button', { name: '送出' }));

    // Assert
    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: '訊息' })).toHaveValue('看一下');
    expect(screen.getByRole('list', { name: '附件' })).toBeInTheDocument();
  });
});

describe('助理：訊息裡的附件', () => {
  it('圖片是縮圖（點了在新分頁開），其他檔案是小卡（點了下載）', async () => {
    // Arrange
    serveConversation('SMK', {
      conversation: makeConversationSummary({ id: 'conv-1', title: '舊的', messageCount: 1 }),
      messages: [
        makeMessage({
          attachments: [uploadedAs('截圖.png', { id: 'img-1' }), uploadedAs('規格.pdf', { id: 'pdf-1', sizeBytes: 3 * MIB })],
        }),
      ],
    });

    // Act
    openAssistant('/projects/SMK/assistant/conv-1');
    const list = await screen.findByRole('list', { name: '附件' });

    // Assert
    const image = within(list).getByRole('img', { name: '截圖.png' });
    expect(image).toHaveAttribute('src', '/v1/projects/SMK/assistant/attachments/img-1/content');
    expect(image).toHaveAttribute('loading', 'lazy');
    expect(image.closest('a')).toHaveAttribute('target', '_blank');
    const file = within(list).getByRole('link', { name: /規格\.pdf/ });
    expect(file).toHaveAttribute('href', '/v1/projects/SMK/assistant/attachments/pdf-1/content');
    expect(file).toHaveAttribute('download', '規格.pdf');
    expect(within(file).getByText('3.0 MB')).toBeInTheDocument();
  });

  it('只有附件、parts 是空陣列：只顯示附件，沒有空的泡泡也沒有複製鈕；SVG（kind 是 text）是檔案小卡', async () => {
    // Arrange
    serveConversation('SMK', {
      conversation: makeConversationSummary({ id: 'conv-1', title: '舊的', messageCount: 1 }),
      messages: [makeMessage({ parts: [], attachments: [uploadedAs('圖.svg', { id: 'svg-1', contentType: 'image/svg+xml', kind: 'text' })] })],
    });

    // Act
    openAssistant('/projects/SMK/assistant/conv-1');
    const list = await screen.findByRole('list', { name: '附件' });

    // Assert
    expect(within(list).queryByRole('img')).not.toBeInTheDocument();
    expect(within(list).getByRole('link', { name: /圖\.svg/ })).toHaveAttribute('download', '圖.svg');
    expect(screen.queryByRole('button', { name: '複製訊息' })).not.toBeInTheDocument();
  });
});
