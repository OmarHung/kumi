import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { aFile } from '../../test/attachments';
import { createFakeUploads, editorText, findEditor, tiptapOf, typeInEditor } from '../../test/richText';
import { EditorHarness } from '../../test/richTextHarness';

const GUID = '0190a100-0000-7000-8000-000000000001';
const IMAGE_PLACEHOLDER = '![上傳中：截圖.png…]()';
const FILE_PLACEHOLDER = '[上傳中：報告.pdf…]()';

function pasteFiles(target: HTMLElement, files: File[], text = '') {
  fireEvent.paste(target, { clipboardData: { files, types: ['Files'], getData: (type: string) => (type === 'text/plain' ? text : '') } });
}

describe('編輯器：貼上與拖進來的檔案', () => {
  it('貼上檔案交給 uploads.addFiles，不貼進文件', async () => {
    const fake = createFakeUploads();
    const onValue = vi.fn();
    render(<EditorHarness uploads={fake.uploads} onValue={onValue} />);
    const editor = await findEditor('說明');
    const file = aFile('截圖.png', 2048, 'image/png');
    pasteFiles(editor, [file]);
    expect(fake.addFiles).toHaveBeenCalledWith([file]);
    expect(onValue).not.toHaveBeenCalled();
  });

  it('剪貼簿同時有文字與檔案時照常貼文字，不上傳', async () => {
    const fake = createFakeUploads();
    render(<EditorHarness uploads={fake.uploads} />);
    const editor = await findEditor('說明');
    pasteFiles(editor, [aFile('表格.png', 10, 'image/png')], '一些文字');
    expect(fake.addFiles).not.toHaveBeenCalled();
  });

  it('拖進來的檔案交給 uploads.addFiles', async () => {
    const fake = createFakeUploads();
    render(<EditorHarness uploads={fake.uploads} />);
    const editor = await findEditor('說明');
    const file = aFile('報告.pdf');
    fireEvent.drop(editor, { dataTransfer: { files: [file], types: ['Files'] } });
    expect(fake.addFiles).toHaveBeenCalledWith([file]);
  });

  it('沒給 uploads 時不接檔案', async () => {
    render(<EditorHarness />);
    const editor = await findEditor('說明');
    fireEvent.drop(editor, { dataTransfer: { files: [aFile('報告.pdf')], types: ['Files'] } });
    expect(screen.queryByText(/上傳中/)).toBeNull();
  });

  it('送出中貼上或拖進檔案一律忽略', async () => {
    const fake = createFakeUploads();
    render(<EditorHarness uploads={fake.uploads} isBusy />);
    const editor = await findEditor('說明');
    pasteFiles(editor, [aFile('截圖.png', 2048, 'image/png')]);
    fireEvent.drop(editor, { dataTransfer: { files: [aFile('報告.pdf')], types: ['Files'] } });
    expect(fake.addFiles).not.toHaveBeenCalled();
  });

  it('格式提示說可以貼上截圖或拖進檔案（能上傳時）', async () => {
    render(<EditorHarness uploads={createFakeUploads().uploads} />);
    await findEditor('說明');
    expect(screen.getByText(/可以貼上截圖，或把圖片與檔案拖進來/)).toBeInTheDocument();
  });
});

describe('編輯器：工具列的「插入圖片或檔案」', () => {
  it('能上傳時在「格式」工具列的最後面，左右鍵移得到；選的檔案交給 uploads.addFiles', async () => {
    // Arrange
    const user = userEvent.setup();
    const fake = createFakeUploads();
    render(<EditorHarness uploads={fake.uploads} />);
    await findEditor('說明');
    const toolbar = screen.getByRole('toolbar', { name: '格式' });
    const insert = within(toolbar).getByRole('button', { name: '插入圖片或檔案' });
    within(toolbar).getAllByRole('button')[0]?.focus();
    const file = aFile('報告.pdf');

    // Act
    await user.keyboard('{End}');

    // Assert
    expect(insert).toHaveFocus();
    expect(within(toolbar).getAllByRole('button').at(-1)).toBe(insert);

    // Act
    await user.upload(screen.getByLabelText('選擇要插入的圖片或檔案'), file);

    // Assert
    expect(fake.addFiles).toHaveBeenCalledWith([file]);
  });

  it('不能上傳（沒給 uploads 或沒有權限）時沒有這顆', async () => {
    // Arrange
    render(<EditorHarness uploads={createFakeUploads({ canUpload: false }).uploads} />);

    // Act
    await findEditor('說明');

    // Assert
    expect(screen.queryByRole('button', { name: '插入圖片或檔案' })).toBeNull();
  });
});

