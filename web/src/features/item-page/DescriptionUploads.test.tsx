import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { aFile, attachment, installFakeUploads, serveAttachments, type FakeUpload } from '../../test/attachments';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { descriptionRevisionOf, detailOf, serveItem, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { findEditor, typeInEditor } from '../../test/richText';

const IMAGE_ID = attachment(1).id;
const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }), '原本的說明');

let uploads: readonly FakeUpload[];

beforeEach(() => {
  uploads = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

async function openDescriptionEditor() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(task);
  serveActivity(task.key, []);
  serveAttachments(task.key, []);
  const patches = servePatch(task);
  const view = renderApp('/projects/SMK/items/SMK-7');
  const section = await screen.findByRole('region', { name: '說明' });
  await view.user.click(within(section).getByRole('button', { name: '編輯說明' }));
  const editor = await findEditor('說明', section);
  return { ...view, section, editor, patches };
}

function pasteFiles(target: HTMLElement, files: File[]) {
  fireEvent.paste(target, { clipboardData: { files, types: ['Files'], getData: () => '' } });
}

const screenshot = () => aFile('截圖.png', 2048, 'image/png');
const pngAttachment = () => attachment(1, { fileName: '截圖.png', contentType: 'image/png', sizeBytes: 2048 });

describe('說明的編輯器：貼圖與附加檔案', () => {
  it('貼上圖片：上傳中儲存停用並說明原因；完成後換成圖片', async () => {
    // Arrange
    const { section, editor, user } = await openDescriptionEditor();
    await typeInEditor(user, editor, '，看這個：');

    // Act
    pasteFiles(editor, [screenshot()]);

    // Assert
    expect(await within(section).findByText('上傳中：截圖.png…')).toBeInTheDocument();
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    const save = within(section).getByRole('button', { name: '儲存' });
    expect(save).toHaveAttribute('aria-disabled', 'true');
    expect(save).toHaveAccessibleDescription('還有檔案在上傳');
    await user.click(save);
    expect(within(section).getByRole('textbox', { name: '說明' })).toBeInTheDocument();

    uploads[0]?.respond(201, pngAttachment());

    expect(await within(section).findByRole('img', { name: '截圖.png' })).toBeInTheDocument();
    expect(within(section).queryByText(/上傳中/)).not.toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '儲存' })).not.toHaveAttribute('aria-disabled');
    expect(within(section).getByRole('button', { name: '插入圖片或檔案' })).toBeInTheDocument();
  });

  it('貼圖完成後儲存：送出的說明有 ![檔名](attachment:編號)', async () => {
    // Arrange
    const { section, editor, user, patches } = await openDescriptionEditor();
    await typeInEditor(user, editor, '，看這個：');
    pasteFiles(editor, [screenshot()]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });
    uploads[0]?.respond(201, pngAttachment());
    await within(section).findByRole('img', { name: '截圖.png' });

    // Act
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(patches).toHaveLength(1);
    });
    expect(patches[0]?.body).toEqual({
      description: `原本的說明，看這個：\n![截圖.png](attachment:${IMAGE_ID})`,
      expectedDescriptionRevision: descriptionRevisionOf('原本的說明'),
    });
  });

  it('還在上傳時 Ctrl+Enter 也不儲存', async () => {
    // Arrange
    const { editor, user, patches } = await openDescriptionEditor();
    pasteFiles(editor, [screenshot()]);
    await waitFor(() => {
      expect(uploads).toHaveLength(1);
    });

    // Act
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert：不存，並且用即時區域說原因
    expect(patches).toHaveLength(0);
    await waitFor(() => {
      expect(screen.getAllByRole('status').map((live) => live.textContent)).toContain('還有檔案在上傳');
    });
  });
});
