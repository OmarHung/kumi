import { useRef, useState, type KeyboardEvent, type RefObject, type SubmitEvent } from 'react';
import { knowledgeAttachmentContentUrl, KNOWLEDGE_PAGE_BODY_MAX, KNOWLEDGE_PAGE_TITLE_MAX } from '../../../api/knowledge';
import type { KnowledgePage } from '../../../api/knowledgeTypes';
import { Button } from '../../../components/Button/Button';
import { ConfirmDialog } from '../../../components/Dialog/ConfirmDialog';
import { TextField } from '../../../components/Field/Field';
import { cx } from '../../../lib/cx';
import { EditorUploadNotes, UploadAwareSubmit } from '../../attachments/EditorUploadControls';
import type { EditorUploads } from '../../attachments/useEditorUploads';
import { DescriptionConflictNotice } from '../../item-page/DescriptionConflictNotice';
import { RestoredDraftNotice } from '../../item-page/RestoredDraftNotice';
import fill from '../../project-info/editorFill.module.css';
import { RichTextEditor, SUBMIT_SHORTCUT } from '../../rich-text/RichTextEditor';
import type { RichTextEditorHandle } from '../../rich-text/types';
import type { PageDraft } from './pageDraft';
import type { PageEditor } from './usePageEditor';
import { usePageEditorUploads } from './usePageEditorUploads';
import styles from './KnowledgePage.module.css';

const BODY_ROWS = 18;

/** 按下去的按鈕跟著提示一起不見：焦點先放到編輯器（儲存中是唯讀，焦點留得住）。 */
function useThenFocus(editorRef: RefObject<RichTextEditorHandle | null>) {
  return (action: () => unknown) => () => {
    editorRef.current?.focus();
    void action();
  };
}

function EditNotices({ editor, editorRef }: { readonly editor: PageEditor; readonly editorRef: RefObject<RichTextEditorHandle | null> }) {
  const thenFocus = useThenFocus(editorRef);
  return (
    <>
      {editor.isRestored && <RestoredDraftNotice target="還沒儲存的頁面" outdated={editor.outdated} onDiscard={thenFocus(editor.discard)} />}
      {editor.conflict !== null && (
        <DescriptionConflictNotice
          conflict={editor.conflict}
          subject="這一頁"
          isBusy={editor.isSaving || editor.isResolving}
          onReload={thenFocus(editor.reload)}
          onKeepEditing={thenFocus(editor.keepEditing)}
          onOverwrite={thenFocus(editor.overwrite)}
        />
      )}
    </>
  );
}

interface TitleFieldProps {
  readonly editor: PageEditor;
  readonly save: () => void;
  readonly cancel: () => void;
  readonly draft: PageDraft;
  readonly shouldAutoFocus: boolean;
  readonly focusEditor: () => void;
}

/** 標題裡按 Enter 是跳到內容，不是儲存（打標題時順手按 Enter 不該把一頁存出去）；⌘／Ctrl+Enter 儲存、Esc 取消。 */
function TitleField({ editor, save, cancel, draft, shouldAutoFocus, focusEditor }: TitleFieldProps) {
  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.nativeEvent.isComposing) return;
    if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      event.preventDefault();
      save();
    } else if (event.key === 'Enter') {
      event.preventDefault();
      focusEditor();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      cancel();
    }
  }
  return (
    <TextField
      label="標題"
      isLabelHidden
      value={draft.title}
      maxLength={KNOWLEDGE_PAGE_TITLE_MAX}
      placeholder="頁面標題"
      readOnly={editor.isSaving}
      error={editor.titleError ?? undefined}
      autoFocus={shouldAutoFocus && draft.title === ''}
      onChange={(event) => {
        editor.change({ title: event.target.value });
      }}
      onKeyDown={handleKeyDown}
    />
  );
}

interface PageEditFormProps {
  readonly editor: PageEditor;
  readonly draft: PageDraft;
  readonly page: KnowledgePage;
}

interface BodyEditorProps {
  readonly editor: PageEditor;
  readonly save: () => void;
  readonly cancel: () => void;
  readonly draft: PageDraft;
  /** 貼上、拖進來的檔案：傳到這一頁的附件。 */
  readonly uploads: EditorUploads;
  readonly editorRef: RefObject<RichTextEditorHandle | null>;
  readonly shouldAutoFocus: boolean;
}

