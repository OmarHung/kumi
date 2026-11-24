import { useState } from 'react';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { removedFields } from '../../domain/settingsChangeSet';
import { sectionHeadingId } from './settingsSections';
import { AddFieldDialog } from './AddFieldDialog';
import { FieldRow, RemovedFieldRow } from './FieldRow';
import { useSettingsEditor } from './SettingsContext';
import styles from './Settings.module.css';

/** 自訂欄位：照藍圖的順序，要刪除的列在後面。核心欄位（標題、狀態、負責人…）是內建的，不在這裡。 */
export function FieldsSection() {
  const editor = useSettingsEditor();
  const [isAdding, setIsAdding] = useState(false);
  const headingId = sectionHeadingId(editor.idPrefix, 'fields');
  const addButtonId = `${editor.idPrefix}-add-field`;
  const removed = removedFields(editor.base, editor.draft);
  return (
    // 設定頁目前這個區塊是第一筆內容（docs/DESIGN.md 6.2）：給 scripts/ui-audit.js 量高度預算。
    <section className={styles.section} aria-labelledby={headingId} data-content-start>
      <div className={styles.sectionHeader}>
        <h2 id={headingId} tabIndex={-1} className={styles.sectionTitle}>
          欄位
        </h2>
        <p className={styles.sectionHint}>核心欄位（標題、狀態、負責人、優先度、日期、估計）是內建的，不在這裡。</p>
      </div>
      <ul className={styles.list} aria-label="自訂欄位">
        {editor.draft.fields.map((field) => (
          <FieldRow key={field.key} field={field} addButtonId={addButtonId} />
        ))}
      </ul>
      {removed.length > 0 && (
        <ul className={styles.list} aria-label="要刪除的欄位">
          {removed.map((field) => (
            <RemovedFieldRow key={field.key} field={field} />
          ))}
        </ul>
      )}
      {editor.canEdit && (
        <Button id={addButtonId} size="sm" className={styles.addButton} onClick={() => { setIsAdding(true); }}>
          <Icon name="add" />
          新增欄位
        </Button>
      )}
      {isAdding && <AddFieldDialog onClose={() => { setIsAdding(false); }} />}
    </section>
  );
}
