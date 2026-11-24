import type { Extensions } from '@tiptap/core';
import { TaskList } from '@tiptap/extension-list';
import { Mention } from '@tiptap/extension-mention';
import { TableRow } from '@tiptap/extension-table';
import { Placeholder } from '@tiptap/extensions';
import StarterKit from '@tiptap/starter-kit';
import type { SuggestionOptions } from '@tiptap/suggestion';
import type { Member } from '../../api/types';
import { isAllowedUrl } from '../../components/Markdown/markdownUrls';
import markdownStyles from '../../components/Markdown/Markdown.module.css';
import { EditorBehavior } from './extensions/editorBehavior';
import type { MentionChoice } from './extensions/mentionSuggestion';
import { EditorListItem, EditorTaskItem } from './extensions/listKeys';
import { AlignedTableCell, AlignedTableHeader, EditorTable, SchemaAttributes } from './extensions/schemaExtensions';
import { AttachmentFileNode, ImageNode, UploadPlaceholderNode } from './nodes/mediaNodes';
import { RawBlock, RawInline } from './nodes/rawNodes';
import styles from './RichTextEditor.module.css';

export interface ExtensionOptions {
  readonly isBusy: () => boolean;
  readonly suggestion: Omit<SuggestionOptions<Member, MentionChoice>, 'editor'>;
  readonly placeholder: string | undefined;
}

/**
 * 編輯器的共用選項（正式的編輯器與測試用同一份）。貼上規則關掉：貼上的內容要原樣進來，
 * Tiptap 的貼上規則會把 `2 * 3 * 4`、網址後面的全形標點改成斜體與連結，存出去的原文就變了；打字時的輸入規則照常。
 */
export const EDITOR_BEHAVIOR_OPTIONS = { injectCSS: false, enablePasteRules: false } as const;

/** 連結只認 http、https、mailto（和顯示時的白名單不同：不收提及與附件，那兩種由自己的節點管）。 */
const EDITOR_LINK_URL = /^(https?:|mailto:)/i;

function mentionExtension(suggestion: ExtensionOptions['suggestion']) {
  return Mention.configure({
    HTMLAttributes: { class: markdownStyles.mention },
    // 和顯示時一樣是「@名稱」（@ 在存的 Markdown 裡是前面的文字，見合約 4.3）。
    renderText: ({ node }) => `@${String(node.attrs.label ?? node.attrs.id)}`,
    suggestion,
  });
}

/**
 * 編輯器的文件結構（internal/HANDOFF-rich-text-editor.md 4.1、4.2）：Tiptap 的標準節點加上合約多出來的屬性與節點。
 * 底線關掉（Markdown 寫不出來，存了會不見）；連結不自動辨識、點了不開（編輯器裡點連結是放游標）、只收 http、https、mailto。
 * 待辦項目可以巢狀（內容是 paragraph block*）。
 */
export function createExtensions({ isBusy, suggestion, placeholder }: ExtensionOptions): Extensions {
  return [
    StarterKit.configure({
      underline: false,
      listItem: false,
      link: {
        openOnClick: false,
        autolink: false,
        linkOnPaste: false,
        isAllowedUri: (url) => EDITOR_LINK_URL.test(url) && isAllowedUrl(url),
      },
    }),
    EditorListItem,
    TaskList,
    EditorTaskItem.configure({
      nested: true,
      a11y: { checkboxLabel: (node, checked) => `${checked ? '已完成' : '未完成'}：${node.textContent === '' ? '空的待辦' : node.textContent}` },
    }),
    EditorTable,
    TableRow,
    AlignedTableHeader,
    AlignedTableCell,
    mentionExtension(suggestion),
    ImageNode,
    AttachmentFileNode,
    UploadPlaceholderNode,
    RawBlock,
    RawInline,
    SchemaAttributes,
    EditorBehavior.configure({ isBusy }),
    ...(placeholder === undefined
      ? []
      : [Placeholder.configure({ placeholder, emptyEditorClass: styles.emptyEditor, showOnlyCurrent: false })]),
  ];
}
