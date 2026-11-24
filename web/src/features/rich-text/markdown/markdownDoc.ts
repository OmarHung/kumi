import type { JSONContent } from '@tiptap/core';
import type { Root } from 'mdast';
import { docNode, emptyParagraph } from './docTypes';
import { parseMarkdown, stringifyMdast } from './mdastIo';
import { blocksOf, readContextOf } from './readBlocks';
import { blocksToMdast } from './writeBlocks';

/**
 * Markdown 與編輯器的文件（Tiptap 的 JSON）互相轉換。存的永遠是 Markdown，
 * 文件的結構、正規化的規矩見 internal/HANDOFF-rich-text-editor.md 第 4 節。純函式，不碰 DOM 與編輯器。
 */
export function markdownToDoc(markdown: string): JSONContent {
  const source = markdown.replace(/\r\n?/g, '\n');
  const root = parseMarkdown(source);
  const blocks = blocksOf(root.children, readContextOf(root, source));
  return docNode('doc', undefined, blocks.length === 0 ? [emptyParagraph()] : blocks);
}

export function docToMarkdown(doc: JSONContent): string {
  const root: Root = { type: 'root', children: blocksToMdast(doc.content ?? []) };
  return stringifyMdast(root).replace(/\n+$/, '');
}

/** 一段內容（剪貼簿複製時）：包成文件再寫。 */
export function contentToMarkdown(content: readonly JSONContent[]): string {
  return docToMarkdown(docNode('doc', undefined, content));
}
