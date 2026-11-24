import type { JSONContent } from '@tiptap/core';
import type { Link, PhrasingContent, Text } from 'mdast';
import { breakMarks, hasMark, isInlineNode, marksOf, sameMark, sortedMarks, type DocMark } from './docTypes';
import type { NewlineMode } from './readInline';

/** 提及的名稱放進連結文字：拿掉會弄壞連結寫法的字（和 mentionText.ts 的 linkText 同一條規矩）。 */
const BREAKS_LINK = /[[\]\\\r\n`*_<]/g;
const FALLBACK_MENTION_NAME = '成員';

export function mentionLabel(label: string): string {
  const text = label.replace(BREAKS_LINK, '').trim();
  return text === '' ? FALLBACK_MENTION_NAME : text;
}

/** 一個行內的文件節點寫成 mdast 之前的中間樣子：要寫的節點，加上外面包著哪些標記。 */
interface Item {
  readonly leaves: readonly PhrasingContent[];
  readonly marks: readonly DocMark[];
  readonly isBreak: boolean;
}

/** 原樣輸出的文字（mdastIo 的 handlers 接）：不跳脫，也不當 HTML 處理。 */
function rawText(value: string): PhrasingContent {
  return { type: 'rawText', value } as unknown as PhrasingContent;
}

function text(value: string): Text {
  return { type: 'text', value };
}

function str(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function withoutLinks(marks: readonly DocMark[]): DocMark[] {
  return marks.filter((mark) => mark.type !== 'link');
}

function leafItem(node: JSONContent, marks: readonly DocMark[]): Item {
  const attrs = node.attrs ?? {};
  switch (node.type) {
    case 'mention': {
      const link: Link = { type: 'link', url: `mention:${str(attrs.id)}`, children: [text(mentionLabel(str(attrs.label)))] };
      return { leaves: [text('@'), link], marks, isBreak: false };
    }
    case 'attachmentFile': {
      const link: Link = { type: 'link', url: `attachment:${str(attrs.attachmentId)}`, children: [text(str(attrs.name))] };
      // 連結不能包連結：附件檔案本身就是連結，外面的連結標記拿掉。
      return { leaves: [link], marks: withoutLinks(marks), isBreak: false };
    }
    case 'image': {
      const title = typeof attrs.title === 'string' ? attrs.title : null;
      return { leaves: [{ type: 'image', url: str(attrs.src), alt: str(attrs.alt), title }], marks, isBreak: false };
    }
    case 'rawInline':
      return { leaves: [{ type: 'html', value: str(attrs.source) }], marks, isBreak: false };
    case 'uploadPlaceholder':
      return { leaves: [rawText(str(attrs.placeholder))], marks: [], isBreak: false };
    default:
      return textItem(node, marks);
  }
}

function textItem(node: JSONContent, marks: readonly DocMark[]): Item {
  const value = node.text ?? '';
  if (marks.some((mark) => mark.type === 'code')) {
    return { leaves: [{ type: 'inlineCode', value }], marks: marks.filter((mark) => mark.type !== 'code'), isBreak: false };
  }
  return { leaves: [text(value)], marks, isBreak: false };
}

function itemsOf(nodes: readonly JSONContent[]): Item[] {
  return nodes.filter(isInlineNode).map((node, index, inline) => {
    if (node.type === 'hardBreak') return { leaves: [], marks: breakMarks(inline, index), isBreak: true };
    return leafItem(node, sortedMarks(marksOf(node)));
  });
}

/** 段落開頭與結尾的換行寫不出來（結尾的換行會被讀成沒有），拿掉。 */
function trimBreaks(items: readonly Item[]): Item[] {
  const first = items.findIndex((item) => !item.isBreak);
  if (first === -1) return [];
  const last = items.length - [...items].reverse().findIndex((item) => !item.isBreak);
  return items.slice(first, last);
}

/** 換行寫成單一的 \n（用 rawText：html 節點接在換行後面時 mdast 會把換行改成空白）；連續的換行第二個起寫成「反斜線加換行」，不然兩個 \n 之間是空行，會把段落切成兩段。 */
function withBreakLeaves(items: readonly Item[], newline: NewlineMode): Item[] {
  return items.map((item, index) => {
    if (!item.isBreak) return item;
    if (newline === 'space') return { ...item, leaves: [text(' ')] };
    return { ...item, leaves: [items[index - 1]?.isBreak === true ? { type: 'break' } : rawText('\n')] };
  });
}

function wrapperOf(mark: DocMark, children: PhrasingContent[]): PhrasingContent {
  switch (mark.type) {
    case 'bold':
      return { type: 'strong', children };
    case 'italic':
      return { type: 'emphasis', children };
    case 'strike':
      return { type: 'delete', children };
    default: {
      const attrs = mark.attrs ?? {};
      const title = typeof attrs.title === 'string' ? attrs.title : null;
      return { type: 'link', url: str(attrs.href), title, children };
    }
  }
}

/** 標記是否值得包起來：不認得的標記（底線等，Markdown 寫不出來）直接略過。 */
function isWritableMark(mark: DocMark): boolean {
  return ['link', 'bold', 'italic', 'strike'].includes(mark.type);
}

function runLength(items: readonly Item[], start: number, mark: DocMark): number {
  let end = start;
  while (end < items.length && hasMark(items[end]?.marks ?? [], mark)) end += 1;
  return end - start;
}

/** 從最長的一段開始包：`*a **b***` 的 a、b 都有斜體，b 另外有粗體，就是斜體在外、粗體在內。同長時照固定順序（連結在最外）。 */
function nest(items: readonly Item[]): PhrasingContent[] {
  const out: PhrasingContent[] = [];
  let index = 0;
  while (index < items.length) {
    const item = items[index];
    if (item === undefined) break;
    const marks = item.marks.filter(isWritableMark);
    if (marks.length === 0) {
      out.push(...item.leaves);
      index += 1;
      continue;
    }
    const best = marks.reduce((winner, mark) => (runLength(items, index, mark) > runLength(items, index, winner) ? mark : winner));
    const length = runLength(items, index, best);
    const inner = items.slice(index, index + length).map((member) => ({ ...member, marks: member.marks.filter((mark) => !sameMark(mark, best)) }));
    out.push(wrapperOf(best, nest(inner)));
    index += length;
  }
  return out;
}

function mergeTexts(nodes: readonly PhrasingContent[]): PhrasingContent[] {
  const merged = nodes.reduce<PhrasingContent[]>((result, node) => {
    const last = result[result.length - 1];
    if (last?.type === 'text' && node.type === 'text') return [...result.slice(0, -1), text(last.value + node.value)];
    return [...result, node];
  }, []);
  return merged.map((node) => ('children' in node ? { ...node, children: mergeTexts(node.children) } : node));
}

const EDGE_SPACE = /^(\s*)([\s\S]*?)(\s*)$/;

/** 粗斜體、刪除線的開頭與結尾不能是空白（`** 字**` 不是粗體）：空白移到標記外面。 */
function hoistSpaces(nodes: readonly PhrasingContent[]): PhrasingContent[] {
  return nodes.flatMap((node): PhrasingContent[] => {
    if (node.type === 'link') return [{ ...node, children: hoistSpaces(node.children) }];
    if (node.type !== 'strong' && node.type !== 'emphasis' && node.type !== 'delete') return [node];
    const children = hoistSpaces(node.children);
    const first = children[0];
    const last = children[children.length - 1];
    const lead = first?.type === 'text' ? (EDGE_SPACE.exec(first.value)?.[1] ?? '') : '';
    const tail = last?.type === 'text' ? (EDGE_SPACE.exec(last.value)?.[3] ?? '') : '';
    const trimmed = children.map((child, index) => trimChild(child, index === 0 ? lead.length : 0, index === children.length - 1 ? tail.length : 0));
    const inner = trimmed.filter((child) => child.type !== 'text' || child.value !== '');
    const result: PhrasingContent[] = inner.length === 0 ? [] : [{ ...node, children: inner }];
    return [...(lead === '' ? [] : [text(lead)]), ...result, ...(tail === '' ? [] : [text(tail)])];
  });
}

function trimChild(child: PhrasingContent, lead: number, tail: number): PhrasingContent {
  if (child.type !== 'text') return child;
  return text(child.value.slice(lead, child.value.length - tail));
}

const PUNCTUATION = /^[\p{P}\p{S}]$/u;
const TRAILING_PUNCTUATION = /^([\s\S]*?)([\p{P}\p{S}]+)$/u;
const LEADING_PUNCTUATION = /^([\p{P}\p{S}]+)([\s\S]*)$/u;
const ONLY_PUNCTUATION = /^[\p{P}\p{S}]+$/u;

/** 字：不是空白也不是標點、符號（和 micromark 判斷標記能不能開關用的分類一樣）。 */
function isWordChar(char: string | undefined): boolean {
  return char !== undefined && !/^\s$/u.test(char) && !PUNCTUATION.test(char);
}

function edgeChar(node: PhrasingContent | undefined, side: 'first' | 'last'): string | undefined {
  if (node?.type !== 'text') return undefined;
  const chars = Array.from(node.value);
  return side === 'first' ? chars[0] : chars[chars.length - 1];
}

/** 標記內側結尾（或開頭）的標點：標記外面緊接著字時，標記關不起來（`**重點：**這` 不是粗體），整段標點移到標記外面。 */
function edgePunctuation(children: readonly PhrasingContent[], side: 'lead' | 'tail'): string {
  const node = side === 'lead' ? children[0] : children[children.length - 1];
  if (node?.type !== 'text' || ONLY_PUNCTUATION.test(node.value) && children.length === 1) return '';
  const match = (side === 'lead' ? LEADING_PUNCTUATION : TRAILING_PUNCTUATION).exec(node.value);
  const punctuation = side === 'lead' ? match?.[1] : match?.[2];
  const rest = side === 'lead' ? match?.[2] : match?.[1];
  if (punctuation === undefined || rest === undefined) return '';
  // 剩下的以空白起頭或結尾時不動：移出去之後標記還是關不起來。
  const restEdge = side === 'lead' ? Array.from(rest)[0] : Array.from(rest).pop();
  if (restEdge !== undefined && /^\s$/u.test(restEdge)) return '';
  return punctuation;
}

function stripEdge(children: readonly PhrasingContent[], side: 'lead' | 'tail', length: number): PhrasingContent[] {
  if (length === 0) return [...children];
  const index = side === 'lead' ? 0 : children.length - 1;
  return children
    .map((child, position) => {
      if (position !== index || child.type !== 'text') return child;
      return text(side === 'lead' ? child.value.slice(length) : child.value.slice(0, child.value.length - length));
    })
    .filter((child) => child.type !== 'text' || child.value !== '');
}

/**
 * 粗斜體、刪除線內側緊接標點、外側緊接字時（`**重點：**這是`），標記開關不起來，mdast 只好把外面那個字寫成字元參照（`&#x9019;`），
 * CLI 與代理讀到的原文就變醜了。和 hoistSpaces 同一個做法：標點移到標記外面。整段都是標點就不動。
 */
function hoistPunctuation(nodes: readonly PhrasingContent[]): PhrasingContent[] {
  return nodes.flatMap((node, index): PhrasingContent[] => {
    if (node.type === 'link') return [{ ...node, children: hoistPunctuation(node.children) }];
    if (node.type !== 'strong' && node.type !== 'emphasis' && node.type !== 'delete') return [node];
    const children = hoistPunctuation(node.children);
    const lead = isWordChar(edgeChar(nodes[index - 1], 'last')) ? edgePunctuation(children, 'lead') : '';
    const tail = isWordChar(edgeChar(nodes[index + 1], 'first')) ? edgePunctuation(children, 'tail') : '';
    if (lead === '' && tail === '') return [{ ...node, children }];
    const inner = stripEdge(stripEdge(children, 'lead', lead.length), 'tail', tail.length);
    return [...(lead === '' ? [] : [text(lead)]), ...(inner.length === 0 ? [] : [{ ...node, children: inner }]), ...(tail === '' ? [] : [text(tail)])];
  });
}

/** 一行的邊界：段落的頭尾，或寫成換行的節點（第一個是 rawText 的 \n，連續的第二個起是 break）。 */
function isLineEdge(node: PhrasingContent | undefined): boolean {
  if (node === undefined || node.type === 'break') return true;
  const raw = node as unknown as { readonly type: string; readonly value?: string };
  return raw.type === 'rawText' && raw.value === '\n';
}

/**
 * 每一行頭尾的半形空白與 tab 拿掉：Markdown 裡它們本來就沒有意義，留著的話 mdast 會寫成 `&#x20;`（才不會被吃掉），
 * CLI 與代理讀到的原文就多了一串怪字（提及後面補的那個空白最常見）。全形空白是字，不動。
 */
function trimLineEdges(nodes: readonly PhrasingContent[]): PhrasingContent[] {
  return nodes
    .map((node, index): PhrasingContent => {
      if (node.type !== 'text') return node;
      const head = isLineEdge(nodes[index - 1]) ? node.value.replace(/^[ \t]+/, '') : node.value;
      return text(isLineEdge(nodes[index + 1]) ? head.replace(/[ \t]+$/, '') : head);
    })
    .filter((node) => node.type !== 'text' || node.value !== '');
}

/** 段落、標題、儲存格的行內內容寫成 mdast。 */
export function inlineToMdast(nodes: readonly JSONContent[], newline: NewlineMode = 'break'): PhrasingContent[] {
  const items = withBreakLeaves(trimBreaks(itemsOf(nodes)), newline);
  return trimLineEdges(mergeTexts(hoistPunctuation(hoistSpaces(nest(items)))));
}
