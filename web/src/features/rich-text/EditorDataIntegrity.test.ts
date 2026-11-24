import { Editor } from '@tiptap/core';
import { Slice } from '@tiptap/pm/model';
import { afterEach, describe, expect, it } from 'vitest';
import { createExtensions, EDITOR_BEHAVIOR_OPTIONS } from './editorExtensions';
import { createEditorProps, type EditorInteractions } from './editorInteractions';
import { docToMarkdown, markdownToDoc } from './markdown/markdownDoc';

const INTERACTIONS: EditorInteractions = {
  onSubmit: () => undefined,
  onCancel: undefined,
  isBusy: false,
  isCollapsed: false,
  isMentionOpen: () => false,
  openLink: () => undefined,
  uploads: undefined,
};

const editors: Editor[] = [];

function makeEditor(markdown: string): Editor {
  const editor = new Editor({
    ...EDITOR_BEHAVIOR_OPTIONS,
    extensions: createExtensions({ isBusy: () => false, suggestion: {}, placeholder: undefined }),
    content: markdownToDoc(markdown),
    editorProps: createEditorProps(() => INTERACTIONS),
  });
  editors.push(editor);
  return editor;
}

afterEach(() => {
  editors.splice(0).forEach((editor) => { editor.destroy(); });
});

function markdownOf(editor: Editor): string {
  return docToMarkdown(editor.getJSON());
}

function clipboardEvent(data: Readonly<Record<string, string>>): ClipboardEvent {
  return { clipboardData: { getData: (type: string) => data[type] ?? '', files: [] }, preventDefault: () => undefined } as unknown as ClipboardEvent;
}

/** 純文字貼上：走編輯器自己的 handlePaste。 */
function pastePlain(editor: Editor, text: string): void {
  editor.view.someProp('handlePaste', (handler) => handler(editor.view, clipboardEvent({ 'text/plain': text }), editor.state.doc.slice(0, 0)));
}

/** HTML 貼上（網頁、VS Code）：走 ProseMirror 的貼上流程，交易帶 paste 的記號，貼上規則才會跑。 */
function pasteHtml(editor: Editor, html: string): void {
  editor.view.pasteHTML(html, clipboardEvent({ 'text/html': html, 'text/plain': html.replace(/<[^>]+>/g, '') }));
}

describe('貼上不經過 Tiptap 的貼上規則', () => {
  it.each([
    ['rm *.txt and *.log'],
    ['2 * 3 * 4'],
    ['use _ and _ here'],
    ['a ** b ** c'],
    ['見 https://example.com/a），然後'],
    ['https://example.com/a'],
  ])('純文字 %s 貼進空段落，文字原樣、沒有標記', (text) => {
    const editor = makeEditor('');
    editor.commands.focus('end');

    pastePlain(editor, text);

    expect(JSON.stringify(editor.getJSON())).not.toContain('"marks"');
    expect(editor.state.doc.textContent).toBe(text);
  });

  it.each([
    ['rm *.txt and *.log', '<p>rm *.txt and *.log</p>'],
    ['2 * 3 * 4', '<p>2 * 3 * 4</p>'],
    ['見 https://example.com/a），然後', '<p>見 https://example.com/a），然後</p>'],
    ['https://example.com/a', '<p>https://example.com/a</p>'],
  ])('HTML %s 貼進去，文字原樣、沒有被改成標記或連結', (text, html) => {
    const editor = makeEditor('');
    editor.commands.focus('end');

    pasteHtml(editor, html);

    const marks = JSON.stringify(editor.getJSON());
    expect(marks).not.toContain('"marks"');
    expect(editor.state.doc.textContent).toBe(text);
  });
});