function BodyEditor({ editor, save, cancel, draft, uploads, editorRef, shouldAutoFocus }: BodyEditorProps) {
  return (
    <RichTextEditor
      ref={editorRef}
      label="內容"
      isLabelHidden
      value={draft.body}
      onChange={(body) => {
        editor.change({ body });
      }}
      maxLength={KNOWLEDGE_PAGE_BODY_MAX}
      rows={BODY_ROWS}
      fillsViewport
      uploads={uploads}
      attachmentUrl={knowledgeAttachmentContentUrl}
      hint={`${SUBMIT_SHORTCUT} 儲存，Esc 取消`}
      onSubmit={save}
      onCancel={cancel}
      isBusy={editor.isSaving}
      error={editor.error}
      autoFocus={shouldAutoFocus && draft.title !== ''}
    />
  );
}

function EditActions({ editor, isUploading, onCancel }: { readonly editor: PageEditor; readonly isUploading: boolean; readonly onCancel: () => void }) {
  return (
    <div className={styles.actions}>
      <Button size="sm" disabled={editor.isSaving} onClick={onCancel}>
        取消
      </Button>
      <UploadAwareSubmit isUploading={isUploading} disabled={editor.isSaving}>
        {editor.isSaving ? '儲存中…' : '儲存'}
      </UploadAwareSubmit>
    </div>
  );
}

/**
 * 送出的三個入口（編輯器與標題的 ⌘／Ctrl+Enter、表單的「儲存」）共用一個擋點：還有檔案在上傳時不存
 * （內容裡還有「上傳中」的記號），用即時區域說原因。從「儲存」送出時先把焦點放回編輯器：儲存中按鈕停用，焦點會掉。
 */
function usePageSubmit(editor: PageEditor, uploads: EditorUploads, editorRef: RefObject<RichTextEditorHandle | null>) {
  const save = () => {
    if (uploads.isUploading) uploads.announceUploading();
    else editor.save();
  };
  const handleSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    editorRef.current?.focus();
    save();
  };
  return { save, handleSubmit };
}

/**
 * 取消（按鈕、編輯器與標題的 Esc）：有修改時先問再丟。頁面最多 10 萬字，一個不小心的 Esc 就丟掉整頁，
 * 比項目的說明代價大得多（docs/design/08-interaction.md 8.3「頁面的編輯與衝突」）；沒修改就直接關。
 */
function useCancelRequest(editor: PageEditor) {
  const [isAsking, setIsAsking] = useState(false);
  const request = () => {
    if (editor.isSaving) return;
    if (editor.hasChanges) setIsAsking(true);
    else editor.cancel();
  };
  const dialog = isAsking && (
    <ConfirmDialog
      title="捨棄修改？"
      description="標題與內容的修改會不見。"
      confirmLabel="捨棄修改"
      cancelLabel="繼續編輯"
      tone="danger"
      onConfirm={() => {
        setIsAsking(false);
        editor.cancel();
      }}
      onCancel={() => {
        setIsAsking(false);
      }}
    />
  );
  return { request, dialog };
}

/**
 * 編輯中的一頁（docs/design/08-interaction.md 8.3「頁面的編輯與衝突」）：標題的輸入框與內容的編輯器占著檢視時標題與內文的位置，
 * 取消、儲存在下面。⌘／Ctrl+Enter 儲存、Esc 取消。
 */
export function PageEditForm({ editor, draft, page }: PageEditFormProps) {
  const editorRef = useRef<RichTextEditorHandle>(null);
  // 還原時就在編輯中：掛上時不搶焦點（使用者剛換到這一頁）；自己按「編輯」或新增頁面之後才搶。
  const [shouldAutoFocus] = useState(!editor.isRestored);
  const uploads = usePageEditorUploads({
    spaceKey: page.space.key,
    pageNumber: page.number,
    canUpload: page.myPermissions.canEdit,
    value: draft.body,
    onChange: (body) => {
      editor.change({ body });
    },
    maxLength: KNOWLEDGE_PAGE_BODY_MAX,
    isReadOnly: editor.isSaving,
  });

  const { save, handleSubmit } = usePageSubmit(editor, uploads, editorRef);
  const cancel = useCancelRequest(editor);

  return (
    <form className={cx(styles.form, fill.fill)} aria-busy={editor.isSaving} onSubmit={handleSubmit}>
      <EditNotices editor={editor} editorRef={editorRef} />
      <TitleField
        editor={editor}
        save={save}
        cancel={cancel.request}
        draft={draft}
        shouldAutoFocus={shouldAutoFocus}
        focusEditor={() => {
          editorRef.current?.focus();
        }}
      />
      <BodyEditor editor={editor} save={save} cancel={cancel.request} draft={draft} uploads={uploads} editorRef={editorRef} shouldAutoFocus={shouldAutoFocus} />
      <EditorUploadNotes uploads={uploads} />
      <EditActions editor={editor} isUploading={uploads.isUploading} onCancel={cancel.request} />
      {cancel.dialog}
    </form>
  );
}
