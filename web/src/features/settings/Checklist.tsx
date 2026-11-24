import { CheckboxField } from '../../components/CheckboxField/CheckboxField';
import { cx } from '../../lib/cx';
import styles from './Settings.module.css';

export interface ChecklistOption {
  readonly key: string;
  readonly label: string;
}

interface ChecklistProps {
  readonly legend: string;
  readonly options: readonly ChecklistOption[];
  /** 勾選的 key。 */
  readonly value: readonly string[];
  readonly onChange: (next: readonly string[]) => void;
  /** 這一組勾選框的說明（例如送出會被擋下的提醒）的 id。 */
  readonly describedBy?: string;
  /** 標題自己一行、選項排在下面（對話框裡）；預設標題和選項排在同一行。 */
  readonly isStacked?: boolean;
}

/**
 * 一組勾選框。勾選的結果照選項的順序存：勾選的先後不影響送出的內容。
 * 值裡有、選項裡沒有的（例如要刪除的類型，送出時才拿掉）接在後面留著：復原它時引用才跟著回來。
 * 每一個是共用的 CheckboxField：整個標籤是點擊範圍（至少 --target-min）。
 * 不用 CheckboxGroup：這裡要把 aria-describedby 接到整組（送出會被擋下的提醒），標題也要能和選項排在同一行。
 */
export function Checklist({ legend, options, value, onChange, describedBy, isStacked = false }: ChecklistProps) {
  const toggle = (key: string, isChecked: boolean) => {
    const others = value.filter((candidate) => candidate !== key);
    const next = isChecked ? [...others, key] : others;
    const hidden = value.filter((candidate) => !options.some((option) => option.key === candidate));
    onChange([...options.map((option) => option.key).filter((candidate) => next.includes(candidate)), ...hidden]);
  };
  return (
    <fieldset className={cx(styles.types, isStacked && styles.typesStacked)} aria-describedby={describedBy}>
      <legend className={styles.typesLegend}>{legend}</legend>
      {options.map((option) => (
        <CheckboxField
          key={option.key}
          label={option.label}
          checked={value.includes(option.key)}
          onChange={(event) => { toggle(option.key, event.target.checked); }}
        />
      ))}
    </fieldset>
  );
}

/** 唯讀時的寫法：「任務、需求」；一個都沒有時用 empty。 */
export function checkedNames(options: readonly ChecklistOption[], value: readonly string[], empty: string): string {
  const names = options.filter((option) => value.includes(option.key)).map((option) => option.label);
  return names.length === 0 ? empty : names.join('、');
}