describe('貼到一行中間時區塊的記號留著', () => {
  it.each([
    ['# 標題', 'abc\n\n# 標題'],
    ['- 項目', 'abc\n\n- 項目'],
    ['1. 步驟', 'abc\n\n1. 步驟'],
    ['> 引用', 'abc\n\n> 引用'],
    ['- a\n- b', 'abc\n\n- a\n- b'],
    ['```\nx\n```', 'abc\n\n```\nx\n```'],
  ])('在 abc 後面貼 %j', (text, expected) => {
    const editor = makeEditor('abc');
    editor.commands.focus('end');

    pastePlain(editor, text);

    expect(markdownOf(editor)).toBe(expected);
  });

  it('單一段落照舊接在游標後面', () => {
    const editor = makeEditor('abc');
    editor.commands.focus('end');

    pastePlain(editor, '**粗**字');

    expect(markdownOf(editor)).toBe('abc**粗**字');
  });

  it('好幾個段落頭尾和游標所在的行合併', () => {
    const editor = makeEditor('ab');
    editor.commands.setTextSelection(2);

    pastePlain(editor, 'x\n\ny');

    expect(markdownOf(editor)).toBe('ax\n\nyb');
  });
});

describe('表格儲存格只放段落', () => {
  const TABLE = '| a | b |\n| - | - |\n| x | y |';

  /** 游標放進第二列第一格的字後面。 */
  function inCell(): Editor {
    const editor = makeEditor(TABLE);
    let position = 0;
    editor.state.doc.descendants((node, pos) => {
      if (node.isText && node.text === 'x') position = pos + 1;
    });
    editor.commands.setTextSelection(position);
    return editor;
  }

  it.each([
    ['項目清單', (e: Editor) => e.chain().focus().toggleBulletList().run()],
    ['編號清單', (e: Editor) => e.chain().focus().toggleOrderedList().run()],
    ['待辦清單', (e: Editor) => e.chain().focus().toggleTaskList().run()],
    ['引用', (e: Editor) => e.chain().focus().toggleBlockquote().run()],
    ['標題', (e: Editor) => e.chain().focus().toggleHeading({ level: 2 }).run()],
    ['程式碼區塊', (e: Editor) => e.chain().focus().toggleCodeBlock().run()],
  ])('工具列的%s在儲存格裡不會改變內容', (_name, command) => {
    const editor = inCell();

    command(editor);

    expect(markdownOf(editor)).toBe(TABLE);
  });

  it.each([['- '], ['1. '], ['> '], ['## '], ['```'], ['[ ] ']])('在儲存格裡打 %j 不會變成區塊', (typed) => {
    const editor = makeEditor('| a | b |\n| - | - |\n| | y |');
    const { view } = editor;
    editor.state.doc.descendants((node, pos) => {
      // 輸入規則只在段落開頭觸發：游標放進第二列的空儲存格。
      if (node.type.name === 'tableCell' && node.textContent === '' && editor.state.selection.from < pos) editor.commands.setTextSelection(pos + 2);
    });
    const marker = typed.slice(0, -1);
    view.dispatch(view.state.tr.insertText(marker));
    const at = editor.state.selection.from;
    view.someProp('handleTextInput', (handler) => handler(view, at, at, typed.slice(-1), () => view.state.tr));

    const types: string[] = [];
    editor.state.doc.descendants((node) => { types.push(node.type.name); });
    expect(types.filter((name) => ['bulletList', 'orderedList', 'blockquote', 'heading', 'codeBlock', 'taskList'].includes(name))).toEqual([]);
  });

  it('儲存格裡有清單的文件寫成 Markdown 時文字不會不見', () => {
    const json = docToMarkdown({
      type: 'doc',
      content: [{
        type: 'table',
        content: [
          { type: 'tableRow', content: [{ type: 'tableHeader', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'h' }] }] }] },
          { type: 'tableRow', content: [{ type: 'tableCell', content: [
            { type: 'bulletList', content: [
              { type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: '甲' }] }] },
              { type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: '乙' }] }] },
            ] },
            { type: 'blockquote', content: [{ type: 'paragraph', content: [{ type: 'text', text: '引' }] }] },
          ] }] },
        ],
      }],
    });

    expect(json).toContain('甲 乙');
    expect(json).toContain('引');
  });
});

