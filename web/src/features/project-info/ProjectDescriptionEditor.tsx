import { useRef, useState, type SubmitEvent } from 'react';
import { Button } from '../../components/Button/Button';
// 和項目的說明同一個上限（後端 ProjectDescriptionRules.MaxTextLength）。
import { MAX_DESCRIPTION_LENGTH } from '../item-table/cells/drafts';
import { RichTextEditor, SUBMIT_SHORTCUT } from '../rich-text/RichTextEditor';
import type { RichTextEditorHandle } from '../rich-text/types';
import { RestoredDraftNotice } from '../item-page/RestoredDraftNotice';
import type { ProjectDescriptionEditor } from './useProjectDescriptionEditor';
import { cx } from '../../lib/cx';
import fill from './editorFill.module.css';
import styles from './ProjectInfo.module.css';

const DESCRIPTION_ROWS = 18;

interface ProjectDescriptionFormProps {
  readonly editor: ProjectDescriptionEditor;
  readonly draft: string;
}

/** 編輯中：編輯器、固定的提醒、儲存與取消。沒有圖片與提及（專案說明沒有附件，也不通知人）。 */
export function ProjectDescriptionForm({ editor, draft }: ProjectDescriptionFormProps) {
  const editorRef = useRef<RichTextEditorHandle>(null);
  // 還原時就在編輯中：掛上時不搶焦點；自己按「編輯」「開始寫」才搶。
  const [shouldAutoFocus] = useState(!editor.isRestored);

  // 從「儲存」送出時先把焦點放回編輯器：儲存中按鈕會停用，有焦點的按鈕一停用，瀏覽器就把焦點丟回頁面本體。
  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    editorRef.current?.focus();
    editor.save();
  }

  function discard() {
    // 「捨棄」跟著提示一起不見：焦點先放到編輯器。
    editorRef.current?.focus();
    editor.discard();
  }

  return (
    <form className={cx(styles.form, fill.fill)} aria-busy={editor.isSaving} onSubmit={handleSubmit}>
      {editor.isRestored && <RestoredDraftNotice target="還沒儲存的說明" outdated={editor.outdated} onDiscard={discard} />}
      <RichTextEditor
        ref={editorRef}
        label="說明"
        isLabelHidden
        value={draft}
        onChange={editor.change}
        maxLength={MAX_DESCRIPTION_LENGTH}
        rows={DESCRIPTION_ROWS}
        fillsViewport
        hasAttachments={false}
        hint={`${SUBMIT_SHORTCUT} 儲存，Esc 取消`}
        onSubmit={editor.save}
        onCancel={editor.cancel}
        isBusy={editor.isSaving}
        error={editor.error}
        autoFocus={shouldAutoFocus}
      />
      <p className={styles.reminder}>不要貼密碼或金鑰，寫密碼管理器裡的項目名稱就好。</p>
      <div className={styles.actions}>
        <Button size="sm" disabled={editor.isSaving} onClick={editor.cancel}>
          取消
        </Button>
        <Button size="sm" variant="primary" type="submit" disabled={editor.isSaving}>
          {editor.isSaving ? '儲存中…' : '儲存'}
        </Button>
      </div>
    </form>
  );
}
