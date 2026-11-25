import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { editorText, findEditor, placeCaretAtEnd, tiptapOf } from '../../test/richText';
import { EditorHarness } from '../../test/richTextHarness';

const GUID = '0190a100-0000-7000-8000-000000000001';

describe('編輯器：附件與圖片', () => {
  it('自己附件的圖片顯示 img，網址是同網域的下載端點', async () => {
    render(<EditorHarness initial={`![截圖.png](attachment:${GUID})`} />);
    await findEditor('說明');
    const image = await screen.findByRole('img', { name: '截圖.png' });
    expect(image).toHaveAttribute('src', `/v1/attachments/${GUID}/content`);
  });

  it('給了 attachmentUrl 時圖片用它組網址（知識庫的頁面）', async () => {
    render(<EditorHarness attachmentUrl={(id) => `/v1/knowledge/attachments/${id}/content`} initial={`![截圖.png](attachment:${GUID})`} />);
    await findEditor('說明');
    const image = await screen.findByRole('img', { name: '截圖.png' });
    expect(image).toHaveAttribute('src', `/v1/knowledge/attachments/${GUID}/content`);
  });

  it('載入失敗換成一行字，名稱沿用檔名', async () => {
    render(<EditorHarness initial={`![截圖.png](attachment:${GUID})`} />);
    await findEditor('說明');
    fireEvent.error(await screen.findByRole('img', { name: '截圖.png' }));
    expect(await screen.findByText('圖片已刪除或無法顯示：截圖.png')).toBeInTheDocument();
  });

  it('hasAttachments 是 false 時附件圖片不載入，畫成「圖片：名稱」的小卡', async () => {
    render(<EditorHarness hasAttachments={false} initial={`![截圖.png](attachment:${GUID})`} />);
    await findEditor('說明');
    expect(await screen.findByText('圖片：截圖.png')).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: '截圖.png' })).toBeNull();
  });

  it('外部網址的圖片不載入（內容安全政策只允許同網域），畫成小卡，存回去不變', async () => {
    const onValue = vi.fn();
    const original = '![外部圖](https://example.com/a.png)';
    const user = userEvent.setup();
    render(<EditorHarness initial={original} onValue={onValue} />);
    const editor = await findEditor('說明');
    expect(await screen.findByText('圖片：外部圖')).toBeInTheDocument();
    expect(editor.querySelector('img:not(.ProseMirror-separator)')).toBeNull();
    await user.click(editor);
    expect(onValue).not.toHaveBeenCalled();
  });

  it('沒有說明文字的圖片小卡退回「圖片」', async () => {
    render(<EditorHarness initial="![](https://example.com/a.png)" />);
    await findEditor('說明');
    expect(await screen.findByText('圖片：圖片')).toBeInTheDocument();
  });

  it('檔案連結是圖示加檔名的小卡，不是連結（點了不開新分頁）', async () => {
    render(<EditorHarness initial={`看 [報告.pdf](attachment:${GUID})`} />);
    const editor = await findEditor('說明');
    expect(await screen.findByText('報告.pdf')).toBeInTheDocument();
    expect(editor.querySelector('[data-icon="filePdf"]')).not.toBeNull();
    expect(editor.querySelector('a')).toBeNull();
  });

  it('圖片選取時有選取的外框，刪掉選取就整個不見', async () => {
    const onValue = vi.fn();
    render(<EditorHarness initial={`前![截圖.png](attachment:${GUID})後`} onValue={onValue} />);
    const editor = await findEditor('說明');
    await screen.findByRole('img', { name: '截圖.png' });
    // jsdom 沒有版面，點不到圖片：用 Tiptap 的指令選取它（位置 2：「前」之後）。
    editor.focus();
    tiptapOf(editor).commands.setNodeSelection(2);
    await waitFor(() => {
      expect(editor.querySelector('.ProseMirror-selectednode')).not.toBeNull();
    });
    // jsdom 的 Selection 畫不出節點選取，按鍵會把它讀成別的選取：直接下刪除選取的指令（Backspace 的鍵盤對應是 ProseMirror 的）。
    tiptapOf(editor).commands.deleteSelection();
    expect(onValue).toHaveBeenLastCalledWith('前後');
  });
});

