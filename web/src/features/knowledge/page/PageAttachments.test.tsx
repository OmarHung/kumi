import { fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { signedInAs } from '../../../test/api';
import { aFile, installFakeUploads, type FakeUpload } from '../../../test/attachments';
import { exampleOwner } from '../../../test/fixtures';
import { knowledgeSpaceOf, serveKnowledge } from '../../../test/knowledge';
import { renderApp } from '../../../test/renderApp';
import { findEditor } from '../../../test/richText';

const IMAGE_ID = '0190a100-0000-7000-8000-000000000042';
const IMAGE_BODY = `流程圖：\n\n![架構圖.png](attachment:${IMAGE_ID})`;
const IMAGE_SRC = `/v1/knowledge/attachments/${IMAGE_ID}/content`;

let uploads: readonly FakeUpload[];

beforeEach(() => {
  uploads = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function openPage(options: Parameters<typeof serveKnowledge>[0] = {}) {
  signedInAs(exampleOwner);
  const backend = serveKnowledge({ bodies: { 'DOCS/2': IMAGE_BODY }, ...options });
  return { backend, ...renderApp('/knowledge/DOCS/2') };
}

const patchesOf = (backend: ReturnType<typeof serveKnowledge>) => backend.requests.filter((request) => request.method === 'PATCH');

async function startUploading(view: ReturnType<typeof openPage>) {
  await view.user.click(await screen.findByRole('button', { name: '編輯' }));
  const editor = await findEditor('內容');
  await view.user.type(screen.getByRole('textbox', { name: '標題' }), '新');
  pasteFiles(editor, [aFile('截圖.png', 2048, 'image/png')]);
  await waitFor(() => {
    expect(uploads).toHaveLength(1);
  });
  return editor;
}

function pasteFiles(target: HTMLElement, files: File[]) {
  fireEvent.paste(target, { clipboardData: { files, types: ['Files'], getData: () => '' } });
}

describe('頁面的附件', () => {
  it('檢視：附件的圖片從知識庫的附件網址載入，不是項目的', async () => {
    // Arrange & Act
    openPage();

    // Assert
    const image = await screen.findByRole('img', { name: '架構圖.png' });
    expect(image).toHaveAttribute('src', IMAGE_SRC);
  });

  it('編輯：貼上圖片會傳到這一頁的附件端點', async () => {
    // Arrange
    const view = openPage();
    await view.user.click(await screen.findByRole('button', { name: '編輯' }));
    const editor = await findEditor('內容');

    // Act
    pasteFiles(editor, [aFile('截圖.png', 2048, 'image/png')]);

    // Assert
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    expect(uploads[0]?.method).toBe('POST');
    expect(uploads[0]?.url).toBe('/v1/knowledge/spaces/DOCS/pages/2/attachments');
  });

  it('編輯：內容裡已有的圖片也從知識庫的附件網址載入', async () => {
    // Arrange
    const view = openPage();

    // Act
    await view.user.click(await screen.findByRole('button', { name: '編輯' }));
    await findEditor('內容');

    // Assert
    const image = await screen.findByRole('img', { name: '架構圖.png' });
    expect(image).toHaveAttribute('src', IMAGE_SRC);
  });

  it('沒有 canEdit 的人看得到圖片，也沒有編輯的入口', async () => {
    // Arrange & Act
    openPage({ spaces: [knowledgeSpaceOf({ myPermissions: { canEdit: false, canComment: false, canManage: false } })] });

    // Assert
    expect(await screen.findByRole('img', { name: '架構圖.png' })).toHaveAttribute('src', IMAGE_SRC);
    expect(screen.queryByRole('button', { name: '編輯' })).not.toBeInTheDocument();
  });

  it('編輯：上傳中「儲存」停住並說明原因，不送請求', async () => {
    // Arrange
    const view = openPage();
    await startUploading(view);
    const save = screen.getByRole('button', { name: '儲存' });

    // Act
    await view.user.click(save);

    // Assert
    expect(save).toHaveAttribute('aria-disabled', 'true');
    expect(save).toHaveAccessibleDescription('還有檔案在上傳');
    expect(await screen.findByText('上傳中：截圖.png…')).toBeInTheDocument();
    expect(patchesOf(view.backend)).toHaveLength(0);
    await waitFor(() => {
      expect(screen.getAllByRole('status').map((live) => live.textContent)).toContain('還有檔案在上傳');
    });
  });

  it('編輯：上傳中在編輯器按 Ctrl+Enter 也不儲存，用即時區域說原因', async () => {
    // Arrange
    const view = openPage();
    await startUploading(view);

    // Act
    await view.user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    expect(patchesOf(view.backend)).toHaveLength(0);
    await waitFor(() => {
      expect(screen.getAllByRole('status').map((live) => live.textContent)).toContain('還有檔案在上傳');
    });
  });

  it('編輯：上傳中在標題按 Ctrl+Enter 也不儲存，用即時區域說原因', async () => {
    // Arrange
    const view = openPage();
    await startUploading(view);
    await view.user.click(screen.getByRole('textbox', { name: '標題' }));

    // Act
    await view.user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    expect(patchesOf(view.backend)).toHaveLength(0);
    await waitFor(() => {
      expect(screen.getAllByRole('status').map((live) => live.textContent)).toContain('還有檔案在上傳');
    });
  });

  it('編輯：檔案傳不上去時，問題寫在編輯器下面', async () => {
    // Arrange
    const view = openPage();
    await startUploading(view);

    // Act
    uploads[0]?.respondFailure(413, 'attachment.too_large', '檔案太大。');

    // Assert
    expect(await screen.findByText(/檔案太大/)).toBeInTheDocument();
    expect(screen.queryByText('上傳中：截圖.png…')).not.toBeInTheDocument();
  });
});
