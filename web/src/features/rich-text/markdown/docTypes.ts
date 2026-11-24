import type { JSONContent } from '@tiptap/core';

/** 文件裡的標記（粗體、連結…）；JSONContent 沒有單獨匯出這個型別。 */
export type DocMark = NonNullable<JSONContent['marks']>[number];

export type Attrs = Record<string, unknown>;

/** 標記巢狀的固定順序：同一段文字有好幾個標記時，讀進來一律照這個排，兩份文件才比得出相等。 */
const MARK_ORDER: readonly string[] = ['link', 'bold', 'italic', 'strike', 'code'];

/** 內容是空的就不放 content 欄位（Tiptap 的 JSON 沒有空陣列的寫法）。 */
export function docNode(type: string, attrs?: Attrs, content?: readonly JSONContent[]): JSONContent {
  const node: JSONContent = { type };
  if (attrs !== undefined) node.attrs = attrs;
  if (content !== undefined && content.length > 0) node.content = [...content];
  return node;
}

export function emptyParagraph(): JSONContent {
  return docNode('paragraph');
}

export function markKey(mark: DocMark): string {
  return JSON.stringify([mark.type, mark.attrs ?? null]);
}

export function sameMark(left: DocMark, right: DocMark): boolean {
  return markKey(left) === markKey(right);
}

export function hasMark(marks: readonly DocMark[], mark: DocMark): boolean {
  return marks.some((candidate) => sameMark(candidate, mark));
}

export function marksOf(node: JSONContent): readonly DocMark[] {
  return node.marks ?? [];
}

function orderOf(mark: DocMark): number {
  const index = MARK_ORDER.indexOf(mark.type);
  return index === -1 ? MARK_ORDER.length : index;
}

/** 去掉重複並照固定順序排好。 */
export function sortedMarks(marks: readonly DocMark[]): DocMark[] {
  const unique = marks.filter((mark, index) => marks.findIndex((other) => sameMark(other, mark)) === index);
  return unique.sort((a, b) => orderOf(a) - orderOf(b));
}

export function withMarks(node: JSONContent, marks: readonly DocMark[]): JSONContent {
  const sorted = sortedMarks(marks);
  const copy: JSONContent = { ...node };
  delete copy.marks;
  return sorted.length === 0 ? copy : { ...copy, marks: sorted };
}

const INLINE_TYPES: ReadonlySet<string> = new Set([
  'text',
  'hardBreak',
  'mention',
  'image',
  'attachmentFile',
  'uploadPlaceholder',
  'rawInline',
]);

export function isInlineNode(node: JSONContent): boolean {
  return INLINE_TYPES.has(node.type ?? '');
}

/**
 * 換行前後共有的標記（code 除外：換行不能是程式碼）。換行自己的標記不算：
 * 讀與寫都用這一條算，換行才不會因為來源是 `*a*\n*b*` 還是 `*a\nb*` 而得到不同的標記，兩邊的文件才相等。
 */
export function breakMarks(nodes: readonly JSONContent[], index: number): DocMark[] {
  const before = nearestNonBreak(nodes, index, -1);
  const after = nearestNonBreak(nodes, index, 1);
  if (before === undefined || after === undefined) return [];
  const afterMarks = marksOf(after);
  return marksOf(before).filter((mark) => mark.type !== 'code' && hasMark(afterMarks, mark));
}

function nearestNonBreak(nodes: readonly JSONContent[], index: number, step: 1 | -1): JSONContent | undefined {
  for (let cursor = index + step; cursor >= 0 && cursor < nodes.length; cursor += step) {
    const candidate = nodes[cursor];
    if (candidate !== undefined && candidate.type !== 'hardBreak') return candidate;
  }
  return undefined;
}
