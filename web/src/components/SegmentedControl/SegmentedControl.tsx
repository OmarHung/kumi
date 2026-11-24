import styles from './SegmentedControl.module.css';

export interface SegmentedOption<T extends string> {
  readonly value: T;
  readonly label: string;
}

export interface SegmentedControlProps<T extends string> {
  /** 群組的名稱，例如「顯示」。 */
  readonly label: string;
  readonly options: readonly SegmentedOption<T>[];
  readonly value: T;
  readonly onChange: (value: T) => void;
  /** sm：和小的圖示按鈕一樣高（工具列裡用）。 */
  readonly size?: 'md' | 'sm';
  /** 暫時不能切換：按鈕用 aria-disabled（焦點留得住），按了不呼叫 onChange；原因另外寫在畫面上，用 describedBy 連著。 */
  readonly isDisabled?: boolean;
  /** 說明原因（或其他補充）的元素 id，每一顆按鈕都連著。 */
  readonly describedBy?: string;
}

/**
 * 一組互斥的選項、狀態不在網址裡時用（網址的狀態用 NavTabs）：群組裡每個選項一顆按鈕，目前的那顆 aria-pressed。
 * 不用 radio 或 tab 的角色：沒有方向鍵的移動，每一顆都是 Tab 停得到的按鈕，行為最單純；
 * 按目前那一顆什麼都不做，焦點留在按下的那一顆（按鈕本身沒有被換掉）。停用時用 aria-disabled、不用 disabled（docs/design 7 節 Button）：焦點不會掉。
 */
export function SegmentedControl<T extends string>({ label, options, value, onChange, size = 'md', isDisabled = false, describedBy }: SegmentedControlProps<T>) {
  return (
    <div role="group" aria-label={label} className={styles.group} data-size={size}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          className={styles.segment}
          aria-pressed={option.value === value}
          aria-disabled={isDisabled || undefined}
          aria-describedby={describedBy}
          onClick={() => {
            if (!isDisabled && option.value !== value) onChange(option.value);
          }}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}