describe('編輯器：不認得的寫法原樣保留', () => {
  it('原始的 HTML 區塊是唯讀的一塊原文，存回去一字不改', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness initial={'前面\n\n<div class="x">原始 HTML</div>\n\n後面'} onValue={onValue} />);
    const editor = await findEditor('說明');
    const raw = editor.querySelector('[data-raw-block]');
    expect(raw).toHaveTextContent('<div class="x">原始 HTML</div>');
    expect(raw).toHaveAttribute('contenteditable', 'false');
    await user.click(editor);
    placeCaretAtEnd(editor);
    await user.keyboard('X');
    expect(onValue).toHaveBeenLastCalledWith('前面\n\n<div class="x">原始 HTML</div>\n\n後面X');
  });

  it('行內的 HTML 與註腳的參照是唯讀的一小塊', async () => {
    const onValue = vi.fn();
    const original = '字<kbd>K</kbd>字[^1]\n\n[^1]: 註腳';
    render(<EditorHarness initial={original} onValue={onValue} />);
    const editor = await findEditor('說明');
    expect(editor.querySelectorAll('[data-raw-inline]').length).toBeGreaterThanOrEqual(2);
    expect(editor.querySelector('[data-raw-block]')).toHaveTextContent('[^1]: 註腳');
    expect(onValue).not.toHaveBeenCalled();
  });

  it('整塊刪掉原文區塊', async () => {
    const onValue = vi.fn();
    render(<EditorHarness initial={'<div>x</div>\n\n字'} onValue={onValue} />);
    const editor = await findEditor('說明');
    editor.focus();
    tiptapOf(editor).commands.setNodeSelection(0);
    tiptapOf(editor).commands.deleteSelection();
    expect(onValue).toHaveBeenLastCalledWith('字');
  });
});

describe('編輯器：其他的結構', () => {
  it('表格顯示成表格（表頭、對齊），沒動就不通知', async () => {
    const onValue = vi.fn();
    render(<EditorHarness initial={'| 名稱 | 數量 |\n| :--- | ---: |\n| 蘋果 | 3 |'} onValue={onValue} />);
    const editor = await findEditor('說明');
    expect(editor.querySelectorAll('th')).toHaveLength(2);
    expect(editor.querySelector('td[data-align="right"]')).toHaveTextContent('3');
    expect(onValue).not.toHaveBeenCalled();
  });

  it('待辦清單的勾選框可以點，存成 - [x]', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness initial="- [ ] 買牛奶" onValue={onValue} />);
    const editor = await findEditor('說明');
    await user.click(editor.querySelector('input[type="checkbox"]') as HTMLElement);
    expect(onValue).toHaveBeenLastCalledWith('- [x] 買牛奶');
  });

  it('標題用 h4～h6 的標籤（頁面的標題層級不被搶），data-level 是原本的層級', async () => {
    render(<EditorHarness initial={'# 一\n\n### 三\n\n###### 六'} />);
    const editor = await findEditor('說明');
    expect(Array.from(editor.querySelectorAll('[data-level]'), (heading) => `${heading.tagName}${heading.getAttribute('data-level') ?? ''}`)).toEqual(['H41', 'H53', 'H66']);
    expect(editorText(editor)).toBe('一\n三\n六');
  });

  it('連結顯示成連結，點了不開新分頁（編輯器裡點連結是放游標）', async () => {
    const user = userEvent.setup();
    render(<EditorHarness initial="[網站](https://example.com/)" />);
    const editor = await findEditor('說明');
    const link = editor.querySelector('a');
    expect(link).toHaveAttribute('href', 'https://example.com/');
    await user.click(link as HTMLElement);
    expect(editor).toHaveFocus();
  });

  it('不認得的連結網址（javascript:）原文照存，顯示時 href 是空的', async () => {
    const onValue = vi.fn();
    render(<EditorHarness initial="[壞連結](javascript:alert(1))" onValue={onValue} />);
    const editor = await findEditor('說明');
    expect(editor.querySelector('a')?.getAttribute('href') ?? '').not.toContain('javascript');
    expect(onValue).not.toHaveBeenCalled();
  });
});

