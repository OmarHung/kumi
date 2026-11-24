import { QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { aFile, attachment, installFakeUploads, serveAttachments, type FakeUpload } from '../../test/attachments';
import { smkDetail } from '../../test/fixtures';
import { editorText, findEditor, typeInEditor } from '../../test/richText';
import { useEditorUploads } from '../attachments/useEditorUploads';
import { abilitiesOf, ProjectTableProvider } from '../item-table/ProjectTableContext';
import { RichTextEditor } from './RichTextEditor';

const detail = smkDetail();
const GUID = attachment(1).id;

function Field({ isBusy = false }: { isBusy?: boolean }) {
  const [value, setValue] = useState('');
  const uploads = useEditorUploads({ itemKey: 'SMK-7', value, onChange: setValue, maxLength: 1000, isReadOnly: isBusy });
  return (
    <>
      <RichTextEditor label="留言" value={value} onChange={setValue} maxLength={1000} rows={3} hint="提示" onSubmit={() => undefined} isBusy={isBusy} error={null} uploads={uploads} />
      <output aria-label="Markdown">{value}</output>
    </>
  );
}

function renderField() {
  const queryClient = createQueryClient({ queries: { retry: false } });
  serveAttachments('SMK-7', []);
  return render(
    <QueryClientProvider client={queryClient}>
      <ProjectTableProvider value={{ detail, can: abilitiesOf(detail), todayDate: '2026-11-20', onOpenMembers: () => undefined }}>
        <Field />
      </ProjectTableProvider>
    </QueryClientProvider>,
  );
}

let fakes: readonly FakeUpload[];

beforeEach(() => {
  fakes = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('所見即所得的編輯器接上真的 useEditorUploads', () => {
  it('貼上圖片：游標的位置先放「上傳中」，傳完換成圖片，Markdown 是 ![檔名](attachment:編號)', async () => {
    const user = userEvent.setup();
    renderField();
    const editor = await findEditor('留言');
    await typeInEditor(user, editor, '看這個：');
    fireEvent.paste(editor, { clipboardData: { files: [aFile('截圖.png', 2048, 'image/png')], types: ['Files'], getData: () => '' } });
    expect(screen.getByLabelText('Markdown').textContent).toBe('看這個：\n![上傳中：截圖.png…]()');
    expect(await screen.findByText('上傳中：截圖.png…')).toBeInTheDocument();
    await waitFor(() => {
      expect(fakes).toHaveLength(1);
    });

    fakes[0]?.respond(201, attachment(1, { fileName: '截圖.png', contentType: 'image/png', sizeBytes: 2048 }));

    await waitFor(() => {
      expect(screen.getByLabelText('Markdown').textContent).toBe(`看這個：\n![截圖.png](attachment:${GUID})`);
    });
    expect(await screen.findByRole('img', { name: '截圖.png' })).toBeInTheDocument();
    expect(screen.queryByText(/上傳中/)).toBeNull();
  });

  it('傳完之前使用者又打了字：不被換字蓋掉，游標留在使用者打字的地方', async () => {
    const user = userEvent.setup();
    renderField();
    const editor = await findEditor('留言');
    await typeInEditor(user, editor, '前');
    fireEvent.paste(editor, { clipboardData: { files: [aFile('報告.pdf')], types: ['Files'], getData: () => '' } });
    await waitFor(() => {
      expect(fakes).toHaveLength(1);
    });
    await user.keyboard('後');
    fakes[0]?.respond(201, attachment(1, { fileName: '報告.pdf' }));
    await waitFor(() => {
      expect(screen.getByLabelText('Markdown')).toHaveTextContent(`前[報告.pdf](attachment:${GUID})後`);
    });
    expect(editorText(editor)).toBe('前報告.pdf後');
  });

  it('上傳失敗：記號拿掉', async () => {
    const user = userEvent.setup();
    renderField();
    const editor = await findEditor('留言');
    await typeInEditor(user, editor, '字');
    fireEvent.paste(editor, { clipboardData: { files: [aFile('報告.pdf')], types: ['Files'], getData: () => '' } });
    await waitFor(() => {
      expect(fakes).toHaveLength(1);
    });
    fakes[0]?.respond(500, { code: 'server.error', message: '壞了' });
    await waitFor(() => {
      expect(screen.getByLabelText('Markdown')).toHaveTextContent(/^字$/);
    });
    expect(screen.queryByText(/上傳中/)).toBeNull();
  });
});
