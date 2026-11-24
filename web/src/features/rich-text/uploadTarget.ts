import type { Editor, JSONContent } from '@tiptap/core';
import type { Selection } from '@tiptap/pm/state';
import { useEffect } from 'react';
import type { EditorUploads, UploadPlaceholderSpec, UploadTarget } from '../attachments/useEditorUploads';
import { PROGRAMMATIC_META } from './extensions/editorBehavior';
import { markdownToDoc } from './markdown/markdownDoc';

/** 每個記號一個 uploadPlaceholder，中間用 hardBreak 分開。 */
function placeholderNodes(specs: readonly UploadPlaceholderSpec[]): JSONContent[] {
  return specs.flatMap((spec, index): JSONContent[] => [
    ...(index > 0 ? [{ type: 'hardBreak' }] : []),
    { type: 'uploadPlaceholder', attrs: { placeholder: spec.placeholder, name: spec.name, isImage: spec.isImage } },
  ]);
}

const HARD_BREAK: JSONContent = { type: 'hardBreak' };

/**
 * 有圖片時記號自成一行：游標前面（同一行）有字就先換行，後面還有字就再換一行。圖片和字擠在同一行時，
 * 顯示出來是一張大圖接在句子後面；檔案的小卡留在行內（「報價單見 [檔案]」是自然的寫法）。
 */
function withOwnLine(selection: Selection, specs: readonly UploadPlaceholderSpec[], nodes: JSONContent[]): JSONContent[] {
  if (!specs.some((spec) => spec.isImage)) return nodes;
  const { $from, $to } = selection;
  const needsBefore = $from.parentOffset > 0 && $from.nodeBefore?.type.name !== 'hardBreak';
  const needsAfter = $to.parentOffset < $to.parent.content.size && $to.nodeAfter?.type.name !== 'hardBreak';
  return [...(needsBefore ? [HARD_BREAK] : []), ...nodes, ...(needsAfter ? [HARD_BREAK] : [])];
}

function insertPlaceholders(editor: Editor, specs: readonly UploadPlaceholderSpec[]): void {
  const { selection } = editor.state;
  const nodes = placeholderNodes(specs);
  // 游標在程式碼區塊裡放不了行內的節點：放在那個區塊後面的新段落。
  if (selection.$from.parent.type.spec.code === true) {
    const after = selection.$from.after();
    editor.chain().focus().insertContentAt({ from: after, to: after }, { type: 'paragraph', content: nodes }).run();
    return;
  }
  editor.chain().focus().insertContentAt({ from: selection.from, to: selection.to }, withOwnLine(selection, specs, nodes)).run();
}

function findPlaceholder(editor: Editor, placeholder: string): { readonly from: number; readonly to: number } | null {
  const result: { range: { from: number; to: number } | null } = { range: null };
  editor.state.doc.descendants((node, position) => {
    if (result.range === null && node.type.name === 'uploadPlaceholder' && node.attrs.placeholder === placeholder) {
      result.range = { from: position, to: position + node.nodeSize };
    }
    return result.range === null;
  });
  return result.range;
}

/** 參照的 Markdown（![檔名](attachment:編號) 或 [檔名](attachment:編號)）轉成行內的節點。 */
function referenceNodes(replacement: string): JSONContent[] {
  return replacement === '' ? [] : (markdownToDoc(replacement).content?.[0]?.content ?? []);
}

function replaceNow(editor: Editor, placeholder: string, replacement: string): void {
  const range = findPlaceholder(editor, placeholder);
  if (range === null) return;
  const { state } = editor;
  const nodes = referenceNodes(replacement).map((json) => state.schema.nodeFromJSON(json));
  // 拿掉記號時，跟在後面的換行一起拿掉（多個記號之間用換行分開）。
  const next = state.doc.resolve(range.to).nodeAfter;
  const to = nodes.length === 0 && next?.type.name === 'hardBreak' ? range.to + next.nodeSize : range.to;
  const transaction = state.tr.replaceWith(range.from, to, nodes).setMeta(PROGRAMMATIC_META, true).setMeta('addToHistory', false);
  editor.view.dispatch(transaction);
}

/**
 * 輸入法組字中先不換（換字會中斷選字），組字結束才換；換的時候再找一次記號的位置（文字可能已經變了）。
 * 回傳記號還在不在：使用者已經刪掉的不補回去。
 */
function replacePlaceholder(editor: Editor, placeholder: string, replacement: string): boolean {
  if (findPlaceholder(editor, placeholder) === null) return false;
  if (!editor.view.composing) {
    replaceNow(editor, placeholder, replacement);
    return true;
  }
  editor.view.dom.addEventListener(
    'compositionend',
    () => {
      setTimeout(() => {
        if (!editor.isDestroyed) replaceNow(editor, placeholder, replacement);
      }, 0);
    },
    { once: true },
  );
  return true;
}

export function createUploadTarget(editor: Editor): UploadTarget {
  return {
    insertPlaceholders: (specs) => {
      insertPlaceholders(editor, specs);
    },
    replacePlaceholder: (placeholder, replacement) => replacePlaceholder(editor, placeholder, replacement),
  };
}

/** 編輯器掛上時把自己註冊成上傳的目標（記號放在游標、傳完換成圖片或檔案的小卡），卸下時拿掉。 */
export function useUploadTarget(editor: Editor | null, uploads: EditorUploads | undefined): void {
  const attach = uploads?.binding.attach;
  useEffect(() => {
    if (editor === null || attach === undefined) return undefined;
    attach(createUploadTarget(editor));
    return () => {
      attach(null);
    };
  }, [editor, attach]);
}
