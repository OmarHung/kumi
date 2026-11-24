import type { JSONContent } from '@tiptap/core';
import { docNode, type Attrs, type DocMark } from './docTypes';
import { docToMarkdown, markdownToDoc } from './markdownDoc';

/** 測試用的假 GUID 與人員編號（全是假的）。 */
export const GUID = '0f8fad5b-d9cb-469f-a165-70867728950e';
export const PERSON = '3b241101-e2bb-4255-8caf-4136c566a962';

export const bold: DocMark = { type: 'bold' };
export const italic: DocMark = { type: 'italic' };
export const strike: DocMark = { type: 'strike' };
export const code: DocMark = { type: 'code' };
export const link = (href: string, title: string | null = null): DocMark => ({ type: 'link', attrs: { href, title } });

export function doc(...content: JSONContent[]): JSONContent {
  return docNode('doc', undefined, content);
}

export function p(...content: JSONContent[]): JSONContent {
  return docNode('paragraph', undefined, content);
}

export function t(text: string, ...marks: DocMark[]): JSONContent {
  return marks.length === 0 ? { type: 'text', text } : { type: 'text', text, marks };
}

export const br: JSONContent = { type: 'hardBreak' };

export function node(type: string, attrs: Attrs | undefined, ...content: JSONContent[]): JSONContent {
  return docNode(type, attrs, content);
}

export function firstBlock(markdown: string): JSONContent {
  const first = markdownToDoc(markdown).content?.[0];
  if (first === undefined) throw new Error('文件是空的');
  return first;
}

/** 讀成文件，再寫回 Markdown。 */
export function roundTrip(markdown: string): string {
  return docToMarkdown(markdownToDoc(markdown));
}

/** 讀、寫、再讀，兩次讀到的文件要一樣。 */
export function expectStable(markdown: string): { first: JSONContent; written: string } {
  const first = markdownToDoc(markdown);
  const written = docToMarkdown(first);
  const second = markdownToDoc(written);
  if (JSON.stringify(second) !== JSON.stringify(first)) {
    throw new Error(`不穩定：\n原文 ${JSON.stringify(markdown)}\n寫回 ${JSON.stringify(written)}\n第一次 ${JSON.stringify(first)}\n第二次 ${JSON.stringify(second)}`);
  }
  return { first, written };
}
