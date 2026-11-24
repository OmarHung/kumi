import { useEffect } from 'react';
import { Icon } from '../../components/Icon/Icon';
import type { JumpGroup, JumpOption } from './jumpGroups';
import styles from './JumpDialog.module.css';

/** 選項的 id（aria-activedescendant 指向它）：照攤平之後的位置編號。 */
export function jumpOptionId(listboxId: string, index: number): string {
  return `${listboxId}-option-${String(index)}`;
}

/** 每一組的第一個選項在攤平的清單裡是第幾個。 */
function groupStarts(groups: readonly JumpGroup[]): readonly number[] {
  return groups.map((_, index) => groups.slice(0, index).reduce((total, group) => total + group.options.length, 0));
}

/** 用方向鍵選到捲出去的選項時，捲到看得到的地方（焦點在輸入框，瀏覽器不會自己捲）。 */
function useScrollActiveIntoView(listboxId: string, activeIndex: number) {
  useEffect(() => {
    if (activeIndex < 0) return;
    const option = document.getElementById(jumpOptionId(listboxId, activeIndex));
    if (option !== null && typeof option.scrollIntoView === 'function') {
      option.scrollIntoView({ block: 'nearest' });
    }
  }, [listboxId, activeIndex]);
}

interface OptionRowProps {
  readonly id: string;
  readonly option: JumpOption;
  readonly isActive: boolean;
  readonly onActivate: (key: string) => void;
  readonly onPick: (option: JumpOption) => void;
}

/** 一個選項：圖示或等寬的代號、名稱、靠右的灰字。之間的空白讓報讀軟體不會把代號和名稱唸成一個字。 */
function OptionRow({ id, option, isActive, onActivate, onPick }: OptionRowProps) {
  return (
    <li
      id={id}
      role="option"
      aria-selected={isActive}
      className={styles.option}
      onPointerMove={() => {
        if (!isActive) onActivate(option.key);
      }}
      onClick={() => {
        onPick(option);
      }}
    >
      {option.icon !== undefined && <Icon name={option.icon} className={styles.optionIcon} />}
      {option.code !== undefined && (
        <>
          <span className={styles.code}>{option.code}</span>{' '}
        </>
      )}
      <span className={styles.label}>{option.label}</span>
      {option.tag !== undefined && (
        <>
          {' '}
          <span className={styles.tag}>{option.tag}</span>
        </>
      )}
      {option.hint !== undefined && (
        <>
          {' '}
          <span className={styles.hint}>{option.hint}</span>
        </>
      )}
    </li>
  );
}

interface JumpResultsProps {
  readonly id: string;
  readonly groups: readonly JumpGroup[];
  readonly activeIndex: number;
  readonly onActivate: (key: string) => void;
  readonly onPick: (option: JumpOption) => void;
}

/** 結果的 listbox：每一組是 role="group"，名稱是組的標題（role="presentation" 的那一列）。沒有結果時藏起來。 */
export function JumpResults({ id, groups, activeIndex, onActivate, onPick }: JumpResultsProps) {
  useScrollActiveIntoView(id, activeIndex);
  const starts = groupStarts(groups);
  return (
    <div
      id={id}
      role="listbox"
      aria-label="結果"
      className={styles.results}
      hidden={groups.length === 0}
      // 按在選項上時焦點留在輸入框：combobox 的焦點一直在輸入框，選起來的那一個靠 aria-activedescendant。
      onMouseDown={(event) => {
        event.preventDefault();
      }}
    >
      {groups.map((group, groupIndex) => {
        const labelId = `${id}-${group.key}`;
        const start = starts[groupIndex] ?? 0;
        return (
          <ul key={group.key} role="group" aria-labelledby={labelId} className={styles.group}>
            <li role="presentation" id={labelId} className={styles.groupLabel}>
              {group.label}
            </li>
            {group.options.map((option, offset) => (
              <OptionRow
                key={option.key}
                id={jumpOptionId(id, start + offset)}
                option={option}
                isActive={start + offset === activeIndex}
                onActivate={onActivate}
                onPick={onPick}
              />
            ))}
          </ul>
        );
      })}
    </div>
  );
}
