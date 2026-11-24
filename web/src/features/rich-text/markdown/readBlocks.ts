import type { JSONContent } from '@tiptap/core';
import type { Definition, List, ListItem, Root, RootContent, Table } from 'mdast';
import { docNode, emptyParagraph } from './docTypes';
import { inlineOf, mergeText, sourceOf, type ReadContext } from './readInline';

type MdNode = RootContent;

/** 整份文件裡用得到的定義（以編號為鍵）。 */
export function readContextOf(root: Root, source: string): ReadContext {
  const definitions = new Map<string, Definition>();
  const used = new Set<string>();
  const walk = (node: MdNode | Root): void => {
    if (node.type === 'definition' && !definitions.has(node.identifier)) definitions.set(node.identifier, node);
    if (node.type === 'linkReference' || node.type === 'imageReference') used.add(node.identifier);
    // 註腳的定義是原文照存（rawBlock），裡面的參照不會換成連結，所以不算「有用到」：算了的話連結的定義會被拿掉。
    if (node.type === 'footnoteDefinition') return;
    if ('children' in node) node.children.forEach((child) => { walk(child); });
  };
  walk(root);
  // 沒被用到的定義不放進去：blockOf 看不到定義就知道是沒用到的，變成 rawBlock。
  const usedDefinitions = new Map([...definitions].filter(([id]) => used.has(id)));
  return { source, definitions: usedDefinitions };
}

export function blocksOf(nodes: readonly MdNode[], ctx: ReadContext): JSONContent[] {
  return nodes.flatMap((node) => blockOf(node, ctx));
}

/** 清單項目與引用的內容不能是空的；清單項目還要以段落開頭（合約 4.1、4.3）。 */
function itemContent(children: readonly MdNode[], ctx: ReadContext, startsWithParagraph: boolean): JSONContent[] {
  const blocks = blocksOf(children, ctx);
  if (blocks.length === 0) return [emptyParagraph()];
  return startsWithParagraph && blocks[0]?.type !== 'paragraph' ? [emptyParagraph(), ...blocks] : blocks;
}

function paragraphOf(node: Extract<MdNode, { type: 'paragraph' }>, ctx: ReadContext): JSONContent {
  return docNode('paragraph', undefined, inlineOf(node.children, ctx));
}

function cellParagraph(children: Parameters<typeof inlineOf>[0], ctx: ReadContext): JSONContent {
  return docNode('paragraph', undefined, inlineOf(children, ctx, 'space'));
}

function tableOf(node: Table, ctx: ReadContext): JSONContent {
  const rows = node.children.map((row, rowIndex) =>
    docNode(
      'tableRow',
      undefined,
      row.children.map((cell, column) =>
        docNode(rowIndex === 0 ? 'tableHeader' : 'tableCell', { align: node.align?.[column] ?? null }, [cellParagraph(cell.children, ctx)]),
      ),
    ),
  );
  return docNode('table', undefined, rows);
}

/** 混著的清單：有勾選框的那幾項在段落最前面補上文字 `[x] `，才不會丟掉勾選的狀態。 */
function taskPrefixed(item: ListItem, content: JSONContent[]): JSONContent[] {
  const [first, ...rest] = content;
  // 勾選框只會出現在第一個段落的開頭（micromark 只認那裡），所以有勾選框的項目一定以段落開頭。
  if (typeof item.checked !== 'boolean' || first?.type !== 'paragraph') return content;
  const prefix: JSONContent = { type: 'text', text: item.checked ? '[x] ' : '[ ] ' };
  return [{ ...first, content: mergeText([prefix, ...(first.content ?? [])]) }, ...rest];
}

function listOf(node: List, ctx: ReadContext): JSONContent {
  const spread = node.spread ?? false;
  const isTask = node.children.length > 0 && node.children.every((item) => typeof item.checked === 'boolean');
  const items = node.children.map((item) => {
    const content = itemContent(item.children, ctx, true);
    return isTask
      ? docNode('taskItem', { checked: item.checked === true }, content)
      : docNode('listItem', undefined, taskPrefixed(item, content));
  });
  if (isTask) return docNode('taskList', { spread }, items);
  if (node.ordered === true) return docNode('orderedList', { start: node.start ?? 1, spread }, items);
  return docNode('bulletList', { spread }, items);
}

function blockOf(node: MdNode, ctx: ReadContext): JSONContent[] {
  switch (node.type) {
    case 'paragraph':
      return [paragraphOf(node, ctx)];
    case 'heading':
      return [docNode('heading', { level: node.depth }, inlineOf(node.children, ctx, 'space'))];
    case 'blockquote':
      return [docNode('blockquote', undefined, itemContent(node.children, ctx, false))];
    case 'list':
      return [listOf(node, ctx)];
    case 'code':
      return [
        docNode('codeBlock', { language: node.lang ?? null, meta: node.meta ?? null }, node.value === '' ? [] : [{ type: 'text', text: node.value }]),
      ];
    case 'thematicBreak':
      return [docNode('horizontalRule')];
    case 'table':
      return [tableOf(node, ctx)];
    case 'html':
      return [docNode('rawBlock', { source: node.value })];
    case 'definition':
      // 被用到的定義已經換進連結裡；ctx 裡沒有這一個（沒人用，或同一個編號前面已經有定義）的才留成原文。
      return ctx.definitions.get(node.identifier) !== node ? [docNode('rawBlock', { source: sourceOf(node, ctx) })] : [];
    default:
      // 註腳的定義，或剖析器以後多給的區塊：原文照存。
      return [docNode('rawBlock', { source: sourceOf(node, ctx) })];
  }
}
