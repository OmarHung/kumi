import { useId, useState, type KeyboardEvent } from 'react';
import { useAssistantItemChoices } from '../../api/assistant';
import { useItem } from '../../api/queries';
import type { Item } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { TextField } from '../../components/Field/Field';
import { Icon } from '../../components/Icon/Icon';
import { Popover } from '../../components/Popover/Popover';
import { cx } from '../../lib/cx';
import { isComposingKey } from '../../lib/keyboard';
import { useSettledText } from '../jump/useSettledText';
import styles from './ItemPicker.module.css';

const NONE_LABEL = '不指定項目';
const NONE_KEY = '';

interface ChoiceListProps {
  readonly projectKey: string;
  readonly currentKey: string | null;
  readonly onChoose: (itemKey: string | null) => void;
}

interface Choice {
  readonly key: string;
  readonly title: string | null;
}

function optionId(listId: string, key: string): string {
  return `${listId}-${key === NONE_KEY ? 'none' : key}`;
}

/**
 * 清單的選項：「不指定」、（沒打字時）目前的項目固定在它下面、然後是搜尋結果。
 * 目前的項目不在搜尋結果的前幾筆時，這樣也看得到、打得了勾。
 */
function choicesOf(items: readonly Item[], pinned: Choice | null): readonly Choice[] {
  const results = items.map((item) => ({ key: item.key, title: item.title }));
  const rest = pinned === null ? results : results.filter((choice) => choice.key !== pinned.key);
  return [{ key: NONE_KEY, title: null }, ...(pinned === null ? [] : [pinned]), ...rest];
}

interface ChoiceKeys {
  readonly active: Choice | undefined;
  readonly setActiveKey: (key: string | null) => void;
  readonly handleKeyDown: (event: KeyboardEvent<HTMLInputElement>) => void;
}

interface KeyOptions {
  readonly choices: readonly Choice[];
  /** 沒動過時選起來的：沒打字是目前的項目；打了字是第一個結果。 */
  readonly preferredKey: string;
  /** 結果對得上現在打的字、沒有在讀：Enter 才會選。打了字但還沒有結果、或 0 筆時不做事（不把「不指定」當預設）。 */
  readonly canCommit: boolean;
  readonly onChoose: (itemKey: string | null) => void;
}

/** 選起來的那一個（記 key；結果換了不在就退回 preferredKey）與上下鍵、Enter。輸入法組字中的按鍵都是輸入法的。 */
function useChoiceKeys({ choices, preferredKey, canCommit, onChoose }: KeyOptions): ChoiceKeys {
  const [activeKey, setActiveKey] = useState<string | null>(null);
  const active = choices.find((choice) => choice.key === activeKey) ?? choices.find((choice) => choice.key === preferredKey) ?? choices[0];
  const move = (delta: number) => {
    const at = active === undefined ? 0 : choices.indexOf(active);
    const next = choices[(at + delta + choices.length) % choices.length];
    if (next !== undefined) setActiveKey(next.key);
  };
  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (isComposingKey(event)) {
      // 組字中的 Esc 是取消選字：不讓它往外傳把浮層關掉。
      if (event.key === 'Escape') event.stopPropagation();
      return;
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      move(event.key === 'ArrowDown' ? 1 : -1);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      if (canCommit && active !== undefined) onChoose(active.key === NONE_KEY ? null : active.key);
    }
  };
  return { active, setActiveKey, handleKeyDown };
}

interface OptionProps {
  readonly id: string;
  readonly choice: Choice;
  readonly isActive: boolean;
  readonly isCurrent: boolean;
  readonly onPoint: () => void;
  readonly onPick: () => void;
}

function ChoiceOption({ id, choice, isActive, isCurrent, onPoint, onPick }: OptionProps) {
  return (
    <li id={id} role="option" aria-selected={isActive} className={styles.option} onPointerMove={onPoint} onClick={onPick}>
      {choice.key === NONE_KEY ? (
        <span className={styles.title}>{NONE_LABEL}</span>
      ) : (
        <>
          <span className={styles.code}>{choice.key}</span> <span className={styles.title}>{choice.title}</span>
        </>
      )}
      {isCurrent && (
        <>
          <span className="visually-hidden">（目前）</span>
          <Icon name="check" className={styles.check} />
        </>
      )}
    </li>
  );
}

interface Found {
  readonly isPending: boolean;
  readonly isError: boolean;
  readonly isPlaceholderData: boolean;
  readonly resultCount: number;
}

function statusText(found: Found, hasText: boolean, isSettled: boolean): string | null {
  if (found.isError) return '搜尋失敗，請再試一次。';
  if (found.isPending || (hasText && (!isSettled || found.isPlaceholderData))) return '正在搜尋…';
  return hasText && found.resultCount === 0 ? '找不到符合的項目。' : null;
}

/** 目前的項目放在清單最上面要有標題：讀不到（還在讀、沒權限）只寫編號。 */
function usePinned(currentKey: string | null, hasText: boolean): Choice | null {
  const item = useItem(currentKey ?? '', currentKey !== null && !hasText);
  if (currentKey === null || hasText) return null;
  return { key: currentKey, title: item.data?.title ?? null };
}