describe('編輯器：上傳的目標', () => {
  async function setup(initial = '') {
    const fake = createFakeUploads();
    const onValue = vi.fn();
    render(<EditorHarness uploads={fake.uploads} initial={initial} onValue={onValue} />);
    const editor = await findEditor('說明');
    await waitFor(() => {
      expect(fake.target()).not.toBeNull();
    });
    return { fake, onValue, editor, target: () => fake.target() };
  }

  it('insertPlaceholders：在游標的位置放記號，多個之間換行；有圖片時自成一行，寫成 Markdown 就是記號的原文', async () => {
    const user = userEvent.setup();
    const { onValue, editor, target } = await setup();
    await typeInEditor(user, editor, '看這個：');
    act(() => {
      target()?.insertPlaceholders([
        { placeholder: IMAGE_PLACEHOLDER, name: '截圖.png', isImage: true },
        { placeholder: FILE_PLACEHOLDER, name: '報告.pdf', isImage: false },
      ]);
    });
    expect(onValue).toHaveBeenLastCalledWith(`看這個：\n${IMAGE_PLACEHOLDER}\n${FILE_PLACEHOLDER}`);
    expect(await screen.findByText('上傳中：截圖.png…')).toBeInTheDocument();
    expect(await screen.findByText('上傳中：報告.pdf…')).toBeInTheDocument();
  });

  it('游標在一行的中間：圖片的記號前後各換一行，檔案的記號留在行內', async () => {
    const { onValue, editor, target } = await setup('前後');
    act(() => {
      tiptapOf(editor).commands.setTextSelection(2);
      target()?.insertPlaceholders([{ placeholder: IMAGE_PLACEHOLDER, name: '截圖.png', isImage: true }]);
    });
    expect(onValue).toHaveBeenLastCalledWith(`前\n${IMAGE_PLACEHOLDER}\n後`);
    act(() => {
      tiptapOf(editor).commands.setTextSelection(1);
      target()?.insertPlaceholders([{ placeholder: FILE_PLACEHOLDER, name: '報告.pdf', isImage: false }]);
    });
    expect(onValue).toHaveBeenLastCalledWith(`${FILE_PLACEHOLDER}前\n${IMAGE_PLACEHOLDER}\n後`);
  });

  it('replacePlaceholder：記號換成參照（圖片顯示成圖片，檔案是小卡），游標留在後面', async () => {
    const user = userEvent.setup();
    const { onValue, editor, target } = await setup();
    await typeInEditor(user, editor, '前面');
    act(() => {
      target()?.insertPlaceholders([{ placeholder: IMAGE_PLACEHOLDER, name: '截圖.png', isImage: true }]);
    });
    let found = false;
    act(() => {
      found = target()?.replacePlaceholder(IMAGE_PLACEHOLDER, `![截圖.png](attachment:${GUID})`) ?? false;
    });
    expect(found).toBe(true);
    expect(onValue).toHaveBeenLastCalledWith(`前面\n![截圖.png](attachment:${GUID})`);
    expect(await screen.findByRole('img', { name: '截圖.png' })).toHaveAttribute('src', `/v1/attachments/${GUID}/content`);
    expect(screen.queryByText(/上傳中/)).toBeNull();
    await user.keyboard('後面');
    expect(onValue).toHaveBeenLastCalledWith(`前面\n![截圖.png](attachment:${GUID})後面`);
  });

  it('replacePlaceholder 換成檔案連結：圖示加檔名的小卡', async () => {
    const { onValue, editor, target } = await setup();
    act(() => {
      target()?.insertPlaceholders([{ placeholder: FILE_PLACEHOLDER, name: '報告.pdf', isImage: false }]);
    });
    act(() => {
      target()?.replacePlaceholder(FILE_PLACEHOLDER, `[報告.pdf](attachment:${GUID})`);
    });
    expect(onValue).toHaveBeenLastCalledWith(`[報告.pdf](attachment:${GUID})`);
    expect(await screen.findByText('報告.pdf')).toBeInTheDocument();
    expect(editor.querySelector('[data-icon="filePdf"]')).not.toBeNull();
  });

  it('換成空字串就是拿掉（失敗時）；找不到記號（使用者已經刪掉）回傳 false', async () => {
    const { onValue, target } = await setup('前面');
    act(() => {
      target()?.insertPlaceholders([{ placeholder: IMAGE_PLACEHOLDER, name: '截圖.png', isImage: true }]);
    });
    let removed = false;
    let again = true;
    act(() => {
      removed = target()?.replacePlaceholder(IMAGE_PLACEHOLDER, '') ?? false;
      again = target()?.replacePlaceholder(IMAGE_PLACEHOLDER, '') ?? true;
    });
    expect(removed).toBe(true);
    expect(again).toBe(false);
    expect(onValue).toHaveBeenLastCalledWith('前面');
  });

  it('使用者刪掉記號（選取後刪掉）之後換不到，也不補回去', async () => {
    const user = userEvent.setup();
    const { editor, target } = await setup();
    await typeInEditor(user, editor, '字');
    act(() => {
      target()?.insertPlaceholders([{ placeholder: IMAGE_PLACEHOLDER, name: '截圖.png', isImage: true }]);
    });
    // 記號是整塊的小卡：選取範圍裡有它，刪掉就整塊不見（只剩前面的「字」）。
    await user.keyboard('{Shift>}{ArrowLeft}{/Shift}{Backspace}');
    expect(editor.querySelector('[data-upload-placeholder]')).toBeNull();
    let found = true;
    act(() => {
      found = target()?.replacePlaceholder(IMAGE_PLACEHOLDER, `![截圖.png](attachment:${GUID})`) ?? true;
    });
    expect(found).toBe(false);
    expect(editorText(editor)).toBe('字');
  });

  it('游標在程式碼區塊裡：記號放在區塊後面的新段落', async () => {
    const user = userEvent.setup();
    const { onValue, editor, target } = await setup('```\ncode\n```');
    await user.click(editor.querySelector('code') as HTMLElement);
    act(() => {
      target()?.insertPlaceholders([{ placeholder: IMAGE_PLACEHOLDER, name: '截圖.png', isImage: true }]);
    });
    expect(onValue).toHaveBeenLastCalledWith(`\`\`\`\ncode\n\`\`\`\n\n${IMAGE_PLACEHOLDER}`);
  });

  it('編輯器卸下時把目標拿掉', async () => {
    const fake = createFakeUploads();
    const { unmount } = render(<EditorHarness uploads={fake.uploads} />);
    await findEditor('說明');
    await waitFor(() => {
      expect(fake.target()).not.toBeNull();
    });
    unmount();
    expect(fake.target()).toBeNull();
  });
});
