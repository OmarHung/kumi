import { QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { setEditorMode } from '../../lib/editorMode';
import { aFile, attachment, installFakeUploads, serveAttachments, type FakeUpload } from '../../test/attachments';
import { smkDetail } from '../../test/fixtures';
import { useEditorUploads } from '../attachments/useEditorUploads';
import { abilitiesOf, ProjectTableProvider } from '../item-table/ProjectTableContext';
import { RichTextEditor } from './RichTextEditor';

const detail = smkDetail();
const GUID = attachment(1).id;

function Field({ initial = '' }: { initial?: string }) {
  const [value, setValue] = useState(initial);
  const uploads = useEditorUploads({ itemKey: 'SMK-7', value, onChange: setValue, maxLength: 1000 });
  return (
    <RichTextEditor label="留言" value={value} onChange={setValue} maxLength={1000} rows={3} hint="提示" onSubmit={() => undefined} isBusy={false} error={null} uploads={uploads} />
  );
}

function renderField(initial = '') {
  const queryClient = createQueryClient({ queries: { retry: false } });
  serveAttachments('SMK-7', []);
  render(
    <QueryClientProvider client={queryClient}>
      <ProjectTableProvider value={{ detail, can: abilitiesOf(detail), todayDate: '2026-11-20', onOpenMembers: () => undefined }}>
        <Field initial={initial} />
      </ProjectTableProvider>
    </QueryClientProvider>,
  );
  return screen.getByRole<HTMLTextAreaElement>('textbox', { name: '留言' });
}

function paste(target: HTMLElement, files: File[]) {
  fireEvent.paste(target, { clipboardData: { files, types: ['Files'], getData: () => '' } });
}

const png = () => aFile('截圖.png', 2048, 'image/png');
const pngAttachment = () => attachment(1, { fileName: '截圖.png', contentType: 'image/png', sizeBytes: 2048 });

let fakes: readonly FakeUpload[];

beforeEach(() => {
  setEditorMode('markdown');
  fakes = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

async function started() {
  await waitFor(() => {
    expect(fakes).toHaveLength(1);
  });
}

describe('Markdown 原文裡貼圖與附加檔案', () => {
  it('貼上圖片：游標處放「上傳中」的記號、圖片自成一行；傳完換成參照，游標跟著平移', async () => {
    // Arrange
    const box = renderField('看這個：後面');
    box.focus();
    box.setSelectionRange(4, 4);

    // Act
    paste(box, [png()]);

    // Assert
    expect(box).toHaveValue('看這個：\n![上傳中：截圖.png…]()\n後面');
    await started();
    const placeholderEnd = '看這個：\n![上傳中：截圖.png…]()'.length;
    expect(box.selectionStart).toBe(placeholderEnd + 1);

    // Act
    fakes[0]?.respond(201, pngAttachment());

    // Assert
    const reference = `![截圖.png](attachment:${GUID})`;
    await waitFor(() => {
      expect(box).toHaveValue(`看這個：\n${reference}\n後面`);
    });
    expect(box.selectionStart).toBe(`看這個：\n${reference}\n`.length);
  });

  it('上方那一列（只有編輯方式的切換）也有「插入圖片或檔案」：選的檔案在游標處放記號', async () => {
    // Arrange
    const user = userEvent.setup();
    const box = renderField('開頭');
    box.focus();
    box.setSelectionRange(2, 2);

    // Act
    await user.upload(screen.getByLabelText('選擇要插入的圖片或檔案'), aFile('報告.pdf'));

    // Assert
    expect(screen.getByRole('button', { name: '插入圖片或檔案' })).toBeInTheDocument();
    expect(box).toHaveValue('開頭[上傳中：報告.pdf…]()');
  });

  it('游標在最前面（空的）貼上：前後不補換行', async () => {
    // Arrange
    const box = renderField('');

    // Act
    paste(box, [png()]);

    // Assert
    expect(box).toHaveValue('![上傳中：截圖.png…]()');
    await started();
  });

  it('多個檔案：記號之間換行；檔案的記號留在行內，前後不補換行', async () => {
    // Arrange
    const box = renderField('報價單見');
    box.setSelectionRange(4, 4);

    // Act
    paste(box, [aFile('a.pdf'), aFile('b.pdf')]);

    // Assert
    expect(box).toHaveValue('報價單見[上傳中：a.pdf…]()\n[上傳中：b.pdf…]()');
    await waitFor(() => {
      expect(fakes).toHaveLength(2);
    });
  });

  it('傳完之前使用者又打了字：不被換字蓋掉', async () => {
    // Arrange
    const user = userEvent.setup();
    const box = renderField('前');
    box.setSelectionRange(1, 1);
    paste(box, [aFile('報告.pdf')]);
    await started();
    await user.click(box);
    await user.keyboard('{End}後');

    // Act
    fakes[0]?.respond(201, attachment(1, { fileName: '報告.pdf' }));

    // Assert
    await waitFor(() => {
      expect(box).toHaveValue(`前[報告.pdf](attachment:${GUID})後`);
    });
  });

  it('上傳失敗：記號拿掉', async () => {
    // Arrange
    const box = renderField('字');
    paste(box, [aFile('報告.pdf')]);
    await started();

    // Act
    fakes[0]?.respond(500, { code: 'server.error', message: '壞了' });

    // Assert
    await waitFor(() => {
      expect(box).toHaveValue('字');
    });
  });

  it('使用者刪掉「上傳中」的記號：上傳中止，完成時也不補回', async () => {
    // Arrange
    const box = renderField('');
    paste(box, [png()]);
    await started();

    // Act
    fireEvent.change(box, { target: { value: '改寫了' } });

    // Assert
    await waitFor(() => {
      expect(fakes[0]?.isAborted).toBe(true);
    });
    fakes[0]?.respond(201, pngAttachment());
    expect(box).toHaveValue('改寫了');
  });

  it('輸入法組字中不換字，組字結束才換', async () => {
    // Arrange
    const box = renderField('');
    paste(box, [png()]);
    await started();
    fireEvent.compositionStart(box);

    // Act
    fakes[0]?.respond(201, pngAttachment());

    // Assert
    // 上傳完成的處理（換字被延後）在下一輪事件之後才輪得到。
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
    expect(box).toHaveValue('![上傳中：截圖.png…]()');
    fireEvent.compositionEnd(box);
    await waitFor(() => {
      expect(box).toHaveValue(`![截圖.png](attachment:${GUID})`);
    });
  });

  it('上傳中：切換編輯方式停用並說明原因（傳完才能切）', async () => {
    // Arrange
    const box = renderField('');
    paste(box, [png()]);
    await started();

    // Assert
    const toRich = screen.getByRole('button', { name: '所見即所得' });
    expect(toRich).toHaveAttribute('aria-disabled', 'true');
    expect(toRich).toHaveAccessibleDescription('還有檔案在上傳');

    // Act
    act(() => {
      fakes[0]?.respond(201, pngAttachment());
    });

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('button', { name: '所見即所得' })).not.toHaveAttribute('aria-disabled');
    });
  });
});