describe('複製同一個文字區塊裡的幾個字，純文字不帶區塊的記號', () => {
  function copyText(editor: Editor, from: number, to: number): string {
    editor.commands.setTextSelection({ from, to });
    const serialize = editor.view.someProp('clipboardTextSerializer', (handler) => handler);
    if (serialize === undefined) throw new Error('沒有 clipboardTextSerializer');
    return serialize(editor.state.selection.content(), editor.view);
  }

  it.each([
    ['清單項目', '- 項目文字\n- 二', 3, 5, '項目'],
    ['標題', '# 標題文字', 2, 4, '題文'],
    ['引用', '> 引用文字', 2, 4, '引用'],
    ['程式碼區塊', '```\nabc_def\n```', 2, 6, 'bc_d'],
    ['含星號與底線的段落', '2 \\* 3 and snake\\_case\\_x', 3, 21, '* 3 and snake_case'],
  ])('在%s裡複製', (_name, markdown, from, to, expected) => {
    const editor = makeEditor(markdown);

    expect(copyText(editor, from, to)).toBe(expected);
  });

  it('表格儲存格裡複製', () => {
    const editor = makeEditor('| a |\n| - |\n| xyz |');
    let start = 0;
    editor.state.doc.descendants((node, pos) => {
      if (node.isText && node.text === 'xyz') start = pos;
    });

    expect(copyText(editor, start + 1, start + 3)).toBe('yz');
  });

  it('換行是 \\n，提及是 @名稱，附件是檔名', () => {
    const editor = makeEditor('');
    editor.commands.setContent({
      type: 'doc',
      content: [{
        type: 'paragraph',
        content: [
          { type: 'text', text: '甲' },
          { type: 'hardBreak' },
          { type: 'mention', attrs: { id: 'p1', label: '小明' } },
          { type: 'text', text: ' ' },
          { type: 'attachmentFile', attrs: { attachmentId: '0f8fad5b-d9cb-469f-a165-70867728950e', name: '報告.pdf' } },
        ],
      }],
    });

    expect(copyText(editor, 1, editor.state.doc.content.size - 1)).toBe('甲\n@小明 報告.pdf');
  });

  it('跨區塊的選取照舊輸出 Markdown', () => {
    const editor = makeEditor('# 標題\n\n- 項目');

    expect(copyText(editor, 1, editor.state.doc.content.size - 1)).toBe('# 標題\n\n- 項目');
  });
});

describe('從網頁貼上的圖片只收附件與 http(s)', () => {
  const GUID = '0f8fad5b-d9cb-469f-a165-70867728950e';

  function imageSources(editor: Editor): string[] {
    const sources: string[] = [];
    editor.state.doc.descendants((node) => {
      if (node.type.name === 'image') sources.push(String(node.attrs.src));
    });
    return sources;
  }

  it.each([
    ['data 網址', 'data:image/png;base64,iVBORw0KGgo='],
    ['file 網址', 'file:///etc/passwd'],
    ['blob 網址', 'blob:https://example.com/1234'],
  ])('%s不收', (_name, src) => {
    const editor = makeEditor('');
    editor.commands.focus('end');

    pasteHtml(editor, `<p>前<img src="${src}" alt="x">後</p>`);

    expect(imageSources(editor)).toEqual([]);
  });

  it.each([[`attachment:${GUID}`], ['https://example.com/a.png'], ['http://example.com/a.png']])('%s 照收', (src) => {
    const editor = makeEditor('');
    editor.commands.focus('end');

    pasteHtml(editor, `<p><img src="${src}" alt="x"></p>`);

    expect(imageSources(editor)).toEqual([src]);
  });

  it('編輯器自己複製出去的附件圖片，貼回來還是附件', () => {
    const editor = makeEditor(`![截圖](attachment:${GUID})`);
    const { dom } = editor.view.serializeForClipboard(new Slice(editor.state.doc.content, 0, 0));
    const html = dom.innerHTML;
    const other = makeEditor('');
    other.commands.focus('end');

    pasteHtml(other, html);

    expect(html).toContain(`src="attachment:${GUID}"`);
    expect(imageSources(other)).toEqual([`attachment:${GUID}`]);
  });
});
