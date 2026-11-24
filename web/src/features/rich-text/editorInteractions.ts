import type { JSONContent } from '@tiptap/core';
import { Slice, type Node as ProseNode } from '@tiptap/pm/model';
import { TextSelection } from '@tiptap/pm/state';
import type { EditorProps, EditorView } from '@tiptap/pm/view';
import { isComposingNativeKey } from '../../lib/keyboard';
import type { EditorUploads } from '../attachments/useEditorUploads';
import { contentToMarkdown, markdownToDoc } from './markdown/markdownDoc';

export { handleToolbarKeyDown } from './toolbarKeys';

/** 編輯器的事件處理要用的最新值（Tiptap 在建立編輯器時就抓住了處理函式，所以讀 ref）。 */
export interface EditorInteractions {
  readonly onSubmit: () => void;
  readonly onCancel: (() => void) | undefined;
  readonly isBusy: boolean;
  /** 收起來的留言框：沒有工具列，連結的快速鍵先不處理。 */
  readonly isCollapsed: boolean;
  readonly isMentionOpen: () => boolean;
  readonly openLink: () => void;
  readonly uploads: EditorUploads | undefined;
}

type Latest = () => EditorInteractions;

function isLinkShortcut(event: KeyboardEvent): boolean {
  return (event.ctrlKey || event.metaKey) && !event.shiftKey && !event.altKey && event.key.toLowerCase() === 'k';
}

/**
 * Ctrl／⌘+Enter 送出（輸入法選字中的 Enter 不算）、Esc 取消（提及清單開著時先關它：交給 Suggestion 外掛）、
 * Ctrl／⌘+K 開連結的小框。這支在外掛之前跑（直接給 EditorView 的屬性），Ctrl+Enter 也先於 HardBreak 的 Mod-Enter。
 */
function handleKeyDown(latest: Latest, view: EditorView, event: KeyboardEvent): boolean {
  const current = latest();
  if (isComposingNativeKey(event)) return false;
  if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
    event.preventDefault();
    current.onSubmit();
    return true;
  }
  if (event.key === 'Escape' && current.onCancel !== undefined && !current.isMentionOpen()) {
    event.preventDefault();
    current.onCancel();
    return true;
  }
  if (isLinkShortcut(event) && !current.isCollapsed) {
    // 送出中、程式碼區塊裡不能加連結：照樣吃掉按鍵，不然會落到全域的「搜尋或跳到…」（⌘K）。
    event.preventDefault();
    if (!current.isBusy && view.state.selection.$from.parent.type.spec.code !== true) current.openLink();
    return true;
  }
  return false;
}

/**
 * 純文字（沒有 HTML）照 Markdown 讀再貼進來；有 HTML（網頁、Word）照 HTML。寫不出結構（轉換失敗）就照一般的貼上。
 * 全是段落時開口，頭尾和游標所在的行合併；有別的區塊（標題、清單、引用、程式碼）時不開口，區塊照原樣插進去
 * （開口的話貼在字後面的 `# 標題`、`- 項目` 會變成接在字後面的文字，記號就不見了）。
 */
function pasteMarkdown(view: EditorView, text: string): boolean {
  if (view.state.selection.$from.parent.type.spec.code === true) return false;
  try {
    const doc = view.state.schema.nodeFromJSON(markdownToDoc(text));
    const isAllParagraphs = doc.childCount > 0 && Array.from({ length: doc.childCount }, (_, index) => doc.child(index)).every((block) => block.type.name === 'paragraph');
    const slice = isAllParagraphs ? Slice.maxOpen(doc.content) : new Slice(doc.content, 0, 0);
    view.dispatch(view.state.tr.replaceSelection(slice).scrollIntoView());
    return true;
  } catch {
    return false;
  }
}

