import { Extension } from '@tiptap/core';
import type { Node as ProseMirrorNode } from '@tiptap/pm/model';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import type { NodeView } from '@tiptap/pm/view';
import markdownStyles from '../../../components/Markdown/Markdown.module.css';

/** 程式放進去的交易（換掉整份內容、上傳完成換記號）：送出中也要放行。 */
export const PROGRAMMATIC_META = 'richTextProgrammatic';

/** 內容裡的標題不搶頁面的層級（和 components/Markdown 一樣）：1、2 → h4，3、4 → h5，5、6 → h6，樣式照原本的層級（data-level）。 */
const HEADING_TAGS: Readonly<Record<number, string>> = { 1: 'h4', 2: 'h4', 3: 'h5', 4: 'h5', 5: 'h6', 6: 'h6' };

function headingView(node: ProseMirrorNode): NodeView {
  const level = Number(node.attrs.level);
  const dom = document.createElement(HEADING_TAGS[level] ?? 'h4');
  dom.className = markdownStyles.heading ?? '';
  dom.dataset.level = String(level);
  return { dom, contentDOM: dom, update: (next) => next.type === node.type && next.attrs.level === node.attrs.level };
}

/** 待辦項目的勾選框（Tiptap 的 node view 畫的 input）。 */
function isTaskCheckbox(target: EventTarget | null): boolean {
  return target instanceof HTMLInputElement && target.type === 'checkbox' && target.closest('li[data-checked]') !== null;
}

export interface EditorBehaviorOptions {
  /** 送出中：內容不能改。要是固定的函式（讀最新的 props）。 */
  readonly isBusy: () => boolean;
}

/**
 * 送出中不能改內容：用 filterTransaction 擋（不用 setEditable(false)：那會讓 contenteditable 變成 false，
 * 焦點掉到頁面本體，留言送出時焦點要留在框裡）。瀏覽器自己改 DOM 的輸入（beforeinput）在這之前就擋下，
 * 不然畫面上多了字、文件裡卻沒有。標題用 h4～h6 的畫法。
 */
export const EditorBehavior = Extension.create<EditorBehaviorOptions>({
  name: 'editorBehavior',

  addOptions() {
    return { isBusy: () => false };
  },

  addProseMirrorPlugins() {
    const { isBusy } = this.options;
    return [
      new Plugin({
        key: new PluginKey('editorBehavior'),
        filterTransaction: (transaction) => !(isBusy() && transaction.docChanged && transaction.getMeta(PROGRAMMATIC_META) !== true),
        props: {
          nodeViews: { heading: headingView },
          handleDOMEvents: {
            beforeinput: (_view, event) => {
              if (!isBusy()) return false;
              event.preventDefault();
              return true;
            },
            // 勾選框是瀏覽器自己切換的，交易被 filterTransaction 擋掉後畫面卻留著打勾：送出中一開始就不讓它動。
            // click 的 preventDefault 讓瀏覽器把勾選狀態還原（滑鼠與空白鍵都會變成 click）。
            click: (_view, event) => {
              if (!isBusy() || !isTaskCheckbox(event.target)) return false;
              event.preventDefault();
              return true;
            },
          },
        },
      }),
    ];
  },
});
