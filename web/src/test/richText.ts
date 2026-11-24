import { expect, vi } from 'vitest';
import type { Editor } from '@tiptap/core';
import { screen, waitFor, within } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import type { EditorUploads, UploadTarget } from '../features/attachments/useEditorUploads';

/**
 * 所見即所得的編輯器（features/rich-text）的測試工具。編輯器是延後載入的，所以一律用非同步的方法等它出現。
 * jsdom 裡 user-event 對 contenteditable 的選取沒實作：`user.keyboard('{Control>}{End}{/Control}')` 會丟例外，
 * 游標要用 placeCaretAtEnd 或 Tiptap 的指令放。
 */

/** 等延後載入完，回傳可編輯的元素（role="textbox"，名稱是編輯器的標籤）。 */
export async function findEditor(name: string | RegExp, container?: HTMLElement): Promise<HTMLElement> {
  const scope = container === undefined ? screen : within(container);
  const editor = await scope.findByRole('textbox', { name });
  await waitFor(() => {
    expect(editor).toHaveAttribute('contenteditable', 'true');
  });
  return editor;
}

/**
 * 可編輯元素背後的 Tiptap 編輯器（Tiptap 把它放在元素上給測試用）：jsdom 沒有版面，點圖片、拖曳這類要靠座標的操作做不了，
 * 要選取節點、設游標時用它的指令（例如 `tiptapOf(editor).commands.setNodeSelection(1)`）。
 */
export function tiptapOf(editorElement: HTMLElement): Editor {
  const { editor } = editorElement as HTMLElement & { editor?: Editor };
  if (editor === undefined) throw new Error('這個元素不是 Tiptap 的編輯器。');
  return editor;
}

/** 把游標放到編輯器裡最後一段文字的後面（沒有文字時放在整個編輯器的最後面）；用 DOM 的 Selection，ProseMirror 會從 selectionchange 讀回去。 */
export function placeCaretAtEnd(editor: HTMLElement): void {
  const selection = window.getSelection();
  if (selection === null) return;
  const walker = document.createTreeWalker(editor, NodeFilter.SHOW_TEXT);
  let last: Node | null = null;
  for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
    if (node.textContent !== '') last = node;
  }
  const range = document.createRange();
  if (last === null) {
    range.selectNodeContents(editor);
    range.collapse(false);
  } else {
    range.setStart(last, last.textContent?.length ?? 0);
    range.collapse(true);
  }
  selection.removeAllRanges();
  selection.addRange(range);
  document.dispatchEvent(new Event('selectionchange'));
}

/** user.keyboard 的一個個單位：{Enter}、{Shift>}、[KeyA]、{{、[[ 與單一個字。 */
const KEY_TOKEN = /\{\{|\[\[|\{[^{}]*\}|\[[^[\]]*\]|[\s\S]/gu;

/**
 * 點進去、游標放到最後再打字（`{` 與 `[` 在 user-event 的 keyboard 裡有特別意思，要打的話連寫兩個）。
 * 一個單位呼叫一次 keyboard：ProseMirror 的輸入規則（「## 」「- 」）要看到前一個字已經進了文件，連在一次呼叫裡打會漏掉。
 */
export async function typeInEditor(user: UserEvent, editor: HTMLElement, text: string): Promise<void> {
  await user.click(editor);
  placeCaretAtEnd(editor);
  for (const token of text.match(KEY_TOKEN) ?? []) {
    await user.keyboard(token);
  }
}

/** 清空：先全選再 Backspace。 */
export async function clearEditor(user: UserEvent, editor: HTMLElement): Promise<void> {
  await user.click(editor);
  await user.keyboard('{Control>}a{/Control}{Backspace}');
}

/** 看得到的字：區塊之間用換行分開（不含工具列與提示）；最後面那個永遠有的空段落不算。 */
export function editorText(editor: HTMLElement): string {
  const blocks = Array.from(editor.children, (block) => block.textContent);
  while (blocks.at(-1) === '') blocks.pop();
  return blocks.join('\n');
}

/** 上傳的假物件（編輯器的 uploads 屬性）：記下 addFiles 與編輯器註冊的目標。真的上傳走 useEditorUploads，要在 ProjectTableContext 裡。 */
export function createFakeUploads(options: { readonly canUpload?: boolean } = {}) {
  const canUpload = options.canUpload ?? true;
  const state: { target: UploadTarget | null; dragging: boolean } = { target: null, dragging: false };
  const addFiles = vi.fn<(files: readonly File[]) => void>();
  const uploads: EditorUploads = {
    canUpload,
    isUploading: false,
    addFiles,
    announceUploading: () => undefined,
    problems: [],
    dismissProblems: () => undefined,
    announcement: '',
    hasAnnouncer: false,
    binding: {
      canUpload,
      isDragging: false,
      attach: (target) => {
        state.target = target;
      },
      handlers: {
        onPaste: (event) => {
          const files = Array.from(event.clipboardData?.files ?? []);
          if (files.length === 0) return;
          event.preventDefault();
          addFiles(files);
        },
        onDragEnter: () => undefined,
        onDragOver: () => undefined,
        onDragLeave: () => undefined,
        onDrop: (event) => {
          event.preventDefault();
          addFiles(Array.from(event.dataTransfer?.files ?? []));
        },
      },
    },
  };
  return { uploads, addFiles, target: () => state.target };
}
