import { act, fireEvent, renderHook, screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { aFile, installFakeUploads, type FakeUpload } from '../../test/attachments';
import { makeConversationSummary, makeMessage, serveCreateConversation, serveReply } from '../../test/assistant';
import { server } from '../../test/server';
import { currentPath } from '../../test/renderApp';
import { findInput, localEngineOptions, openAssistant, typeMessage, uploadedAs } from './assistantTestKit';
import { setSendKey, useSendKey } from './sendKey';

let uploads: readonly FakeUpload[];

beforeEach(() => {
  uploads = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

type View = ReturnType<typeof openAssistant>;

async function pick(user: View['user'], files: File[]) {
  await findInput();
  await user.upload(screen.getByLabelText('選擇附件檔案'), files);
}

async function finishUpload(index: number, name: string, overrides: Parameters<typeof uploadedAs>[1] = {}) {
  await waitFor(() => {
    expect(uploads.length).toBeGreaterThan(index);
  });
  uploads[index]?.respond(201, uploadedAs(name, overrides));
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

describe('助理的附件小卡：縮圖', () => {
  it('上傳完成的圖片用伺服器的下載網址當縮圖（不用 blob:）；上傳中是檔案圖示', async () => {
    // Arrange
    const { user, container } = openAssistant();
    await pick(user, [aFile('截圖.png', 1000, 'image/png')]);
    await screen.findByText('上傳中…');

    // Assert
    expect(container.querySelector('li img')).toBeNull();

    // Act
    await finishUpload(0, '截圖.png');

    // Assert
    await waitFor(() => {
      expect(container.querySelector('li img')?.getAttribute('src')).toBe('/v1/projects/SMK/assistant/attachments/att-%E6%88%AA%E5%9C%96.png/content');
    });
    expect(container.innerHTML).not.toContain('blob:');
  });

  it('文字檔是檔案圖示，沒有 img', async () => {
    // Arrange
    const { user, container } = openAssistant();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);

    // Act
    await finishUpload(0, '筆記.md');
    await screen.findByText('2.0 KB');

    // Assert
    expect(container.querySelector('li img')).toBeNull();
  });
});

describe('助理的附件小卡：失敗的原因與重試', () => {
  it.each([
    ['422 類型不收', 422, 'ai.attachment_type_not_allowed', '只收圖片、PDF 與文字檔。'],
    ['422 檔名不合法', 422, 'ai.attachment_name_invalid', '檔名不合法。'],
    ['413 太大', 413, 'ai.attachment_too_large', '檔案太大。'],
  ])('%s：完整的原因列在下面，沒有重試、只能移除', async (_name, status, code, message) => {
    // Arrange
    const { user } = openAssistant();
    await pick(user, [aFile('報告.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    uploads[0]?.respondFailure(status, code, message);

    // Assert
    expect(await screen.findByText(`報告.pdf：${message}`)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /重試上傳/ })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '移除附件 報告.pdf' })).toBeEnabled();
  });

  it.each([
    ['5xx', () => uploads[0]?.respondFailure(502, 'x', 'bad gateway')],
    ['429', () => uploads[0]?.respondFailure(429, 'x', 'slow down')],
    ['連不上', () => uploads[0]?.failNetwork()],
  ])('%s：有重試', async (_name, fail) => {
    // Arrange
    const { user } = openAssistant();
    await pick(user, [aFile('報告.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    fail();

    // Assert
    expect(await screen.findByRole('button', { name: '重試上傳 報告.pdf' })).toBeEnabled();
  });

  it('重試之後焦點留在那張小卡的 ×', async () => {
    // Arrange
    const { user } = openAssistant();
    await pick(user, [aFile('報告.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    uploads[0]?.failNetwork();

    // Act
    await user.click(await screen.findByRole('button', { name: '重試上傳 報告.pdf' }));

    // Assert
    await screen.findByText('上傳中…');
    expect(screen.getByRole('button', { name: '移除附件 報告.pdf' })).toHaveFocus();
  });
});

describe('助理的附件小卡：焦點', () => {
  async function threeReady(user: View['user']) {
    await pick(user, [aFile('甲.md', 10, 'text/markdown'), aFile('乙.md', 10, 'text/markdown'), aFile('丙.md', 10, 'text/markdown')]);
    await finishUpload(0, '甲.md');
    await finishUpload(1, '乙.md');
    await finishUpload(2, '丙.md');
    await waitFor(() => {
      expect(screen.getAllByText('2.0 KB')).toHaveLength(3);
    });
  }

  it('移除中間那張：焦點到下一張的 ×；移除最後一張：到前一張；全部移除：到文字框', async () => {
    // Arrange
    deleteSpy();
    const { user } = openAssistant();
    await threeReady(user);

    // Act
    await user.click(screen.getByRole('button', { name: '移除附件 乙.md' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByText('乙.md')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: '移除附件 丙.md' })).toHaveFocus();

    // Act
    await user.click(screen.getByRole('button', { name: '移除附件 丙.md' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByText('丙.md')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: '移除附件 甲.md' })).toHaveFocus();

    // Act
    await user.click(screen.getByRole('button', { name: '移除附件 甲.md' }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('list', { name: '附件' })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('textbox', { name: '訊息' })).toHaveFocus();
  });
});

describe('助理的附件小卡：送出中', () => {
  it('送出到伺服器收下之間：× 與重試停用；小卡跟著剛開的對話過去，收下之後清掉', async () => {
    // Arrange
    serveCreateConversation('SMK', makeConversationSummary({ id: 'conv-new' }));
    const reply = serveReply('SMK', 'conv-new', () => undefined);
    const { user, router } = openAssistant();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown'), aFile('報告.pdf')]);
    await finishUpload(0, '筆記.md');
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });
    uploads[1]?.failNetwork();
    await screen.findByRole('button', { name: '重試上傳 報告.pdf' });
    await user.click(screen.getByRole('button', { name: '移除附件 報告.pdf' }));
    await typeMessage(user, '看一下');

    // Act
    await user.click(screen.getByRole('button', { name: '送出' }));

    // Assert：請求已送出、還沒收下
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });
    await waitFor(() => {
      expect(currentPath(router)).toBe('/projects/SMK/assistant/conv-new');
    });
    expect(screen.getByRole('button', { name: '移除附件 筆記.md' })).toBeDisabled();

    // Act：伺服器收下
    act(() => {
      reply.stream()?.event('start', { userMessage: makeMessage(), assistantMessageId: 'a-1' });
    });

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('list', { name: '附件' })).not.toBeInTheDocument();
    });
    reply.stream()?.close();
  });

  it('失敗的小卡在送出中也不能重試（重試鈕停用）', async () => {
    // Arrange：失敗的小卡擋住送出，所以直接驗證停用狀態的來源：送出中 isLocked 套到重試鈕上
    serveCreateConversation('SMK', makeConversationSummary({ id: 'conv-new' }));
    const reply = serveReply('SMK', 'conv-new', () => undefined);
    const { user } = openAssistant();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    await finishUpload(0, '筆記.md');
    await typeMessage(user, '看一下');
    await user.click(screen.getByRole('button', { name: '送出' }));
    await waitFor(() => {
      expect(reply.sent).toHaveLength(1);
    });

    // Act：送出中又選了一個會失敗的檔案
    await user.upload(screen.getByLabelText('選擇附件檔案'), [aFile('補充.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });
    uploads[1]?.failNetwork();

    // Assert
    expect(await screen.findByRole('button', { name: '重試上傳 補充.pdf' })).toBeDisabled();
    reply.stream()?.close();
  });
});

describe('助理的附件：離開輸入框', () => {
  it('離開頁面（卸載）：已上傳還沒送出的附件刪掉；只是換對話不刪', async () => {
    // Arrange
    const deleted = deleteSpy();
    server.use(http.get('/v1/projects/SMK/assistant/conversations/conv-2', () => new Response(JSON.stringify({ success: true, data: { conversation: makeConversationSummary({ id: 'conv-2' }), messages: [] }, error: null }), { headers: { 'Content-Type': 'application/json' } })));
    const { user, router } = openAssistant();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    await finishUpload(0, '筆記.md');
    await screen.findByText('2.0 KB');

    // Act：換對話
    await act(async () => {
      await router.navigate('/projects/SMK/assistant/conv-2');
    });
    await findInput();

    // Assert
    expect(deleted).not.toContain('att-筆記.md');

    // Act：離開助理頁
    await act(async () => {
      await router.navigate('/projects/SMK');
    });

    // Assert
    await waitFor(() => {
      expect(deleted).toContain('att-筆記.md');
    });
  });

  it('整頁離開（pagehide）：已上傳還沒送出的附件刪掉；放進往返快取的不刪', async () => {
    // Arrange
    const deleted = deleteSpy();
    const { user } = openAssistant();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    await finishUpload(0, '筆記.md');
    await screen.findByText('2.0 KB');

    // Act：放進往返快取（之後會原樣回來）
    act(() => {
      window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
    });

    // Assert（只看這個測試自己的檔案：前一個測試卸載時送出的刪除可能晚一點才到）
    expect(deleted).not.toContain('att-筆記.md');

    // Act：真的離開
    act(() => {
      window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }));
    });

    // Assert
    await waitFor(() => {
      expect(deleted).toContain('att-筆記.md');
    });
  });

  it('還在傳的時候離開：拿到伺服器的回應就刪', async () => {
    // Arrange
    const deleted = deleteSpy();
    const { user, router } = openAssistant();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    await screen.findByText('上傳中…');

    // Act
    await act(async () => {
      await router.navigate('/projects/SMK');
    });
    expect(uploads[0]?.isAborted).toBe(false);
    uploads[0]?.respond(201, uploadedAs('筆記.md'));

    // Assert
    await waitFor(() => {
      expect(deleted).toContain('att-筆記.md');
    });
  });
});

describe('助理的附件：報讀', () => {
  it('上傳完成與失敗由同一個 role="status" 報讀', async () => {
    // Arrange
    const { user } = openAssistant();
    await pick(user, [aFile('筆記.md', 100, 'text/markdown')]);
    const live = () => screen.getAllByRole('status').find((node) => node.classList.contains('visually-hidden'));

    // Act
    await finishUpload(0, '筆記.md');

    // Assert
    await waitFor(() => {
      expect(live()).toHaveTextContent('已上傳 筆記.md');
    });

    // Act
    await user.upload(screen.getByLabelText('選擇附件檔案'), [aFile('報告.pdf')]);
    await waitFor(() => {
      expect(uploads).toHaveLength(2);
    });
    uploads[1]?.respondFailure(422, 'x', '這種檔案不收。');

    // Assert
    await waitFor(() => {
      expect(live()).toHaveTextContent('報告.pdf 上傳失敗：這種檔案不收。');
    });
  });

  it('「Codex 讀不到 PDF」也報讀', async () => {
    // Arrange
    const { user } = openAssistant('/projects/SMK/assistant', localEngineOptions('codex'));
    await pick(user, [aFile('規格.pdf', 1000, 'application/pdf')]);

    // Act
    await finishUpload(0, '規格.pdf');

    // Assert
    await waitFor(() => {
      const live = screen.getAllByRole('status').find((node) => node.classList.contains('visually-hidden'));
      expect(live).toHaveTextContent('Codex 讀不到 PDF');
    });
  });
});

describe('助理的輸入框：拖曳', () => {
  it('游標在輸入框裡移動（進到子元素、dragleave 沒有 relatedTarget）時外框不閃；真的離開才取消', async () => {
    // Arrange
    openAssistant();
    const input = await findInput();
    const form = input.closest('form');
    if (form === null) throw new Error('找不到輸入框的表單');
    const dataTransfer = { types: ['Files'], files: [] };

    // Act：進到表單、再進到文字框，離開表單（Safari：沒有 relatedTarget）
    fireEvent.dragEnter(form, { dataTransfer });
    fireEvent.dragEnter(input, { dataTransfer });
    fireEvent.dragLeave(form, { dataTransfer, relatedTarget: null });

    // Assert
    expect(input.closest('[data-dragging="true"]')).not.toBeNull();

    // Act
    fireEvent.dragLeave(input, { dataTransfer, relatedTarget: null });

    // Assert
    expect(input.closest('[data-dragging]')).toBeNull();
  });
});

describe('助理的送出按法：偏好同步', () => {
  it('兩個輸入框同時掛著：一邊改了，另一邊跟著變', () => {
    // Arrange
    setSendKey('enter');
    const first = renderHook(() => useSendKey());
    const second = renderHook(() => useSendKey());

    // Act
    act(() => {
      setSendKey('mod-enter');
    });

    // Assert
    expect(first.result.current).toBe('mod-enter');
    expect(second.result.current).toBe('mod-enter');
    act(() => {
      setSendKey('enter');
    });
  });
});

describe('助理的送出按法：選單鈕', () => {
  it('送出的按法選單鈕有下拉箭頭（和模型選單一致）', async () => {
    // Arrange
    openAssistant();

    // Act
    const trigger = await screen.findByRole('combobox', { name: '送出的按法' });

    // Assert
    expect(within(trigger).getByText('Enter 送出')).toBeInTheDocument();
    expect(trigger.querySelector('svg')).not.toBeNull();
  });
});
