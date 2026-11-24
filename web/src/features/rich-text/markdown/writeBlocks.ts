import type { JSONContent } from '@tiptap/core';
import type { AlignType, List, ListItem, RootContent, Table, TableCell, TableRow } from 'mdast';
import { isInlineNode } from './docTypes';
import { inlineToMdast } from './writeInline';

function attr(node: JSONContent, name: string): unknown {
  return node.attrs?.[name];
}

function isEmptyParagraph(node: JSONContent): boolean {
  return node.type === 'paragraph' && (node.content ?? []).length === 0;
}

/** 空段落寫不出來（只會多出空行），拿掉。 */
export function blocksToMdast(nodes: readonly JSONContent[]): RootContent[] {
  return nodes.filter((node) => !isEmptyParagraph(node)).flatMap(blockToMdast);
}

/** 清單項目：待辦的第一個空段落要留著，勾選框是寫在第一個段落裡的。 */
function itemChildren(content: readonly JSONContent[], isTask: boolean): RootContent[] {
  const [first, ...rest] = content;
  if (!isTask || first === undefined || !isEmptyParagraph(first)) return blocksToMdast(content);
  return [{ type: 'paragraph', children: [] }, ...blocksToMdast(rest)];
}

function listItemOf(item: JSONContent, spread: boolean, isTask: boolean): ListItem {
  return {
    type: 'listItem',
    spread,
    checked: isTask ? attr(item, 'checked') === true : null,
    children: itemChildren(item.content ?? [], isTask) as ListItem['children'],
  };
}

/** 空的待辦項目寫不出來（`- [ ]` 後面沒有字會被讀成一般文字 `[ ]`），整項不寫。 */
function isEmptyTask(item: JSONContent): boolean {
  return (item.content ?? []).every(isEmptyParagraph);
}

function listOf(node: JSONContent, kind: 'bullet' | 'ordered' | 'task'): List[] {
  const spread = attr(node, 'spread') === true;
  const start = attr(node, 'start');
  const items = (node.content ?? []).filter((item) => kind !== 'task' || !isEmptyTask(item));
  if (kind === 'task' && items.length === 0) return [];
  return [{
    type: 'list',
    ordered: kind === 'ordered',
    start: kind === 'ordered' ? (typeof start === 'number' ? start : 1) : null,
    spread,
    children: items.map((item) => listItemOf(item, spread, kind === 'task')),
  }];
}

/** 儲存格裡的行內內容：段落攤開；萬一有別的區塊（清單、引用、巢狀表格），文字攤平，不要整格丟掉。 */
function cellInline(block: JSONContent): JSONContent[] {
  if (isInlineNode(block)) return [block];
  const content = block.content ?? [];
  if (block.type === 'paragraph' || content.every(isInlineNode)) return content;
  return content.flatMap((child, index) => (index === 0 ? cellInline(child) : [{ type: 'text', text: ' ' }, ...cellInline(child)]));
}

function cellOf(cell: JSONContent): TableCell {
  // 儲存格寫不出多個段落與換行：段落用空白接起來，換行換成空白。
  const paragraphs = (cell.content ?? []).map(cellInline);
  const inline = paragraphs.flatMap((nodes, index) => (index === 0 ? nodes : [{ type: 'text', text: ' ' }, ...nodes]));
  return { type: 'tableCell', children: inlineToMdast(inline, 'space') };
}

function alignOf(cell: JSONContent | undefined): AlignType {
  const align = cell === undefined ? null : attr(cell, 'align');
  return align === 'left' || align === 'center' || align === 'right' ? align : null;
}

function tableOf(node: JSONContent): Table[] {
  if ((node.content ?? []).length === 0) return [];
  const rows = (node.content ?? []).map((row): TableRow => ({ type: 'tableRow', children: (row.content ?? []).map(cellOf) }));
  const header = node.content?.[0]?.content ?? [];
  return [{ type: 'table', align: header.map(alignOf), children: rows }];
}

function codeText(node: JSONContent): string {
  return (node.content ?? []).map((child) => child.text ?? '').join('');
}

function stringOrNull(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

function headingDepth(node: JSONContent): 1 | 2 | 3 | 4 | 5 | 6 {
  const level = attr(node, 'level');
  return level === 1 || level === 2 || level === 3 || level === 4 || level === 5 || level === 6 ? level : 1;
}

function blockToMdast(node: JSONContent): RootContent[] {
  switch (node.type) {
    case 'paragraph':
      return [{ type: 'paragraph', children: inlineToMdast(node.content ?? []) }];
    case 'heading':
      return [{ type: 'heading', depth: headingDepth(node), children: inlineToMdast(node.content ?? [], 'space') }];
    case 'blockquote':
      return [{ type: 'blockquote', children: blocksToMdast(node.content ?? []) as never }];
    case 'bulletList':
      return listOf(node, 'bullet');
    case 'orderedList':
      return listOf(node, 'ordered');
    case 'taskList':
      return listOf(node, 'task');
    case 'codeBlock':
      return [{ type: 'code', lang: stringOrNull(attr(node, 'language')), meta: stringOrNull(attr(node, 'meta')), value: codeText(node) }];
    case 'horizontalRule':
      return [{ type: 'thematicBreak' }];
    case 'table':
      return tableOf(node);
    case 'rawBlock':
      return [{ type: 'html', value: typeof attr(node, 'source') === 'string' ? (attr(node, 'source') as string) : '' }];
    default:
      return unknownBlock(node);
  }
}

/** 不認得的區塊：有行內內容就當段落，不然把它裡面的區塊攤開，內容不會悄悄不見。 */
function unknownBlock(node: JSONContent): RootContent[] {
  const content = node.content ?? [];
  if (isInlineNode(node)) return [{ type: 'paragraph', children: inlineToMdast([node]) }];
  if (content.some(isInlineNode)) return [{ type: 'paragraph', children: inlineToMdast(content) }];
  return blocksToMdast(content);
}
