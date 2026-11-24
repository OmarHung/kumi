import { Extension } from '@tiptap/core';
import { Table, TableCell, TableHeader } from '@tiptap/extension-table';

/**
 * 文件結構的合約（internal/HANDOFF-rich-text-editor.md 4.1、4.2）裡比 Tiptap 預設多的屬性：
 * 清單的 spread（鬆散或緊密，只為了存回去不變）、程式碼區塊的 meta（圍欄第一行語言後面的字）、連結的 title。
 * 都不畫到 DOM 上（title 除外），只活在文件裡。
 */
export const SchemaAttributes = Extension.create({
  name: 'schemaAttributes',

  addGlobalAttributes() {
    return [
      { types: ['bulletList', 'orderedList', 'taskList'], attributes: { spread: { default: false, rendered: false } } },
      { types: ['codeBlock'], attributes: { meta: { default: null, rendered: false } } },
      {
        types: ['link'],
        attributes: {
          title: {
            default: null,
            parseHTML: (element: HTMLElement) => element.getAttribute('title'),
            renderHTML: (attributes: Record<string, unknown>) => (typeof attributes.title === 'string' ? { title: attributes.title } : {}),
          },
        },
      },
    ];
  },
});

/**
 * 表格儲存格的對齊（GFM 的 :---：）：Tiptap 預設寫成行內的 style，內容安全政策不允許行內樣式，改寫成 data-align，樣式在 CSS。
 */
const alignAttribute = {
  default: null,
  parseHTML: (element: HTMLElement) => element.getAttribute('data-align'),
  renderHTML: (attributes: Record<string, unknown>) => (typeof attributes.align === 'string' ? { 'data-align': attributes.align } : {}),
};

/** 儲存格只放段落：GFM 的儲存格是一行文字，清單、引用、巢狀表格寫不出來，存檔時會整格不見。 */
const CELL_CONTENT = 'paragraph+';

export const AlignedTableCell = TableCell.extend({
  content: CELL_CONTENT,
  addAttributes() {
    return { ...this.parent?.(), align: alignAttribute };
  },
});

export const AlignedTableHeader = TableHeader.extend({
  content: CELL_CONTENT,
  addAttributes() {
    return { ...this.parent?.(), align: alignAttribute };
  },
});

/** 表格裡 Tab 移到下一格；最後一格再按 Tab 就離開表格（照常移到下一個控制項），不加新的一列、不困住焦點。 */
export const EditorTable = Table.extend({
  addKeyboardShortcuts() {
    return { ...this.parent?.(), Tab: () => this.editor.commands.goToNextCell() };
  },
});
