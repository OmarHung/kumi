import type { Editor } from '@tiptap/core';
import { useEditor } from '@tiptap/react';
import { useEffect, useMemo, useState } from 'react';
import markdownStyles from '../../components/Markdown/Markdown.module.css';
import { cx } from '../../lib/cx';
import { createExtensions, EDITOR_BEHAVIOR_OPTIONS } from './editorExtensions';
import { createEditorProps, type EditorInteractions } from './editorInteractions';
import type { MentionAria } from './useEditorMention';
import { useLatest } from './useLatest';
import { markdownToDoc } from './markdown/markdownDoc';
import { useExternalValue, useMarkdownSync } from './useMarkdownSync';
import type { useEditorMention } from './useEditorMention';
import styles from './RichTextEditor.module.css';

export interface EditorAttributeInputs {
  readonly labelId: string;
  readonly describedBy: string;
  readonly isInvalid: boolean;
  readonly mentionAria: MentionAria;
}

interface AttributeValues extends EditorAttributeInputs {
  readonly isBusy: boolean;
  readonly placeholder: string | undefined;
}

/** 值是 undefined 的屬性不放（放了會寫成字串 "undefined"）。 */
function definedOnly(attributes: MentionAria): Record<string, string> {
  return Object.fromEntries(Object.entries(attributes).filter((entry): entry is [string, string] => entry[1] !== undefined));
}

/** 可編輯的元素上的屬性：textbox、標籤、說明、無效、唯讀（送出中）、提及的清單。 */
function attributesOf(inputs: AttributeValues): Record<string, string> {
  const { labelId, describedBy, isInvalid, isBusy, placeholder, mentionAria } = inputs;
  return {
    class: cx(markdownStyles.markdown, styles.editor),
    role: 'textbox',
    'aria-multiline': 'true',
    'aria-labelledby': labelId,
    'aria-describedby': describedBy,
    ...(isInvalid ? { 'aria-invalid': 'true' } : {}),
    ...(isBusy ? { 'aria-readonly': 'true' } : {}),
    ...(placeholder === undefined ? {} : { 'aria-placeholder': placeholder }),
    ...definedOnly(mentionAria),
  };
}

export interface RichTextEditorInputs {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly placeholder: string | undefined;
  readonly autoFocus: boolean | 'end';
  readonly isBusy: boolean;
  readonly interactions: EditorInteractions;
  readonly mention: ReturnType<typeof useEditorMention>;
  readonly attributes: EditorAttributeInputs;
}

/**
 * 建立 Tiptap 的編輯器（只建一次）：文件結構、事件處理、可編輯元素的屬性、和外面的 Markdown 同步。
 * 事件處理與同步都讀最新的 props（ref），所以編輯器不必因為 props 變了重建。
 */
export function useRichTextEditor(inputs: RichTextEditorInputs): Editor | null {
  const { value, onChange, placeholder, autoFocus, isBusy, mention, attributes } = inputs;
  const interactions = useLatest(inputs.interactions);
  const busy = useLatest(isBusy);
  const sync = useMarkdownSync(value, onChange);
  const [extensions] = useState(() => createExtensions({ isBusy: () => busy.get(), suggestion: mention.suggestion, placeholder }));
  const [baseProps] = useState(() => createEditorProps(() => interactions.get()));
  const [initialContent] = useState(() => markdownToDoc(value));
  const { labelId, describedBy, isInvalid, mentionAria } = attributes;
  const { 'aria-controls': controls, 'aria-autocomplete': autocomplete, 'aria-activedescendant': activeDescendant } = mentionAria;
  const editorProps = useMemo(
    () => ({
      ...baseProps,
      attributes: attributesOf({ labelId, describedBy, isInvalid, isBusy, placeholder, mentionAria: { 'aria-controls': controls, 'aria-autocomplete': autocomplete, 'aria-activedescendant': activeDescendant } }),
    }),
    [baseProps, labelId, describedBy, isInvalid, isBusy, placeholder, controls, autocomplete, activeDescendant],
  );

  const editor = useEditor(
    {
      extensions,
      content: initialContent,
      autofocus: autoFocus === 'end' ? 'end' : autoFocus ? 'start' : false,
      ...EDITOR_BEHAVIOR_OPTIONS,
      editorProps,
      onUpdate: ({ editor: updated }) => {
        sync.handleUpdate(updated);
      },
    },
    [],
  );
  useEffect(() => {
    editor.setOptions({ editorProps });
  }, [editor, editorProps]);
  const { setEditor } = mention;
  useEffect(() => {
    setEditor(editor);
  }, [editor, setEditor]);
  useExternalValue(editor, value, sync);
  return editor;
}
