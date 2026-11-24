import type { JSONContent } from '@tiptap/core';
import type { Definition, Link, PhrasingContent, RootContent } from 'mdast';
import { attachmentIdOf, isMentionUrl, mentionIdOf } from '../../../components/Markdown/markdownUrls';
import { breakMarks, docNode, markKey, marksOf, withMarks, type DocMark } from './docTypes';

/** 讀的時候要用到的：原文（切不認得的寫法用）與參照式連結的定義。 */
export interface ReadContext {
  readonly source: string;
  readonly definitions: ReadonlyMap<string, Definition>;
}

/** 行內的換行怎麼處理：標題與表格的儲存格寫不出換行，一律變成空白。 */
export type NewlineMode = 'break' | 'space';

/** 從原文的位置切出來；沒有位置（不是從文字剖析來的）時用 fallback。 */
export function sourceOf(node: RootContent, ctx: ReadContext, fallback = ''): string {
  const start = node.position?.start.offset;
  const end = node.position?.end.offset;
  return start === undefined || end === undefined ? fallback : ctx.source.slice(start, end);
}

/** 節點底下看得到的純文字（提及的名稱、附件的檔名）。 */
export function plainText(node: { readonly type: string; readonly value?: string; readonly alt?: string | null; readonly children?: readonly unknown[] }): string {
  if (node.type === 'image') return node.alt ?? '';
  if (node.children === undefined) return node.value ?? '';
  return node.children.map((child) => plainText(child as Parameters<typeof plainText>[0])).join('');
}

/** 參照式的連結與圖片用定義換成一般的寫法；定義不存在（剖析器不會給）就原樣交回。 */
function resolveReference(node: PhrasingContent, ctx: ReadContext): PhrasingContent {
  if (node.type !== 'linkReference' && node.type !== 'imageReference') return node;
  const definition = ctx.definitions.get(node.identifier);
  if (definition === undefined) return node;
  if (node.type === 'linkReference') {
    return { type: 'link', url: definition.url, title: definition.title ?? null, children: node.children };
  }
  return { type: 'image', url: definition.url, title: definition.title ?? null, alt: node.alt ?? '' };
}

function isMentionLink(node: PhrasingContent | undefined, ctx: ReadContext): node is Link {
  if (node === undefined) return false;
  const resolved = resolveReference(node, ctx);
  return resolved.type === 'link' && isMentionUrl(resolved.url);
}

function textNodes(value: string, marks: readonly DocMark[], newline: NewlineMode): JSONContent[] {
  if (newline === 'space') return value === '' ? [] : [withMarks({ type: 'text', text: value.replace(/\n/g, ' ') }, marks)];
  const lines = value.split('\n');
  return lines.flatMap((line, index): JSONContent[] => {
    const text: JSONContent[] = line === '' ? [] : [withMarks({ type: 'text', text: line }, marks)];
    return index === 0 ? text : [{ type: 'hardBreak' }, ...text];
  });
}

function collectLink(node: Link, ctx: ReadContext, marks: readonly DocMark[], newline: NewlineMode): JSONContent[] {
  if (isMentionUrl(node.url)) {
    return [withMarks(docNode('mention', { id: mentionIdOf(node.url), label: plainText(node) }), marks)];
  }
  const attachmentId = attachmentIdOf(node.url);
  // 連結的內容全是文字才是附件檔案；有圖片、粗體這些就當一般的連結（網址照原文），不然內容會被壓平成檔名。
  if (attachmentId !== null && node.children.every((child) => child.type === 'text')) {
    return [withMarks(docNode('attachmentFile', { attachmentId, name: plainText(node) }), marks)];
  }
  const link: DocMark = { type: 'link', attrs: { href: node.url, title: node.title ?? null } };
  return collect(node.children, ctx, [...marks, link], newline);
}

function collectOne(raw: PhrasingContent, next: PhrasingContent | undefined, ctx: ReadContext, marks: readonly DocMark[], newline: NewlineMode): JSONContent[] {
  const node = resolveReference(raw, ctx);
  switch (node.type) {
    case 'text': {
      // @[名稱](mention:編號) 的 @ 屬於提及，前面文字結尾的 @ 拿掉（和 remarkMentions 一樣）。
      const value = isMentionLink(next, ctx) && node.value.endsWith('@') ? node.value.slice(0, -1) : node.value;
      return textNodes(value, marks, newline);
    }
    case 'emphasis':
      return collect(node.children, ctx, [...marks, { type: 'italic' }], newline);
    case 'strong':
      return collect(node.children, ctx, [...marks, { type: 'bold' }], newline);
    case 'delete':
      return collect(node.children, ctx, [...marks, { type: 'strike' }], newline);
    case 'inlineCode':
      return [withMarks({ type: 'text', text: node.value }, [...marks, { type: 'code' }])];
    case 'break':
      return newline === 'break' ? [{ type: 'hardBreak' }] : [{ type: 'text', text: ' ' }];
    case 'link':
      return collectLink(node, ctx, marks, newline);
    case 'image':
      return [withMarks(docNode('image', { src: node.url, alt: node.alt ?? '', title: node.title ?? null }), marks)];
    case 'html':
      return [withMarks(docNode('rawInline', { source: node.value }), marks)];
    default:
      // 註腳的參照，或剖析器以後多給的寫法：原文照存。
      return [docNode('rawInline', { source: sourceOf(node, ctx, plainText(node)) })];
  }
}

function collect(nodes: readonly PhrasingContent[], ctx: ReadContext, marks: readonly DocMark[], newline: NewlineMode): JSONContent[] {
  return nodes.flatMap((child, index) => collectOne(child, nodes[index + 1], ctx, marks, newline));
}

/** 相鄰、標記相同的文字併成一個（ProseMirror 的文件也是這樣）。 */
export function mergeText(nodes: readonly JSONContent[]): JSONContent[] {
  return nodes.reduce<JSONContent[]>((merged, node) => {
    const last = merged[merged.length - 1];
    const canMerge = last?.type === 'text' && node.type === 'text' && markSignature(last) === markSignature(node);
    if (!canMerge) return [...merged, node];
    return [...merged.slice(0, -1), { ...last, text: `${last.text ?? ''}${node.text ?? ''}` }];
  }, []);
}

function markSignature(node: JSONContent): string {
  return marksOf(node).map(markKey).join('|');
}

/** 換行的標記由前後文字決定（見 breakMarks），讀與寫共用同一條規則。 */
function settleBreaks(nodes: readonly JSONContent[]): JSONContent[] {
  return nodes.map((node, index) => (node.type === 'hardBreak' ? withMarks(node, breakMarks(nodes, index)) : node));
}

/** 段落、標題、儲存格的行內內容。 */
export function inlineOf(nodes: readonly PhrasingContent[], ctx: ReadContext, newline: NewlineMode = 'break'): JSONContent[] {
  return settleBreaks(mergeText(collect(nodes, ctx, [], newline)));
}
