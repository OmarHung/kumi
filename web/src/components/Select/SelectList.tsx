import { useCallback, useId, useLayoutEffect, useRef, type RefObject } from 'react';
import type { FloatingAlign } from '../../lib/useAnchoredPosition';
import { TextField } from '../Field/Field';
import { Floating } from '../Floating/Floating';
import { TRANSIENT_FOCUS_ATTRIBUTE } from '../../lib/focusable';
import { Icon } from '../Icon/Icon';
import type { SelectOption } from './Select';
import { filterStatus, useSettledQuery } from './selectFilter';
import { visibleGroupLabels } from './selectGroups';
import type { SelectFilterControls } from './useSelect';
import styles from './Select.module.css';

export function optionId(listId: string, index: number): string {
  return `${listId}-${String(index)}`;
}

/** 沒有選項（或篩選之後沒有符合的）時清單裡那一列說明的 id。 */
export function emptyId(listId: string): string {
  return `${listId}-empty`;
}

/** 反白的選項捲到看得到的地方：只捲清單自己（清單在 top layer，scrollIntoView 可能連後面的表格一起捲）。 */
function revealOption(list: HTMLElement | null, option: HTMLElement | null) {
  if (list === null || option === null) return;
  const box = list.getBoundingClientRect();
  const rect = option.getBoundingClientRect();
  if (rect.top < box.top) list.scrollTop -= box.top - rect.top;
  else if (rect.bottom > box.bottom) list.scrollTop += rect.bottom - box.bottom;
}

interface SelectEntryProps {
  readonly option: SelectOption;
  /** 這一項前面畫的組名：篩選時不一定是選項自己帶的（selectGroups.ts）。 */
  readonly groupLabel: string | undefined;
  readonly id: string;
  readonly isSelected: boolean;
  readonly isActive: boolean;
  readonly onPoint: () => void;
  readonly onChoose: () => void;
}

function SelectEntry({ option, groupLabel, id, isSelected, isActive, onPoint, onChoose }: SelectEntryProps) {
  const reason = option.disabledReason;
  return (
    <>
      {groupLabel !== undefined && (
        <li role="presentation" className={styles.groupLabel} data-group-label>
          {groupLabel}
        </li>
      )}
      <li
      id={id}
      role="option"
      className={styles.option}
      data-value={option.value}
      data-active={isActive || undefined}
      data-starts-group={option.startsGroup === true || undefined}
      aria-selected={isSelected}
      aria-disabled={reason !== undefined || undefined}
      aria-labelledby={`${id}-name`}
      aria-describedby={reason === undefined ? undefined : `${id}-reason`}
      onPointerMove={onPoint}
      onClick={onChoose}
    >
      {option.leading !== undefined && (
        <span className={styles.optionLeading} aria-hidden="true">
          {option.leading}
        </span>
      )}
      <span className={styles.optionText}>
        <span id={`${id}-name`}>{option.label}</span>
        {reason !== undefined && (
          <span id={`${id}-reason`} className={styles.reason}>
            {reason}
          </span>
        )}
      </span>
      <span className={styles.check}>{isSelected && <Icon name="check" />}</span>
      </li>
    </>
  );
}

/** 按鈕的名稱、說明與錯誤：清單用同一個名稱，篩選框帶著同樣的說明與錯誤。 */
export interface SelectListAria {
  readonly 'aria-label'?: string;
  readonly 'aria-labelledby'?: string;
  readonly 'aria-describedby'?: string;
  readonly 'aria-invalid'?: boolean;
}

interface SelectFilterProps {
  readonly listId: string;
  readonly activeId: string | undefined;
  readonly aria: SelectListAria;
  readonly inputRef: RefObject<HTMLInputElement | null>;
  readonly controls: SelectFilterControls;
}

/**
 * 篩選框（docs/DESIGN.md 7.3「選項很多的時候」）：做法照快速跳轉的 combobox——輸入框是 combobox、控制清單，
 * 反白用 aria-activedescendant。名稱是「篩選」加清單的名稱（hidden 的元素照樣算得進 aria-labelledby）；
 * 焦點在這裡時報讀軟體聽不到按鈕的說明與錯誤，所以一起帶過來。
 * TRANSIENT_FOCUS_ATTRIBUTE：焦點只是暫時在這裡（Tab 時先回到按鈕），對話框的焦點陷阱算頭尾時不算它。
 */
