import { useId, useRef, useState, type RefObject, type SubmitEvent } from 'react';
import type { ItemDetail } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { EditorUploadNotes, UploadAwareSubmit } from '../attachments/EditorUploadControls';
import { useEditorUploads } from '../attachments/useEditorUploads';
import { MAX_DESCRIPTION_LENGTH } from '../item-table/cells/drafts';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { CollapsibleText } from './CollapsibleText';
import { SectionHeading } from './ItemPanelContext';
import { RichTextEditor, SUBMIT_SHORTCUT } from '../rich-text/RichTextEditor';
import type { RichTextEditorHandle } from '../rich-text/types';
import { DescriptionConflictNotice } from './DescriptionConflictNotice';
import { useDescriptionEditor, type DescriptionEditor } from './useDescriptionEditor';
import { RestoredDraftNotice } from './RestoredDraftNotice';
import { useMentionCandidates } from './useMentionCandidates';
import styles from './DescriptionSection.module.css';

const DESCRIPTION_ROWS = 10;

/**
 * 送出：還有檔案在上傳時不存（說明裡還有「上傳中」的記號），說原因。從「儲存」送出時先把焦點放回編輯器：
 * 儲存中按鈕會停用，有焦點的按鈕一停用，瀏覽器就把焦點丟回頁面本體；編輯器儲存中是唯讀，焦點留得住（和留言框一樣）。
 */
function useDescriptionSubmit(editor: DescriptionEditor, uploads: ReturnType<typeof useEditorUploads>, editorRef: RefObject<RichTextEditorHandle | null>) {
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

function DescriptionForm({ editor, draft, itemKey }: { readonly editor: DescriptionEditor; readonly draft: string; readonly itemKey: string }) {
  const candidates = useMentionCandidates();
  const editorRef = useRef<RichTextEditorHandle>(null);
  const uploads = useEditorUploads({ itemKey, value: draft, onChange: editor.change, maxLength: MAX_DESCRIPTION_LENGTH, isReadOnly: editor.isSaving });
  // 還原時就在編輯中：掛上時不搶焦點（使用者剛換到這一頁，焦點在頁面的標題或面板上）；自己進入編輯（「新增說明」、鉛筆）才搶。
  const [shouldAutoFocus] = useState(!editor.isRestored);
  const { save, handleSubmit } = useDescriptionSubmit(editor, uploads, editorRef);

  // 按下去的按鈕跟著提示或說明一起不見：焦點先放到編輯器（儲存中是唯讀，焦點留得住）。
  const thenFocus = (action: () => unknown) => () => {
    editorRef.current?.focus();
    void action();
  };

  const conflictActions = {
    onReload: thenFocus(editor.reload),
    onKeepEditing: thenFocus(editor.keepEditing),
    onOverwrite: thenFocus(editor.overwrite),
  };

  return (
    <form className={styles.form} aria-busy={editor.isSaving} onSubmit={handleSubmit}>
      {editor.isRestored && <RestoredDraftNotice target="還沒儲存的說明" outdated={editor.outdated} onDiscard={thenFocus(editor.discard)} />}
      {editor.conflict !== null && <DescriptionConflictNotice conflict={editor.conflict} isBusy={editor.isSaving || editor.isResolving} {...conflictActions} />}
      <RichTextEditor
        ref={editorRef}
        label="說明"
        isLabelHidden
        value={draft}
        onChange={editor.change}
        maxLength={MAX_DESCRIPTION_LENGTH}
        rows={DESCRIPTION_ROWS}
        placeholder="背景、目標、驗收條件…"
        hint={`${SUBMIT_SHORTCUT} 儲存，Esc 取消`}
        onSubmit={save}
        onCancel={editor.cancel}
        isBusy={editor.isSaving}
        error={editor.error}
        autoFocus={shouldAutoFocus}
        mentionMembers={candidates}
        uploads={uploads}
      />
      <EditorUploadNotes uploads={uploads} />
      <div className={styles.actions}>
        <Button size="sm" disabled={editor.isSaving} onClick={editor.cancel}>
          取消
        </Button>
        <UploadAwareSubmit isUploading={uploads.isUploading} disabled={editor.isSaving}>
          {editor.isSaving ? '儲存中…' : '儲存'}
        </UploadAwareSubmit>
      </div>
    </form>
  );
}

const ADD_HINT = '寫下背景、目標或驗收條件，接手的人比較好懂。';

interface DescriptionViewProps {
  readonly description: string | null;
  readonly canEdit: boolean;
  readonly entryRef: RefObject<HTMLButtonElement | null>;
  readonly onStart: () => void;
}

/**
 * 不在編輯時的說明（docs/DESIGN.md 8.3「說明」）。能編輯的人：沒有說明時整塊虛線框就是「新增說明」的按鈕；
 * 有說明時只有標題列的鉛筆（「編輯說明」）能進入編輯，文字本身點了沒有反應：連結、附件圖片要能直接點，選字也不會誤進編輯。
 * 不能編輯的人：沒有說明時是虛線的提示。
 */
function DescriptionView({ description, canEdit, entryRef, onStart }: DescriptionViewProps) {
  const hintId = useId();
  if (description === null && !canEdit) {
    return <p className={styles.empty}>還沒有說明。</p>;
  }
  if (description === null) {
    return (
      // 名稱固定是「新增說明」，下面那行灰字是它的說明（aria-describedby）。
      <button ref={entryRef} type="button" className={styles.add} aria-label="新增說明" aria-describedby={hintId} onClick={onStart}>
        <span className={styles.addLabel}>
          <Icon name="add" />
          新增說明
        </span>
        <span id={hintId} className={styles.addHint}>
          {ADD_HINT}
        </span>
      </button>
    );
  }
  // 很長時先收起來（超過 12 行）；編輯時這裡換成編輯器，一律是全文。
  return <CollapsibleText text={description} />;
}

/** 說明（Markdown）。有 item.update 才能新增或修改（標題列的鉛筆或「新增說明」）。 */
function DescriptionContent({ item }: { readonly item: ItemDetail }) {
  const { can } = useProjectTable();
  const headingId = useId();
  const entryRef = useRef<HTMLButtonElement>(null);
  const editor = useDescriptionEditor(item, entryRef);
  const description = item.description ?? null;
  // 沒有 item.update 的人（例如權限剛被拿掉）不還原：存著的草稿留在分頁裡，不顯示成能編輯。
  const isEditing = can.update && editor.draft !== null;
  // 沒有說明時入口是下面那一塊，標題列不再放「新增說明」。
  const editButton = can.update && editor.draft === null && description !== null && (
    <IconButton ref={entryRef} icon="edit" label="編輯說明" size="sm" variant="ghost" onClick={editor.start} />
  );
  return (
    <section className={styles.section} aria-labelledby={headingId}>
      <div className={styles.header}>
        <SectionHeading id={headingId} className={styles.heading}>
          說明
        </SectionHeading>
        {editButton}
      </div>
      {isEditing && editor.draft !== null ? (
        <DescriptionForm editor={editor} draft={editor.draft} itemKey={item.key} />
      ) : (
        <DescriptionView description={description} canEdit={can.update} entryRef={entryRef} onStart={editor.start} />
      )}
    </section>
  );
}

/** 換了項目就重新掛上：草稿、編輯中的狀態都跟著項目。 */
export function DescriptionSection({ item }: { readonly item: ItemDetail }) {
  return <DescriptionContent key={item.key} item={item} />;
}
