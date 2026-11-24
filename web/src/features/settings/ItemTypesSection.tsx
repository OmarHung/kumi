import { useState } from 'react';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { removedItemTypes } from '../../domain/settingsTypeRemoval';
import { sectionHeadingId } from './settingsSections';
import { AddItemTypeDialog } from './AddItemTypeDialog';
import { ItemTypeRow } from './ItemTypeRow';
import { RemovedItemTypeRow } from './RemovedItemTypeRow';
import { useSettingsEditor } from './SettingsContext';
import styles from './Settings.module.css';

/**
 * 類型：照藍圖的順序，新的接在最後，要刪除的列在後面。還有這種項目時預覽頁會擋下刪除
 * （設定頁不知道數量，要先在項目頁刪除那些項目或改類型）。
 */
export function ItemTypesSection() {
  const editor = useSettingsEditor();
  const [isAdding, setIsAdding] = useState(false);
  const headingId = sectionHeadingId(editor.idPrefix, 'types');
  const addButtonId = `${editor.idPrefix}-add-item-type`;
  const removed = removedItemTypes(editor.base, editor.draft);
  return (
    // 設定頁目前這個區塊是第一筆內容（docs/DESIGN.md 6.2）：給 scripts/ui-audit.js 量高度預算。
    <section className={styles.section} aria-labelledby={headingId} data-content-start>
      <div className={styles.sectionHeader}>
        <h2 id={headingId} tabIndex={-1} className={styles.sectionTitle}>
          類型
        </h2>
        <p className={styles.sectionHint}>每種類型用一個流程；上層決定這種項目可以掛在哪些類型底下。還有這種項目的類型刪不掉，要先在項目頁刪除那些項目或改成別的類型。</p>
      </div>
      <ul className={styles.list} aria-label="類型">
        {editor.draft.itemTypes.map((type) => (
          <ItemTypeRow key={type.key} type={type} addButtonId={addButtonId} />
        ))}
      </ul>
      {removed.length > 0 && (
        <ul className={styles.list} aria-label="要刪除的類型">
          {removed.map((type) => (
            <RemovedItemTypeRow key={type.key} type={type} />
          ))}
        </ul>
      )}
      {editor.canEdit && (
        <Button id={addButtonId} size="sm" className={styles.addButton} onClick={() => { setIsAdding(true); }}>
          <Icon name="add" />
          新增類型
        </Button>
      )}
      {isAdding && <AddItemTypeDialog onClose={() => { setIsAdding(false); }} />}
    </section>
  );
}
