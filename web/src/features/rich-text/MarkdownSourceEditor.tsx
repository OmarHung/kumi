import { useEffect, useId, useImperativeHandle, useRef, useState, type KeyboardEvent, type RefObject } from 'react';
import { isCommitEnter } from '../../lib/keyboard';
import { InsertFileButton } from '../attachments/EditorUploadControls';
import { useMentionInput, type MentionInput } from '../item-page/mentions/useMentionInput';
import { EditorFrame } from './EditorFrame';
import { dragHandlersOf, MentionOverlay } from './EditorOverlays';
import { EditorToolbarBar } from './EditorToolbarBar';
import { handleToolbarKeyDown } from './toolbarKeys';
import { countNoteOf, describedByIds, formatHintOf } from './fieldText';
import type { EditorInnerProps } from './types';
import { useTextareaUploadTarget } from './textareaUploadTarget';
import styles from './MarkdownSourceEditor.module.css';

/** 輸入法選字中的 Enter 不算送出；Esc 取消（提及清單開著時先由清單接走，見 useMentionInput）。 */
function handleShortcuts(event: KeyboardEvent<HTMLTextAreaElement>, onSubmit: () => void, onCancel?: () => void) {
  if (isCommitEnter(event) && (event.ctrlKey || event.metaKey)) {
    event.preventDefault();
    onSubmit();
  } else if (event.key === 'Escape' && onCancel !== undefined) {
    event.preventDefault();
    onCancel();
  }
}

/** 掛上時放焦點：剛切換來的、或要求自動取得焦點的游標在最前面；下載好之前被要求過焦點的放最後（和所見即所得一樣）。 */
function useInitialFocus(textareaRef: RefObject<HTMLTextAreaElement | null>, props: EditorInnerProps) {
  const { autoFocus = false, takePendingFocus, takeSwitchFocus } = props;
  const [wantsAutoFocus] = useState(autoFocus);
  const isAtEnd = wantsAutoFocus === 'end';
  useEffect(() => {
    const textarea = textareaRef.current;
    if (textarea === null) return;
    const isSwitched = takeSwitchFocus();
    const hasPending = takePendingFocus();
    if (isSwitched || wantsAutoFocus) {
      textarea.focus();
      const caret = isAtEnd && !isSwitched ? textarea.value.length : 0;
      textarea.setSelectionRange(caret, caret);
    } else if (hasPending) {
      textarea.focus();
      textarea.setSelectionRange(textarea.value.length, textarea.value.length);
    }
  }, [textareaRef, wantsAutoFocus, isAtEnd, takePendingFocus, takeSwitchFocus]);
}

interface SourceTextareaProps {
  readonly id: string;
  readonly props: EditorInnerProps;
  readonly textareaRef: RefObject<HTMLTextAreaElement | null>;
  readonly mention: MentionInput;
  readonly count: ReturnType<typeof countNoteOf>;
}

/** 文字框本身：打字直接是 Markdown，提及的清單、貼上的檔案、快速鍵都接在這裡。 */
function SourceTextarea({ id, props, textareaRef, mention, count }: SourceTextareaProps) {
  const { value, onChange, maxLength, placeholder, onSubmit, onCancel, isBusy, error, uploads } = props;
  const { handlers } = mention;
  return (
    <textarea
      ref={textareaRef}
      className={styles.source}
      data-markdown-source=""
      maxLength={maxLength}
      value={value}
      placeholder={placeholder}
      readOnly={isBusy}
      spellCheck={false}
      aria-labelledby={`${id}-label`}
      aria-describedby={describedByIds(id, count !== null, error !== null)}
      aria-invalid={error !== null || count?.isOver === true || undefined}
      {...mention.aria}
      onPaste={(event) => {
        uploads?.binding.handlers.onPaste(event.nativeEvent);
      }}
      onChange={(event) => {
        onChange(event.target.value);
        handlers.onChange(event);
      }}
      onSelect={handlers.onSelect}
      onFocus={handlers.onFocus}
      onBlur={handlers.onBlur}
      onCompositionStart={handlers.onCompositionStart}
      onCompositionEnd={handlers.onCompositionEnd}
      onKeyDown={(event) => {
        if (!handlers.onKeyDown(event)) handleShortcuts(event, onSubmit, onCancel);
      }}
    />
  );
}

/** 文字框上方那一列：沒有格式按鈕，能上傳時左邊是「插入圖片或檔案」，右邊是編輯方式的切換。 */
function SourceToolbar({ props }: { readonly props: EditorInnerProps }) {
  const { onSubmit, onCancel, isBusy, uploads, modeControl } = props;
  return (
    <EditorToolbarBar
      control={modeControl}
      onShortcutKey={(event) => {
        handleToolbarKeyDown({ onSubmit, onCancel }, event);
      }}
    >
      {uploads?.canUpload === true && <InsertFileButton uploads={uploads} isBusy={isBusy} />}
    </EditorToolbarBar>
  );
}

/**
 * Markdown 原文的編輯器（編輯方式選 Markdown 時，取代所見即所得的 Tiptap）：值就是 Markdown 字串，不經過轉換，
 * 打什麼存什麼。外框、標籤、提示、字數、錯誤和所見即所得的編輯器同一套；功能和以前的文字框一樣：
 * Ctrl／⌘+Enter 送出、Esc 取消、送出中唯讀、打 @ 列出成員、貼上與拖進來的檔案上傳。不下載 Tiptap。
 */
export function MarkdownSourceEditor(props: EditorInnerProps) {
  const { label, isLabelHidden = false, value, onChange, maxLength, rows, hint, isBusy, error, isCollapsed = false, mentionMembers, uploads, fillsViewport = false, ref } = props;
  const id = useId();
  const frameRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const count = countNoteOf(value.length, maxLength);
  const mention = useMentionInput({ value, onChange, members: isBusy ? undefined : mentionMembers, maxLength });
  useTextareaUploadTarget({ textareaRef, value, onChange, uploads });
  useInitialFocus(textareaRef, props);
  useImperativeHandle(ref, () => ({
    focus: () => {
      textareaRef.current?.focus();
    },
  }));
  const focusTextarea = () => {
    textareaRef.current?.focus();
  };
  return (
    <EditorFrame
      id={id}
      label={label}
      isLabelHidden={isLabelHidden}
      rows={rows}
      hint={hint}
      formatHint={formatHintOf(mentionMembers !== undefined, uploads?.binding.canUpload === true, 'markdown')}
      count={count}
      error={error}
      isCollapsed={isCollapsed}
      isDropping={uploads?.binding.isDragging === true}
      isBusy={isBusy}
      fillsViewport={fillsViewport}
      frameRef={frameRef}
      dragHandlers={dragHandlersOf(uploads, isBusy)}
      onLabelClick={focusTextarea}
      onScrollClick={focusTextarea}
      toolbar={<SourceToolbar props={props} />}
      overlay={<MentionOverlay mention={mention} anchorRef={frameRef} />}
    >
      <SourceTextarea id={id} props={props} textareaRef={textareaRef} mention={mention} count={count} />
    </EditorFrame>
  );
}
