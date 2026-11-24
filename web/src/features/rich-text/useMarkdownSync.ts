import type { Editor } from '@tiptap/core';
import { TextSelection } from '@tiptap/pm/state';
import { useEffect, useRef, useState, type RefObject } from 'react';
import { PROGRAMMATIC_META } from './extensions/editorBehavior';
import { docToMarkdown, markdownToDoc } from './markdown/markdownDoc';

/** 載入（或被重設）時的原文，與它轉成文件再轉回去的樣子。 */
export interface MarkdownBaseline {
  readonly original: string;
  readonly normalized: string;
}

export function baselineOf(markdown: string): MarkdownBaseline {
  return { original: markdown, normalized: docToMarkdown(markdownToDoc(markdown)) };
}

/**
 * 沒改就回傳原文：打開編輯器不動、或改了又改回來，轉出來的 Markdown 和「原文轉過去再轉回來」一樣，
 * 就把原文交出去（轉換可能把 `* ` 換成 `- `、在清單前多一個空行），呼叫端才判斷得出「沒改」。
 */
export function markdownToEmit(baseline: MarkdownBaseline, current: string): string {
  return current === baseline.normalized ? baseline.original : current;
}

/** 換掉整份內容（捨棄草稿、留言送出後清空、還原時拿掉殘留的記號）：游標盡量留在原本的位置，超過就放到最後；不進復原的紀錄。 */
function resetContent(editor: Editor, markdown: string): void {
  const { from } = editor.state.selection;
  editor
    .chain()
    .command(({ tr }) => {
      tr.setMeta(PROGRAMMATIC_META, true).setMeta('addToHistory', false);
      return true;
    })
    .setContent(markdownToDoc(markdown), { emitUpdate: false })
    .command(({ tr }) => {
      tr.setSelection(TextSelection.near(tr.doc.resolve(Math.min(from, tr.doc.content.size))));
      return true;
    })
    .run();
}

export interface MarkdownSync {
  /** 編輯器每次改了內容時呼叫：算出要交出去的 Markdown，和上一次一樣就不再通知。 */
  readonly handleUpdate: (editor: Editor) => void;
  /** 最後一次交給外面（或從外面收到）的 Markdown。 */
  readonly lastRef: RefObject<string>;
  readonly baselineRef: RefObject<MarkdownBaseline | null>;
}

/**
 * 編輯器內部自己管文件，這裡管它和外面的 Markdown 字串的關係：內容改了就 onChange（沒改回傳原文），
 * 和上一次交出去的一樣就不再通知。編輯器還沒建好時就可以先用（Tiptap 的 onUpdate 在建立時給）。
 */
export function useMarkdownSync(value: string, onChange: (value: string) => void): MarkdownSync {
  const baselineRef = useRef<MarkdownBaseline | null>(null);
  const lastRef = useRef(value);
  const onChangeRef = useRef(onChange);
  useEffect(() => {
    onChangeRef.current = onChange;
  });
  const [handleUpdate] = useState(() => (editor: Editor) => {
    baselineRef.current ??= baselineOf(lastRef.current);
    const next = markdownToEmit(baselineRef.current, docToMarkdown(editor.getJSON()));
    if (next === lastRef.current) return;
    lastRef.current = next;
    onChangeRef.current(next);
  });
  return { handleUpdate, lastRef, baselineRef };
}

/** 外面的 value 和最後一次交出去的不一樣（捨棄草稿、留言送出後清空、還原時拿掉殘留的記號）：用新的值重設內容。 */
export function useExternalValue(editor: Editor | null, value: string, sync: MarkdownSync): void {
  const { lastRef, baselineRef } = sync;
  useEffect(() => {
    if (editor === null || value === lastRef.current) return;
    lastRef.current = value;
    baselineRef.current = baselineOf(value);
    resetContent(editor, value);
  }, [editor, value, lastRef, baselineRef]);
}
