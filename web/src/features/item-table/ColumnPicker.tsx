import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { Popover } from '../../components/Popover/Popover';
import { isHiddenByChoice, type ColumnVisibility } from './columnVisibility';
import styles from './ItemToolbar.module.css';

interface ColumnPickerProps {
  readonly columns: readonly { readonly id: string; readonly label: string }[];
  readonly visibility: ColumnVisibility;
  readonly onChange: (visibility: ColumnVisibility) => void;
  /** 欄寬回到預設（docs/DESIGN.md 7.1「欄寬」）。 */
  readonly onResetWidths: () => void;
}

/**
 * 選要顯示哪些欄。編號與標題一定顯示，不在清單裡。開關、Esc、點外面關上由共用的 Popover 處理。
 * 「隱藏了 N 欄」放在按鈕前面（整組靠右）：浮層開著時勾掉一欄，多出來的文字往左長，按鈕不會被推走。
 * 最下面的「重設欄寬」把拉過的欄寬都回到預設：除了拖曳，不用拖的另一條路（WCAG 2.5.7，另一條是點兩下把手照內容調整）。
 * 按了不會不見、不停用（沒拉過時按了也沒有變化），焦點留在它身上。
 */
export function ColumnPicker({ columns, visibility, onChange, onResetWidths }: ColumnPickerProps) {
  const hiddenCount = columns.filter((column) => isHiddenByChoice(column.id, visibility)).length;
  return (
    <div className={styles.picker}>
      {hiddenCount > 0 && <span className={styles.pickerNote}>隱藏了 {hiddenCount} 欄</span>}
      <Popover
        label="顯示的欄位"
        align="end"
        renderTrigger={(trigger) => (
          <Button {...trigger} variant="ghost" size="sm" title="顯示的欄位">
            <Icon name="columns" />
            <span className={styles.pickerLabel}>顯示的欄位</span>
          </Button>
        )}
      >
        <fieldset className={styles.pickerList}>
          <legend className="visually-hidden">顯示的欄位</legend>
          {columns.map((column) => (
            <label key={column.id} className={styles.pickerItem}>
              <input
                type="checkbox"
                checked={visibility[column.id] !== false}
                onChange={(event) => {
                  onChange({ ...visibility, [column.id]: event.target.checked });
                }}
              />
              {column.label}
            </label>
          ))}
        </fieldset>
        <div className={styles.pickerFooter}>
          <Button size="sm" onClick={onResetWidths}>
            重設欄寬
          </Button>
        </div>
      </Popover>
    </div>
  );
}