describe('編輯器：貼上與複製', () => {
  it('只有純文字的剪貼簿照 Markdown 讀', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness onValue={onValue} />);
    const editor = await findEditor('說明');
    await user.click(editor);
    await user.paste('## 標題\n\n- 一\n- 二\n\n**粗**');
    expect(editor.querySelector('h4[data-level="2"]')).toHaveTextContent('標題');
    expect(editor.querySelectorAll('li')).toHaveLength(2);
    expect(onValue).toHaveBeenLastCalledWith('## 標題\n\n- 一\n- 二\n\n**粗**');
  });

  it('單行的純文字貼進游標的位置，不另開段落', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness initial="前後" onValue={onValue} />);
    const editor = await findEditor('說明');
    await user.click(editor);
    placeCaretAtEnd(editor);
    await user.paste('中');
    expect(onValue).toHaveBeenLastCalledWith('前後中');
  });

  it('剪貼簿有 HTML 時照 HTML（不當 Markdown 讀）', async () => {
    const onValue = vi.fn();
    render(<EditorHarness onValue={onValue} />);
    const editor = await findEditor('說明');
    fireEvent.paste(editor, { clipboardData: { files: [], types: ['text/plain', 'text/html'], getData: (type: string) => ({ 'text/html': '<p><strong>網頁</strong></p>', 'text/plain': '## 不是標題' })[type] ?? '' } });
    await waitFor(() => {
      expect(editor.querySelector('strong')).toHaveTextContent('網頁');
    });
    expect(editor.querySelector('h4')).toBeNull();
  });

  it('程式碼區塊裡貼上的純文字不當 Markdown 讀', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<EditorHarness initial={'```\n\n```'} onValue={onValue} />);
    const editor = await findEditor('說明');
    await user.click(editor.querySelector('code') as HTMLElement);
    await user.paste('## 原樣');
    expect(editor.querySelector('pre code')).toHaveTextContent('## 原樣');
    expect(editor.querySelector('h4')).toBeNull();
  });

  it('複製時純文字放 Markdown', async () => {
    const user = userEvent.setup();
    render(<EditorHarness initial={'## 標題\n\n- 一\n- 二'} />);
    const editor = await findEditor('說明');
    await user.click(editor);
    await user.keyboard('{Control>}a{/Control}');
    const setData = vi.fn();
    fireEvent.copy(editor, { clipboardData: { setData, clearData: vi.fn(), getData: () => '' } });
    const plain = setData.mock.calls.find(([type]) => type === 'text/plain') as [string, string] | undefined;
    expect(plain?.[1]).toBe('## 標題\n\n- 一\n- 二');
  });
});

describe('編輯器：複製出去的 HTML', () => {
  it('外部網址的圖片不輸出 src，改放 data-external-src；自己的附件照舊有 src', async () => {
    // Arrange
    render(<EditorHarness initial={`![外部圖](https://example.com/a.png) ![自己的](attachment:${GUID})`} />);
    const editor = await findEditor('說明');
    await screen.findByRole('img', { name: '自己的' });

    // Act
    const html = tiptapOf(editor).getHTML();

    // Assert
    expect(html).toContain('data-external-src="https://example.com/a.png"');
    expect(html).not.toMatch(/\ssrc="https:/);
    expect(html).toContain(`src="attachment:${GUID}"`);
  });

  it('貼回編輯器時 data-external-src 換回圖片的網址', async () => {
    // Arrange
    render(<EditorHarness initial="" />);
    const editor = await findEditor('說明');

    // Act
    tiptapOf(editor).commands.insertContent('<p><img data-external-src="https://example.com/a.png" alt="外部圖"></p>');

    // Assert
    const json = JSON.stringify(tiptapOf(editor).getJSON());
    expect(json).toContain('"type":"image"');
    expect(json).toContain('"src":"https://example.com/a.png"');
  });

  it('data-attachment-file 不是附件編號的 span 不當附件檔案', async () => {
    // Arrange
    render(<EditorHarness initial="" />);
    const editor = await findEditor('說明');

    // Act
    tiptapOf(editor).commands.insertContent('<p><span data-attachment-file="x) 外部的](https://other.example">檔</span></p>');

    // Assert
    expect(JSON.stringify(tiptapOf(editor).getJSON())).not.toContain('attachmentFile');
  });
});
