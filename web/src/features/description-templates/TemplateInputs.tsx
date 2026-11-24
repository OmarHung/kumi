import { useEffect, useRef } from 'react';
import { CheckboxField } from '../../components/CheckboxField/CheckboxField';
import { TextField } from '../../components/Field/Field';
import { RichTextEditor, SUBMIT_SHORTCUT } from '../rich-text/RichTextEditor';
import type { RichTextEditorHandle } from '../rich-text/types';
import { MAX_DESCRIPTION_LENGTH } from '../item-table/cells/drafts';
import { TEMPLATE_NAME_MAX_LENGTH } from './templateRules';
import type { TemplateFormState } from './useTemplateForm';
import styles from './TemplateForm.module.css';

/** 名稱、「設成預設」與內容（Markdown，填滿畫面高度）。送出中欄位唯讀，內容不會被丟掉。 */
export function TemplateInputs({ form }: { readonly form: TemplateFormState }) {
  const { draft, fieldErrors, isSaving, change, focusRequest } = form;
  const nameRef = useRef<HTMLInputElement>(null);
  const bodyRef = useRef<RichTextEditorHandle>(null);
  // 送出後有問題：焦點到第一個有問題的欄位。
  useEffect(() => {
    if (focusRequest !== null) (focusRequest.field === 'name' ? nameRef : bodyRef).current?.focus();
  }, [focusRequest]);
  return (
    <>
      <div className={styles.head} data-content-start>
        <TextField
          ref={nameRef}
          className={styles.nameField}
          label="名稱"
          value={draft.name}
          maxLength={TEMPLATE_NAME_MAX_LENGTH * 2}
          autoComplete="off"
          readOnly={isSaving}
          error={fieldErrors.name}
          onChange={(event) => { change({ name: event.target.value }, 'name'); }}
        />
        <CheckboxField
          label="設成預設（開新專案、開始寫說明時預先選這一份）"
          checked={draft.isDefault}
          onChange={(event) => {
            if (!isSaving) change({ isDefault: event.target.checked });
          }}
        />
      </div>
      <RichTextEditor
        ref={bodyRef}
        label="內容"
        value={draft.body}
        onChange={(body) => { change({ body }, 'body'); }}
        maxLength={MAX_DESCRIPTION_LENGTH}
        rows={12}
        fillsViewport
        hasAttachments={false}
        hint={`${SUBMIT_SHORTCUT} 儲存`}
        onSubmit={form.save}
        isBusy={isSaving}
        error={fieldErrors.body ?? null}
      />
    </>
  );
}
