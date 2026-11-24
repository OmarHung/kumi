import { useRef, type RefObject } from 'react';
import { EditorUploadNotes } from '../attachments/EditorUploadControls';
import { useItemEditorUploads } from '../attachments/useItemEditorUploads';
import { MAX_COMMENT_LENGTH } from '../item-table/cells/drafts';
import { RichTextEditor, SUBMIT_SHORTCUT } from '../rich-text/RichTextEditor';
import type { RichTextEditorHandle, RichTextEditorProps } from '../rich-text/types';
import type { EditorUploads } from '../attachments/useEditorUploads';
import type { Member } from '../../api/types';
import { useMentionCandidates } from './useMentionCandidates';

export { SUBMIT_SHORTCUT };

interface EditorPartsInput {
  readonly itemKey: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  /** 送出中：編輯器唯讀，上傳也停住。 */
  readonly isBusy: boolean;
}

/** 留言框與編輯留言共用的零件：提及的人選、編輯器的把手、貼圖與檔案的上傳（內容寫進同一個草稿）。 */
export function useCommentEditorParts({ itemKey, value, onChange, isBusy }: EditorPartsInput) {
  const candidates = useMentionCandidates();
  const editorRef = useRef<RichTextEditorHandle>(null);
  const uploads = useItemEditorUploads({ itemKey, value, onChange, maxLength: MAX_COMMENT_LENGTH, isReadOnly: isBusy });
  return { candidates, editorRef, uploads };
}

export type CommentEditorParts = ReturnType<typeof useCommentEditorParts>;

type FieldProps = Pick<
  RichTextEditorProps,
  'label' | 'isLabelHidden' | 'value' | 'onChange' | 'rows' | 'isCollapsed' | 'placeholder' | 'autoFocus' | 'onCancel' | 'hint' | 'onSubmit' | 'isBusy' | 'error'
>;

interface CommentEditorFieldProps extends FieldProps {
  /** useCommentEditorParts 的三個零件，攤開傳（整包傳會讓 lint 以為在 render 裡讀 ref）。 */
  readonly editorRef: RefObject<RichTextEditorHandle | null>;
  readonly candidates: readonly Member[];
  readonly uploads: EditorUploads;
}

/** 留言的編輯器加上傳的提示：新留言、回覆、編輯既有的留言長得一樣，貼圖、字數、提及、快速鍵都一樣。 */
export function CommentEditorField({ editorRef, candidates, uploads, ...props }: CommentEditorFieldProps) {
  return (
    <>
      <RichTextEditor ref={editorRef} {...props} maxLength={MAX_COMMENT_LENGTH} mentionMembers={candidates} uploads={uploads} />
      <EditorUploadNotes uploads={uploads} />
    </>
  );
}
