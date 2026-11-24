import { useLayoutEffect, useRef, type RefObject } from 'react';
import type { Member } from '../../../api/types';
import { Avatar } from '../../../components/Avatar/Avatar';
import { Floating } from '../../../components/Floating/Floating';
import styles from './MentionList.module.css';

export function mentionOptionId(listId: string, index: number): string {
  return `${listId}-${String(index)}`;
}

/** 沒有符合的成員時，清單裡那一列說明的 id。 */
export function mentionEmptyId(listId: string): string {
  return `${listId}-empty`;
}

/** 反白的選項捲到看得到的地方：只捲清單自己（清單在 top layer，scrollIntoView 可能連後面的頁面一起捲）。 */
function revealOption(list: HTMLElement | null, option: HTMLElement | null): void {
  if (list === null || option === null) return;
  const box = list.getBoundingClientRect();
  const rect = option.getBoundingClientRect();
  if (rect.top < box.top) list.scrollTop -= box.top - rect.top;
  else if (rect.bottom > box.bottom) list.scrollTop += rect.bottom - box.bottom;
}

interface MentionListProps {
  readonly listId: string;
  readonly options: readonly Member[];
  /** 反白的是第幾個（沒有選項時沒有意義）。 */
  readonly active: number;
  /** 浮在哪個元素旁邊（文字框）。 */
  readonly anchorRef: RefObject<HTMLElement | null>;
  readonly onPoint: (index: number) => void;
  readonly onChoose: (index: number) => void;
  /** 文字框被捲出看得到的範圍時：關上。要是固定的函式。 */
  readonly onAnchorHidden: () => void;
}

/**
 * 打 @ 時的成員清單（WAI-ARIA 的 listbox，焦點一直在文字框，反白由文字框的 aria-activedescendant 指著；文字框本身不是 combobox）。
 * 長相和 Select、Menu 的清單一樣：浮在 top layer（Floating），選項是頭像加名字，反白是實心的強調色底；
 * 在清單上按下滑鼠不帶走文字框的焦點（keepsAnchorFocus）。沒有符合的成員時是一列不能選的說明。
 */
export function MentionList({ listId, options, active, anchorRef, onPoint, onChoose, onAnchorHidden }: MentionListProps) {
  const listRef = useRef<HTMLUListElement>(null);
  useLayoutEffect(() => {
    revealOption(listRef.current, document.getElementById(mentionOptionId(listId, active)));
  }, [listId, active, options]);
  return (
    <Floating anchorRef={anchorRef} align="start" keepsAnchorFocus onAnchorHidden={onAnchorHidden} className={styles.surface}>
      <ul ref={listRef} id={listId} role="listbox" aria-label="提及成員" className={styles.list}>
        {options.map((member, index) => (
          <li
            key={member.personId}
            id={mentionOptionId(listId, index)}
            role="option"
            aria-selected={index === active}
            data-active={index === active || undefined}
            className={styles.option}
            onPointerMove={() => {
              if (index !== active) onPoint(index);
            }}
            onClick={() => {
              onChoose(index);
            }}
          >
            <Avatar name={member.displayName} size="sm" decorative />
            <span className={styles.name}>{member.displayName}</span>
          </li>
        ))}
        {options.length === 0 && (
          <li id={mentionEmptyId(listId)} role="option" aria-disabled="true" aria-selected={false} className={styles.empty}>
            沒有符合的成員
          </li>
        )}
      </ul>
    </Floating>
  );
}