function SelectFilter({ listId, activeId, aria, inputRef, controls }: SelectFilterProps) {
  const wordId = useId();
  const nameId = useId();
  const label = aria['aria-label'];
  const labelledBy = aria['aria-labelledby'];
  return (
    <div className={styles.filter} {...{ [TRANSIENT_FOCUS_ATTRIBUTE]: '' }}>
      <span id={wordId} hidden>
        篩選
      </span>
      {labelledBy === undefined && (
        <span id={nameId} hidden>
          {label}
        </span>
      )}
      <TextField
        ref={inputRef}
        label="篩選"
        isLabelHidden
        size="sm"
        leadingIcon="search"
        type="text"
        role="combobox"
        aria-labelledby={`${wordId} ${labelledBy ?? nameId}`}
        aria-describedby={aria['aria-describedby']}
        aria-invalid={aria['aria-invalid'] === true || undefined}
        aria-expanded
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={activeId}
        placeholder="輸入文字篩選"
        autoComplete="off"
        spellCheck={false}
        value={controls.text}
        onChange={(event) => {
          controls.onTextChange(event.target.value);
        }}
        onCompositionStart={controls.onCompositionStart}
        onCompositionEnd={(event) => {
          controls.onCompositionEnd(event.currentTarget.value);
        }}
        onKeyDown={controls.onKeyDown}
      />
    </div>
  );
}

export interface SelectListProps {
  readonly listId: string;
  /** 看得到的選項（篩選之後的）。 */
  readonly visible: readonly SelectOption[];
  /** 看得到的每一個選項在完整清單裡的位置：選項的 id 照它編，篩選時同一個選項 id 不變。 */
  readonly positions: readonly number[];
  /** 有沒有任何選項（沒篩選前）：說明寫「沒有可以選的」還是「沒有符合的選項」。 */
  readonly hasAnyOption: boolean;
  readonly value: string;
  readonly active: number;
  /** 反白的選項的 id（沒有反白是 undefined）。 */
  readonly activeId: string | undefined;
  readonly align: FloatingAlign;
  readonly anchorRef: RefObject<HTMLButtonElement | null>;
  readonly aria: SelectListAria;
  /** 有篩選框時才有（選項 10 個以上）。 */
  readonly filter: { readonly controls: SelectFilterControls; readonly inputRef: RefObject<HTMLInputElement | null>; readonly all: readonly SelectOption[] } | null;
  readonly onPoint: (index: number) => void;
  readonly onChoose: (index: number) => void;
  readonly onAnchorHidden: () => void;
}

/**
 * 反白的選項捲到看得到：反白換了、選項換了、篩選字換了（結果換了但數量與反白的那一個沒變，清單照樣可能被捲走），
 * 以及 Floating 套上 maxHeight 之後（定位完成時清單才縮到實際的高度）。
 */
function useRevealActive(activeId: string | undefined, count: number, query: string) {
  const listRef = useRef<HTMLUListElement>(null);
  const reveal = useCallback(() => {
    revealOption(listRef.current, activeId === undefined ? null : document.getElementById(activeId));
  }, [activeId]);
  useLayoutEffect(() => {
    reveal();
  }, [reveal, count, query]);
  return { listRef, reveal };
}

/**
 * 打開時的清單：浮在 top layer（Floating），至少和按鈕一樣寬；名稱和按鈕一樣。在上面按下滑鼠不帶走焦點（keepsAnchorFocus）。
 * 沒有選項時清單裡是一列不能選的說明（role="option"、aria-disabled）：報讀軟體打開時聽得到，不是一個空的清單。
 * 有篩選框時，結果數在停下來之後用 aria-live="polite" 的區域唸（不用 role="status"：頁面上的 status 是全域的那一個）。
 */
export function SelectList(props: SelectListProps) {
  const { listId, visible: options, positions, hasAnyOption, value, active, activeId, align, anchorRef, aria, filter, onPoint, onChoose, onAnchorHidden } = props;
  const query = filter?.controls.query ?? '';
  const { listRef, reveal } = useRevealActive(activeId, options.length, query);
  const settled = useSettledQuery(query);
  const groupLabels = visibleGroupLabels(filter?.all ?? options, options.map((_, index) => positions[index] ?? index));
  return (
    <Floating anchorRef={anchorRef} align={align} matchAnchorWidth keepsAnchorFocus onAnchorHidden={onAnchorHidden} onPositioned={reveal} className={styles.surface}>
      <div className={styles.body}>
        {filter !== null && <SelectFilter listId={listId} activeId={activeId} aria={aria} inputRef={filter.inputRef} controls={filter.controls} />}
        <ul ref={listRef} id={listId} role="listbox" aria-label={aria['aria-label']} aria-labelledby={aria['aria-labelledby']} className={styles.list}>
          {options.map((option, index) => (
            <SelectEntry
              key={option.value}
              option={option}
              groupLabel={groupLabels[index]}
              id={optionId(listId, positions[index] ?? index)}
              isSelected={option.value === value}
              isActive={index === active}
              onPoint={() => {
                if (index !== active) onPoint(index);
              }}
              onChoose={() => {
                onChoose(index);
              }}
            />
          ))}
          {options.length === 0 && (
            <li id={emptyId(listId)} role="option" aria-disabled="true" aria-selected={false} className={styles.empty}>
              {hasAnyOption ? '沒有符合的選項' : '沒有可以選的'}
            </li>
          )}
        </ul>
        {filter !== null && (
          <p aria-live="polite" className="visually-hidden">
            {filterStatus(filter.all, settled)}
          </p>
        )}
      </div>
    </Floating>
  );
}
