import type { Root, Text } from 'mdast';
import { gfmFootnoteFromMarkdown, gfmFootnoteToMarkdown } from 'mdast-util-gfm-footnote';
import { gfmStrikethroughFromMarkdown, gfmStrikethroughToMarkdown } from 'mdast-util-gfm-strikethrough';
import { gfmTableFromMarkdown, gfmTableToMarkdown } from 'mdast-util-gfm-table';
import { gfmTaskListItemFromMarkdown, gfmTaskListItemToMarkdown } from 'mdast-util-gfm-task-list-item';
import { fromMarkdown } from 'mdast-util-from-markdown';
import { defaultHandlers, toMarkdown, type Handle, type Options } from 'mdast-util-to-markdown';
import { gfmFootnote } from 'micromark-extension-gfm-footnote';
import { gfmStrikethrough } from 'micromark-extension-gfm-strikethrough';
import { gfmTable } from 'micromark-extension-gfm-table';
import { gfmTaskListItem } from 'micromark-extension-gfm-task-list-item';

/**
 * 不用 micromark-extension-gfm／mdast-util-gfm：它們包含 GFM 的自動連結，
 * 讀進來會把單獨的網址變成連結，存回去變 [網址](網址)，連 `https:` 都被跳脫（internal/HANDOFF-rich-text-editor.md 4.3、4.4）。
 */
export function parseMarkdown(markdown: string): Root {
  return fromMarkdown(markdown, {
    extensions: [gfmStrikethrough({ singleTilde: true }), gfmTable(), gfmTaskListItem(), gfmFootnote()],
    mdastExtensions: [
      gfmStrikethroughFromMarkdown(),
      gfmTableFromMarkdown(),
      gfmTaskListItemFromMarkdown(),
      gfmFootnoteFromMarkdown(),
    ],
  });
}

/** 後面不是 `;` 結尾的 `&` 不是字元參照：網址裡的 `&y=2` 不用跳脫成 `\&y=2`（mdast 一律跳脫，CLI 與代理讀到的網址會變醜）。 */
const NEEDLESS_AMPERSAND_ESCAPE = /\\&(?![#A-Za-z0-9]+;)/g;

const textHandler: Handle = (node: Text, parent, state, info) =>
  defaultHandlers.text(node, parent, state, info).replace(NEEDLESS_AMPERSAND_ESCAPE, '&');

export function stringifyMdast(tree: Root): string {
  return toMarkdown(tree, {
    extensions: [
      gfmStrikethroughToMarkdown(),
      // 不對齊直線：中文字寬不同，補空白對齊只會讓每次改一格就整張表都變。
      gfmTableToMarkdown({ tablePipeAlign: false }),
      gfmTaskListItemToMarkdown(),
      gfmFootnoteToMarkdown(),
    ],
    // 上傳中的記號與換行要原樣輸出（writeInline 的 rawText）。
    handlers: { text: textHandler, rawText: (node: { value: string }) => node.value } as unknown as Options['handlers'],
    bullet: '-',
    emphasis: '*',
    strong: '*',
    rule: '-',
    fence: '`',
    fences: true,
    listItemIndent: 'one',
    setext: false,
    closeAtx: false,
    resourceLink: false,
  });
}