function handlePaste(latest: Latest, view: EditorView, event: ClipboardEvent): boolean {
  const { isBusy, uploads } = latest();
  if (isBusy) {
    event.preventDefault();
    return true;
  }
  const data = event.clipboardData;
  if (data === null) return false;
  const text = data.getData('text/plain');
  // 剪貼簿同時有文字與檔案（試算表、Word、網頁複製）時，使用者要的是文字：不上傳（和原本的文字框一樣）。
  if (uploads !== undefined && data.files.length > 0 && text === '') {
    uploads.binding.handlers.onPaste(event);
    return true;
  }
  if (text === '' || data.getData('text/html') !== '') return false;
  // 回傳 true 時 ProseMirror 自己會 preventDefault。
  return pasteMarkdown(view, text);
}

/** 拖進來的檔案：先把游標放到放開的位置，再交給上傳（在游標的位置放「上傳中」的記號）。編輯器自己搬動的內容照 ProseMirror 的做法。 */
function handleDrop(latest: Latest, view: EditorView, event: DragEvent, moved: boolean): boolean {
  const { isBusy, uploads } = latest();
  if (moved) return false;
  if (isBusy) {
    event.preventDefault();
    return true;
  }
  if (uploads === undefined || event.dataTransfer === null || event.dataTransfer.files.length === 0) return false;
  const target = view.posAtCoords({ left: event.clientX, top: event.clientY });
  if (target !== null) view.dispatch(view.state.tr.setSelection(TextSelection.near(view.state.doc.resolve(target.pos))));
  uploads.binding.handlers.onDrop(event);
  return true;
}

/** 行內的葉節點寫成純文字：換行是 \n，提及是 @名稱，附件是檔名，圖片是 Markdown 的寫法，原始的行內 HTML 是原文。 */
function leafPlainText(node: ProseNode): string {
  const attrs = node.attrs as Record<string, unknown>;
  switch (node.type.name) {
    case 'hardBreak':
      return '\n';
    case 'mention':
      return `@${String(attrs.label ?? attrs.id)}`;
    case 'attachmentFile':
      return String(attrs.name);
    case 'image':
      return `![${String(attrs.alt)}](${String(attrs.src)})`;
    case 'rawInline':
      return String(attrs.source);
    default:
      return '';
  }
}

/** 選取的頭尾落在同一個文字區塊（含程式碼區塊）時，切片是一串單一子節點的包裝（清單、引用、儲存格）一路開口到文字區塊；回傳那個文字區塊。 */
function textblockOfSlice(slice: Slice): ProseNode | null {
  if (slice.openStart === 0 || slice.openStart !== slice.openEnd) return null;
  let node: ProseNode | null | undefined = slice.content.childCount === 1 ? slice.content.firstChild : null;
  for (let depth = 0; node != null && depth < slice.openStart - 1; depth += 1) {
    node = node.childCount === 1 ? node.firstChild : null;
  }
  return node?.isTextblock === true ? node : null;
}

/**
 * 複製時純文字放 Markdown（HTML 照 Tiptap 的）。結構太零碎寫不出 Markdown 時退回純文字。
 * 只選了同一個文字區塊裡的幾個字時放那幾個字的純文字：照 Markdown 寫的話會帶上 `- `、`# `、`> ` 與 `\*` 這些記號，貼到別處是雜訊。
 */
function serializeClipboardText(slice: Slice): string {
  const textblock = textblockOfSlice(slice);
  if (textblock !== null) return textblock.textBetween(0, textblock.content.size, '', leafPlainText);
  try {
    return contentToMarkdown((slice.content.toJSON() as readonly JSONContent[] | null) ?? []);
  } catch {
    return slice.content.textBetween(0, slice.content.size, '\n\n');
  }
}

/** 補一個空的交易：輸入法選字結束後，提及的外掛才會再看一次游標前面是不是 @。 */
function refreshAfterComposition(view: EditorView): boolean {
  setTimeout(() => {
    if (!view.isDestroyed) view.dispatch(view.state.tr);
  }, 0);
  return false;
}

export function createEditorProps(latest: Latest): EditorProps {
  return {
    handleKeyDown: (view, event) => handleKeyDown(latest, view, event),
    handlePaste: (view, event) => handlePaste(latest, view, event),
    handleDrop: (view, event, _slice, moved) => handleDrop(latest, view, event, moved),
    clipboardTextSerializer: serializeClipboardText,
    handleDOMEvents: { compositionend: refreshAfterComposition },
  };
}