interface Search {
  readonly choices: readonly Choice[];
  readonly canCommit: boolean;
  readonly preferredKey: string;
  readonly status: string | null;
}

/** 搜尋：打字停下來才送；結果、能不能 Enter 選、一開始選起來的、狀態文字都在這裡算。 */
function useChoiceSearch(projectKey: string, currentKey: string | null, text: string): Search {
  const typed = text.trim();
  const settled = useSettledText(typed);
  const found = useAssistantItemChoices(projectKey, settled);
  const choices = choicesOf(found.data ?? [], usePinned(currentKey, typed !== ''));
  const isSettled = settled === typed;
  const isFresh = !found.isPending && !found.isPlaceholderData && !found.isError;
  const resultCount = found.data?.length ?? 0;
  return {
    choices,
    // 打了字：要等搜尋停下來、而且結果是這個字的（不是上一份留著的）、至少一筆才算數；沒打字隨時可以選。
    canCommit: typed === '' || (isSettled && isFresh && resultCount > 0),
    preferredKey: typed === '' ? (currentKey ?? NONE_KEY) : (choices[1]?.key ?? NONE_KEY),
    status: statusText({ isPending: found.isPending, isError: found.isError, isPlaceholderData: found.isPlaceholderData, resultCount }, typed !== '', isSettled),
  };
}

/** 搜尋框加結果：焦點一直在輸入框，選起來的那一個用 aria-activedescendant 表示（和快速跳轉同一種 combobox）。 */
function ChoiceList({ projectKey, currentKey, onChoose }: ChoiceListProps) {
  const listId = useId();
  const [text, setText] = useState('');
  const { choices, canCommit, preferredKey, status } = useChoiceSearch(projectKey, currentKey, text);
  const { active, setActiveKey, handleKeyDown } = useChoiceKeys({ choices, preferredKey, canCommit, onChoose });
  return (
    <div className={styles.picker}>
      <TextField
        label="搜尋項目"
        isLabelHidden
        size="sm"
        leadingIcon="search"
        placeholder="編號或標題"
        role="combobox"
        aria-expanded="true"
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={active === undefined ? undefined : optionId(listId, active.key)}
        autoComplete="off"
        value={text}
        onChange={(event) => {
          setText(event.target.value);
          setActiveKey(null);
        }}
        onKeyDown={handleKeyDown}
      />
      <ul id={listId} role="listbox" aria-label="項目" className={styles.list}>
        {choices.map((choice) => (
          <ChoiceOption
            key={choice.key}
            id={optionId(listId, choice.key)}
            choice={choice}
            isActive={choice.key === active?.key}
            isCurrent={choice.key === NONE_KEY ? currentKey === null : choice.key === currentKey}
            onPoint={() => {
              setActiveKey(choice.key);
            }}
            onPick={() => {
              onChoose(choice.key === NONE_KEY ? null : choice.key);
            }}
          />
        ))}
      </ul>
      <p className={styles.note} aria-live="polite">
        {status}
      </p>
    </div>
  );
}

interface ItemPickerProps {
  readonly projectKey: string;
  readonly itemKey: string | null;
  readonly onChange: (itemKey: string | null) => void;
  /** compact：頁列上用（按鈕有固定的寬度上限，標題長也不把頁列的標題擠掉）；預設撐到外層的寬度。 */
  readonly size?: 'fill' | 'compact';
}

/** 按鈕上的字：選了是「WEB-12 標題」，沒選是「不指定項目」。標題讀不到（沒權限、還在讀）只寫編號。 */
function TriggerText({ itemKey }: { readonly itemKey: string }) {
  const item = useItem(itemKey);
  return (
    <>
      <span className={styles.code}>{itemKey}</span>
      {item.data !== undefined && <span className={cx(styles.triggerTitle, styles.itemTitle)}>{item.data.title}</span>}
    </>
  );
}

/**
 * 選助理要針對的項目（面板的頁首第二列、整頁的頁首）：按鈕打開一個浮層，上面是搜尋框（這個專案的編號或標題），
 * 下面是結果，最前面是「不指定項目」。選了就換、浮層關上、焦點回到按鈕。
 */
export function ItemPicker({ projectKey, itemKey, onChange, size = 'fill' }: ItemPickerProps) {
  return (
    <Popover
      label="選擇項目"
      renderTrigger={(trigger) => (
        <Button {...trigger} size="sm" className={styles.trigger} data-size={size} aria-label={itemKey === null ? '項目：不指定' : `項目：${itemKey}`}>
          <Icon name="link" />
          {itemKey === null ? <span className={styles.triggerTitle}>{NONE_LABEL}</span> : <TriggerText itemKey={itemKey} />}
        </Button>
      )}
    >
      {({ close }) => (
        <ChoiceList
          projectKey={projectKey}
          currentKey={itemKey}
          onChoose={(next) => {
            onChange(next);
            close();
          }}
        />
      )}
    </Popover>
  );
}
